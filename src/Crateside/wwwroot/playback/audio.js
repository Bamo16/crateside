// Loading, playing and the click: one decoded file through Web Audio, the click scheduled on the same clock (ADR 0094).

import { LOOKAHEAD, START_DELAY, MUSIC_TARGET_LUFS, MUSIC_FALLBACK_DB, clamp } from "./shared.js";
import { SOUNDS } from "./sounds.js";

export const AudioEngine = (Base) =>
    class extends Base {
        ctx = null;
        master = null;
        music = null;
        musicDb = MUSIC_FALLBACK_DB;
        buffer = null;
        bufferId = null;
        loading = null;
        failure = null;
        wantPlay = false;
        playing = false;
        parked = 0;
        session = null;
        scheduler = null;
        clickOn = false;

        ensureContext() {
            if (this.ctx && this.ctx.state !== "closed")
                return;

            this.ctx = new AudioContext({ latencyHint: "interactive" });
            this.master = this.ctx.createGain();
            this.master.gain.value = this.settings.volume;
            this.master.connect(this.ctx.destination);
            this.music = this.ctx.createGain();
            this.music.connect(this.master);
        }

        async load(id) {
            this.ensureContext();

            if (this.bufferId === id && this.buffer)
                return true;

            this.failure = null;

            const token = {};
            this.loading = token;
            this.emit();

            try {
                const job = this.jobFor(id);

                this.job = null;

                const { decoded, measured } = await job;

                if (this.loading !== token)
                    return false;

                const musicDb = await Promise.race([measured, new Promise((resolve) => setTimeout(resolve, 20000, MUSIC_FALLBACK_DB))]);

                if (this.loading !== token)
                    return false;

                this.buffer = decoded;
                this.bufferId = id;
                this.musicDb = musicDb;
                this.duration = this.duration || decoded.duration;

                if (this.window.end <= 0)
                    this.window = { start: 0, end: this.duration };

                this.drawWave();
            } catch (error) {
                if (this.loading !== token)
                    return false;

                this.failure = { id, reason: error.message ?? "the file could not be read." };
                this.wantPlay = false;
                this.loading = null;
                this.emit();

                return false;
            }

            this.loading = null;
            this.emit();

            return true;
        }

        // The fetch and decode of a file, started once: the page starts the first file's as it opens, so a play
        // that follows finds it done or under way.
        jobFor(id) {
            const key = `${this.trackKey}/${id}`;

            if (this.job?.key === key)
                return this.job.promise;

            const url = `audio/${encodeURIComponent(this.trackKey)}/${encodeURIComponent(id)}`;
            const measured = fetch(`${url}/loudness`)
                .then((reply) => (reply.ok ? reply.json() : null))
                .then((reading) => (typeof reading?.lufs === "number" ? Math.min(0, MUSIC_TARGET_LUFS - reading.lufs) : MUSIC_FALLBACK_DB))
                .catch(() => MUSIC_FALLBACK_DB);
            const promise = this.fetchAudio(url).then(async (response) => ({
                decoded: await this.ctx
                    .decodeAudioData(await response.arrayBuffer())
                    .catch(() => Promise.reject(new Error("the file could not be read as audio."))),
                measured,
            }));

            this.job = { key, promise };
            promise.catch(() => {
                if (this.job?.promise === promise)
                    this.job = null;
            });

            return promise;
        }

        prefetch() {
            if (!this.fileId || this.buffer || this.job || this.loading || this.failure || !this.files.length)
                return;

            this.ensureContext();
            this.jobFor(this.fileId);
        }

        // Asked up to three times: a server that has just come back, or is reading its rows again, answers
        // the first. Only a 404 that stands says the file is gone; any other failure says crateside did not answer.
        async fetchAudio(url) {
            let last = null;

            for (let attempt = 0; attempt < 3; attempt++) {
                if (attempt > 0)
                    await new Promise((resolve) => setTimeout(resolve, 600 * attempt));

                try {
                    const response = await fetch(url);

                    if (response.ok)
                        return response;

                    last = response;

                    if (response.status < 500 && response.status !== 404)
                        break;
                } catch {
                    last = null;
                }
            }

            if (last?.status === 404) {
                const problem = await last.json().catch(() => null);

                throw new Error(problem?.detail ?? "the file moved or is gone; try again in a moment.");
            }

            throw new Error(last ? `crateside answered ${last.status}; try again in a moment.` : "crateside did not answer; try again in a moment.");
        }

        async play() {
            if (!this.fileId || this.playing)
                return;

            this.wantPlay = true;

            if (!(await this.load(this.fileId)) || !this.wantPlay)
                return;

            await this.ctx.resume();
            this.begin(this.parked);
        }

        pause() {
            this.wantPlay = false;

            if (this.playing) {
                this.parked = this.position();
                this.stopAudio();
                this.playing = false;
                this.emit();
            }

            if (this.loading) {
                this.loading = null;
                this.emit();
            }
        }

        toggle() {
            if (!this.files.length)
                return;

            if (this.playing || this.loading || this.wantPlay)
                this.pause();
            else
                this.play();
        }

        begin(at) {
            this.stopAudio();

            const start = clamp(at, 0, this.buffer.duration - 0.001);
            const source = this.ctx.createBufferSource();

            source.buffer = this.buffer;
            this.music.gain.value = 10 ** (this.musicDb / 20);
            source.connect(this.music);

            if (this.loopOn && this.window.end > this.window.start) {
                source.loop = true;
                source.loopStart = this.window.start;
                source.loopEnd = Math.min(this.window.end, this.buffer.duration);
            }

            const when = this.ctx.currentTime + START_DELAY;

            source.onended = () => {
                if (this.session?.source === source && !source.loop) {
                    this.parked = 0;
                    this.playing = false;
                    this.session = null;
                    this.stopClicks();
                    this.emit();
                }
            };
            source.start(when, start);
            this.session = {
                source,
                ctxStart: when,
                mediaStart: start,
                loop: source.loop ? { start: source.loopStart, end: source.loopEnd } : null,
                scheduledTo: when,
                clicks: new Set(),
            };
            this.playing = true;
            this.wantPlay = false;
            this.ensureScheduler();
            this.emit();
        }

        stopAudio() {
            if (this.session) {
                const { source } = this.session;

                source.onended = null;

                try {
                    source.stop();
                } catch {
                    // Never started, or already over.
                }

                this.stopClicks();
                this.session = null;
            }

            if (this.scheduler) {
                clearInterval(this.scheduler);
                this.scheduler = null;
            }
        }

        stopClicks() {
            for (const node of this.session?.clicks ?? []) {
                try {
                    node.stop();
                } catch {
                    // Already over.
                }
            }

            this.session?.clicks.clear();
        }

        // The restart a change to the loop, the file or the position needs: same place, new session.
        restart() {
            if (!this.playing || !this.buffer)
                return;

            const at = this.position();

            this.begin(at);
        }

        position() {
            if (!this.playing || !this.session)
                return this.parked;

            const { ctxStart, mediaStart, loop } = this.session;
            const heard = this.ctx.currentTime - ctxStart - (this.ctx.outputLatency || 0);
            const media = mediaStart + Math.max(0, heard);

            if (!loop || media < loop.end)
                return Math.min(media, this.buffer.duration);

            return loop.start + ((media - loop.end) % (loop.end - loop.start));
        }

        seek(at, { play = false } = {}) {
            const target = clamp(at, 0, this.duration || Infinity);

            this.parked = target;

            if (this.playing)
                this.begin(target);
            else if (play)
                this.play();

            this.followIntoView(target);
            this.paintPlayhead();
        }

        async selectFile(id, { at = null, play = false } = {}) {
            if (id === this.fileId && !this.failure && at === null)
                return;

            const wasPlaying = this.playing || this.wantPlay || play;

            this.failure = null;

            if (at !== null)
                this.parked = at;
            else if (this.playing)
                this.parked = this.position();

            this.stopAudio();
            this.playing = false;
            this.fileId = id;
            this.buffer = this.bufferId = null;
            this.loading = null;
            this.emit();

            if (wasPlaying)
                await this.play();
        }

        retry() {
            this.failure = null;
            this.emit();
            this.play();
        }

        ensureScheduler() {
            if (this.scheduler)
                clearInterval(this.scheduler);

            this.scheduler = setInterval(() => this.scheduleClicks(), 25);
            this.scheduleClicks();
        }

        segmentAt(at) {
            const { ctxStart, mediaStart, loop } = this.session;
            const end = loop ? loop.end : this.buffer.duration;
            const first = end - mediaStart;

            if (!loop || at - ctxStart < first)
                return { ctx0: ctxStart, a: mediaStart, b: end };

            const length = loop.end - loop.start;
            const n = Math.floor((at - ctxStart - first) / length);

            return { ctx0: ctxStart + first + n * length, a: loop.start, b: loop.end };
        }

        scheduleClicks() {
            if (!this.playing || !this.session || !this.clickOn || !this.grid) {
                if (this.session)
                    this.session.scheduledTo = Math.max(this.session.scheduledTo, this.ctx.currentTime);

                return;
            }

            const now = this.ctx.currentTime;
            const horizon = now + LOOKAHEAD;
            const session = this.session;

            session.scheduledTo = Math.max(session.scheduledTo, now);

            for (let guard = 0; guard < 8 && session.scheduledTo < horizon; guard++) {
                const from = session.scheduledTo;
                const segment = this.segmentAt(from + 1e-7);
                const segmentEnd = segment.ctx0 + (segment.b - segment.a);
                const to = Math.min(horizon, segmentEnd);
                const mediaFrom = segment.a + (from - segment.ctx0);
                const mediaTo = mediaFrom + (to - from);

                this.clickBeats(mediaFrom, mediaTo, (media, accent) => {
                    const when = segment.ctx0 + (media - segment.a);

                    if (when >= now)
                        this.sound(when, accent);
                });

                if (to <= from) {
                    if (!session.loop)
                        break;

                    session.scheduledTo = segmentEnd + 1e-7;
                } else {
                    session.scheduledTo = to;
                }
            }
        }

        clickBeats(from, to, each) {
            const { grid } = this;
            const first = Math.ceil(grid.beatAt(from) / grid.unit - 1e-9);

            for (let k = first; ; k++) {
                const beat = k * grid.unit;
                const media = grid.secondsAt(beat);

                if (media >= to - 1e-9)
                    break;

                if (media >= from - 1e-9)
                    each(media, Math.abs(beat / grid.perBar - Math.round(beat / grid.perBar)) < 1e-6);
            }
        }

        sound(when, accent) {
            const node = SOUNDS[this.settings.sound](this.ctx, this.master, when, accent, this.settings.clickLevel);

            this.session?.clicks.add(node);
            node.onended = () => this.session?.clicks.delete(node);
        }

        preview() {
            this.ensureContext();
            this.ctx.resume();

            const now = this.ctx.currentTime + 0.03;

            this.previewNodes?.forEach((node) => {
                try {
                    node.stop();
                } catch {
                    // Already over.
                }
            });
            this.previewNodes = [
                SOUNDS[this.settings.sound](this.ctx, this.master, now, true, this.settings.clickLevel),
                SOUNDS[this.settings.sound](this.ctx, this.master, now + 0.32, false, this.settings.clickLevel),
            ];
        }

        // The music keeps playing: only the clicks already scheduled are dropped, and the next ones use the new voice.
        swapClickVoice() {
            if (!this.session)
                return;

            this.stopClicks();
            this.session.scheduledTo = this.ctx.currentTime;
            this.scheduleClicks();
        }

        setClick(on) {
            this.clickOn = on && Boolean(this.grid);

            if (this.clickOn && this.playing && this.session)
                this.session.scheduledTo = this.ctx.currentTime;

            this.syncPlayer();
        }
    };
