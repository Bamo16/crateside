// Playback on Track details (#105, ADR 0094): one file at a time, decoded with Web Audio and played
// from memory, with a synthesized click scheduled on the same clock. The player strip and the warp
// view are Blazor components; this module owns everything that moves at the pointer's or the audio's
// pace (the playhead, seeking, the view's window) and tells them only what they must draw.

const SNAP = 1e-6;
const LOOKAHEAD = 0.15;
const START_DELAY = 0.06;
const MIN_WINDOW_SECONDS = 4;
const DRAG_THRESHOLD = 3;
const SETTINGS_KEY = "crateside.playback";
// The music is leveled to this loudness (LUFS) and never raised, so the click, at full scale on top,
// stays clear of a loud master; a file with no reading is lowered by the fallback instead.
const MUSIC_TARGET_LUFS = -18;
const MUSIC_FALLBACK_DB = -6;

const clamp = (value, low, high) => Math.min(high, Math.max(low, value));

// What the rows are drawn with while the window is dragged (WarpView.razor's constants and helpers).
const COLUMNS = 832;
const PLOT_PIXELS = 832;
const FLAG_ROOM = 9;
const xOf = (view, at) => ((at - view.from) / (view.to - view.from)) * 100;
const clampX = (view, at) => clamp(xOf(view, at), 0, 100);
const pct = (value) => String(Math.round(value * 1000) / 1000);
const px = (pixels) => (pixels / PLOT_PIXELS) * 100;
const escapeHtml = (text) => String(text).replace(/[&<>"]/g, (char) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[char]);

// ---- the warp's grid ------------------------------------------------------------------------

function makeGrid(source) {
    const markers = (source?.markers ?? []).filter(
        (marker, i, all) => i === 0 || (marker[0] > all[i - 1][0] && marker[1] > all[i - 1][1]),
    );

    if (markers.length < 2)
        return null;

    const seconds = markers.map((marker) => marker[0]);
    const beats = markers.map((marker) => marker[1]);
    const perBar = (source.numerator * 4) / source.denominator;
    const unit = 4 / source.denominator;

    const beatAt = (at) => {
        let i = 1;

        while (i < seconds.length - 1 && at > seconds[i])
            i++;

        return beats[i - 1] + ((at - seconds[i - 1]) * (beats[i] - beats[i - 1])) / (seconds[i] - seconds[i - 1]);
    };

    const secondsAt = (beat) => {
        let i = 1;

        while (i < beats.length - 1 && beat > beats[i])
            i++;

        return seconds[i - 1] + ((beat - beats[i - 1]) * (seconds[i] - seconds[i - 1])) / (beats[i] - beats[i - 1]);
    };

    const indexAt = (at) => Math.floor(beatAt(at) / perBar + SNAP);
    const number = (index) => (index >= 0 ? index + 1 : index);

    return {
        perBar,
        unit,
        numerator: source.numerator,
        beatAt,
        secondsAt,
        indexAt,
        number,
        startOf: (index) => secondsAt(index * perBar),
        barAt: (at) => number(indexAt(at)),
        // Live's Start field: bar.beat.sixteenth, the way the player reads it out.
        position(at) {
            const beat = beatAt(at);
            const index = Math.floor(beat / perBar + SNAP);
            const within = beat - index * perBar;
            const inBar = Math.floor(within / unit + SNAP);
            const sixteenth = Math.floor((within - inBar * unit) / 0.25 + SNAP);

            return `${number(index)}.${inBar + 1}.${sixteenth + 1}`;
        },
    };
}

// ---- formatting -----------------------------------------------------------------------------

const two = (value) => String(value).padStart(2, "0");

function clock(at, hundredths) {
    const whole = Math.max(0, hundredths ? at : Math.floor(at));
    const minutes = Math.floor(whole / 60);

    return hundredths
        ? `${minutes}:${two(Math.floor(whole % 60))}.${two(Math.floor((whole % 1) * 100))}`
        : `${minutes}:${two(Math.floor(whole % 60))}`;
}

function readSettings() {
    try {
        return JSON.parse(localStorage.getItem(SETTINGS_KEY) ?? "{}");
    } catch {
        return {};
    }
}

function writeSettings(settings) {
    try {
        localStorage.setItem(SETTINGS_KEY, JSON.stringify(settings));
    } catch {
        // Private windows and blocked site data keep nothing; the page works without.
    }
}

// ---- the click's sounds ---------------------------------------------------------------------

// Each is a short transient: a noise tick on the attack and, except Click, a brief tone behind it,
// the body of Classic and Click between 1 and 1.5 kHz as Live's are. At full level the two sum to full scale.
const SOUNDS = {
    classic: (ctx, out, when, accent, level) => {
        tick(ctx, out, when, accent ? 0.22 : 0.18, level, 1200);

        return tone(ctx, out, when, accent ? 1500 : 1000, 0.04, "triangle", accent ? 0.95 : 0.78, level);
    },
    click: (ctx, out, when, accent, level) => noise(ctx, out, when, accent ? 1500 : 1100, 0.012, accent ? 2.6 : 2.1, level, 3),
    wood: (ctx, out, when, accent, level) => {
        tick(ctx, out, when, accent ? 0.38 : 0.3, level);

        return tone(ctx, out, when, accent ? 1500 : 1000, 0.05, "sine", accent ? 0.62 : 0.5, level, 0.7);
    },
};

function tick(ctx, out, when, loudness, level, cutoff = 4000) {
    const frames = Math.ceil(ctx.sampleRate * 0.005);
    const buffer = ctx.createBuffer(1, frames, ctx.sampleRate);
    const data = buffer.getChannelData(0);

    for (let i = 0; i < frames; i++)
        data[i] = (Math.random() * 2 - 1) * (1 - i / frames) ** 2;

    const source = ctx.createBufferSource();
    const filter = ctx.createBiquadFilter();
    const gain = ctx.createGain();

    source.buffer = buffer;
    filter.type = "highpass";
    filter.frequency.value = cutoff;
    gain.gain.value = Math.max(0.0002, loudness * level);
    source.connect(filter).connect(gain).connect(out);
    source.start(when);
}

function tone(ctx, out, when, hertz, length, type, loudness, level, drop = 1) {
    const oscillator = ctx.createOscillator();
    const gain = ctx.createGain();

    oscillator.type = type;
    oscillator.frequency.setValueAtTime(hertz, when);
    oscillator.frequency.exponentialRampToValueAtTime(hertz * drop, when + length);
    gain.gain.setValueAtTime(0.0001, when);
    gain.gain.exponentialRampToValueAtTime(Math.max(0.0002, loudness * level), when + 0.0005);
    gain.gain.exponentialRampToValueAtTime(0.0001, when + length);
    oscillator.connect(gain).connect(out);
    oscillator.start(when);
    oscillator.stop(when + length + 0.02);

    return oscillator;
}

function noise(ctx, out, when, hertz, length, loudness, level, q = 1.2) {
    const frames = Math.ceil(ctx.sampleRate * length);
    const buffer = ctx.createBuffer(1, frames, ctx.sampleRate);
    const data = buffer.getChannelData(0);

    for (let i = 0; i < frames; i++)
        data[i] = (Math.random() * 2 - 1) * (1 - i / frames);

    const source = ctx.createBufferSource();
    const filter = ctx.createBiquadFilter();
    const gain = ctx.createGain();

    source.buffer = buffer;
    filter.type = "bandpass";
    filter.frequency.value = hertz;
    filter.Q.value = q;
    gain.gain.value = Math.max(0.0002, loudness * level * 2.2);
    source.connect(filter).connect(gain).connect(out);
    source.start(when);

    return source;
}

// ---- the engine -----------------------------------------------------------------------------

class Playback {
    settings = { volume: 0.8, clickLevel: 0.6, sound: "classic", ...readSettings() };

    trackKey = null;
    files = [];
    fileId = null;
    duration = 0;
    grid = null;

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
    loopOn = false;
    window = { start: 0, end: 0 };
    rememberedZoom = null;
    leftBehind = false;
    drag = null;
    dragging = null;
    live = null;
    liveLayers = null;
    liveShown = null;
    liveTimer = 0;

    player = null;
    view = null;
    els = {};
    frame = 0;

    // ---- mounting ----

    mountPlayer(root, dotnet) {
        // A page drawn again on a new circuit (the old one was lost) keeps what is decoded and where it was.
        if (this.player) {
            this.player.abort.abort();
            this.player.observer?.disconnect();
        }

        this.player = { root, dotnet, abort: new AbortController() };

        const { signal } = this.player.abort;

        root.addEventListener("click", (event) => this.onPlayerClick(event), { signal });
        root.addEventListener("input", (event) => this.onPlayerInput(event), { signal });
        root.addEventListener("pointerdown", (event) => this.onWaveDown(event), { signal });
        document.addEventListener("keydown", (event) => this.onKey(event), { signal });
        document.addEventListener(
            "click",
            (event) => {
                if (this.els.menu && !event.target.closest("[data-click-sound], [data-click-menu]"))
                    this.els.menu.hidden = true;
            },
            { signal },
        );

        const sentinel = root.querySelector("[data-sentinel]");

        if (sentinel && "IntersectionObserver" in window) {
            const offset = Number.parseFloat(getComputedStyle(document.documentElement).getPropertyValue("--pin-nav")) || 0;

            this.player.observer = new IntersectionObserver(
                ([entry]) => {
                    const stuck = !entry.isIntersecting && entry.boundingClientRect.top < offset;
                    root.querySelector(".player")?.classList.toggle("stuck", stuck);
                },
                { rootMargin: `-${offset}px 0px 0px 0px` },
            );
            this.player.observer.observe(sentinel);
        }

        this.syncPlayer();
        this.startFrames();
        this.emit();
    }

    unmountPlayer() {
        if (!this.player)
            return;

        this.player.abort.abort();
        this.player.observer?.disconnect();
        this.player = null;
        this.stopAudio();
        this.playing = false;
        this.wantPlay = false;
        this.buffer = this.bufferId = this.loading = null;
        this.ctx?.close().catch(() => {});
        this.ctx = this.master = this.music = null;
        this.trackKey = null;
        this.files = [];
        this.fileId = null;
        this.duration = 0;
        this.grid = null;
        this.snapCache = null;
        this.parked = 0;
        this.failure = null;
        this.loopOn = this.clickOn = false;
        this.leftBehind = false;
        this.rememberedZoom = null;
        this.window = { start: 0, end: 0 };
    }

    mountView(root, dotnet) {
        this.unmountView();
        this.view = { root, dotnet, abort: new AbortController(), inFlight: false, pending: null };

        const { signal } = this.view.abort;

        root.addEventListener("click", (event) => this.onViewClick(event), { signal });
        root.addEventListener("pointermove", (event) => this.onViewMove(event), { signal });
        root.addEventListener("pointerleave", () => this.hideSeekLine(), { signal });
        root.addEventListener("pointerdown", (event) => this.onOverviewDown(event), { signal });
        root.addEventListener("dblclick", (event) => this.onOverviewDouble(event), { signal });
        root.addEventListener("keydown", (event) => this.onOverviewKey(event), { signal });
        root.addEventListener("keyup", (event) => this.onOverviewKeyUp(event), { signal });

        this.observer = new ResizeObserver(() => this.layoutField());
        this.observer.observe(root);
        this.startFrames();
    }

    unmountView() {
        if (!this.view)
            return;

        this.stopLive();
        this.view.abort.abort();
        this.observer?.disconnect();
        this.view = null;
        this.drag = null;
        document.body.classList.remove("pb-locked", "pb-locked-start", "pb-locked-end", "pb-locked-body");
    }

    // ---- what the components tell it ----

    setTrack(trackKey, files, defaultId) {
        const before = `${this.trackKey}|${this.fileId}`;

        if (this.trackKey !== trackKey) {
            this.stopAudio();
            this.playing = this.wantPlay = false;
            this.buffer = this.bufferId = this.loading = null;
            this.parked = 0;
            this.failure = null;
            this.loopOn = false;
            this.rememberedZoom = null;
            this.fileId = null;

            // The warp view may have told its grid for this track before the player told its name.
            if (this.trackKey !== null) {
                this.duration = 0;
                this.grid = null;
                this.snapCache = null;
                this.window = { start: 0, end: 0 };
                this.leftBehind = false;
            }

            this.trackKey = trackKey;
        }

        this.files = files;

        if (!files.some((file) => file.id === this.fileId))
            this.fileId = defaultId ?? files[0]?.id ?? null;

        this.syncPlayer();

        // The page redraws on every state it is told, and tells this on every redraw.
        if (before !== `${this.trackKey}|${this.fileId}`)
            this.emit();
    }

    setGrid(source, duration, laneFiles) {
        const first = this.duration === 0;

        this.grid = makeGrid(source);
        this.duration = duration || this.buffer?.duration || 0;
        this.snapCache = null;

        if (first || this.window.end <= 0 || this.window.end > this.duration + SNAP)
            this.window = { start: 0, end: this.duration };

        this.laneFiles = laneFiles ?? [];
        this.syncPlayer();
        this.viewRendered();
    }

    // The warp view has drawn: put back everything that lives on the elements it drew.
    viewRendered() {
        this.stopLive();
        this.findViewElements();
        this.layoutField();
        this.paintWindow();
        this.paintLanes();
        this.copyShape();
    }

    // Until the file is decoded, the slim waveform borrows the overview strip's shape.
    copyShape() {
        const source = this.view?.root.querySelector("[data-overview] svg path");
        const target = this.player?.root.querySelector("[data-shape] path");

        if (source && target)
            target.setAttribute("d", source.getAttribute("d") ?? "");
    }

    emit() {
        this.player?.dotnet
            .invokeMethodAsync(
                "OnState",
                this.playing,
                Boolean(this.loading),
                this.fileId,
                this.failure ? { id: this.failure.id, reason: this.failure.reason } : null,
                Boolean(this.grid),
            )
            .catch(() => {});
    }

    // ---- loading and playing ----

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
            const url = `audio/${encodeURIComponent(this.trackKey)}/${encodeURIComponent(id)}`;
            const measured = fetch(`${url}/loudness`)
                .then((reply) => (reply.ok ? reply.json() : null))
                .then((reading) => (typeof reading?.lufs === "number" ? Math.min(0, MUSIC_TARGET_LUFS - reading.lufs) : MUSIC_FALLBACK_DB))
                .catch(() => MUSIC_FALLBACK_DB);
            const response = await this.fetchAudio(url);
            const decoded = await this.ctx
                .decodeAudioData(await response.arrayBuffer())
                .catch(() => Promise.reject(new Error("the file could not be read as audio.")));

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

    // A seek to a spot outside the window brings the window, at its width, to start on that bar.
    followIntoView(at) {
        const { start, end } = this.window;

        if (!this.view || (at >= start && at < end) || this.isWhole())
            return;

        this.leftBehind = false;

        const snaps = this.snaps();
        const width = this.indexOf(end) - this.indexOf(start);
        const first = clamp(this.floorIndex(at), 0, snaps.length - 1 - width);

        this.setWindow(snaps[first], snaps[first + width], { final: true });
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

    // ---- the click ----

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

    // ---- loop, window ----

    isWhole() {
        return this.window.start <= SNAP && this.window.end >= this.duration - 0.001;
    }

    setLoop(on) {
        if (on && this.isWhole())
            on = false;

        this.loopOn = on;

        if (on) {
            const at = this.playing ? this.position() : this.parked;

            // Switched on with the playhead outside the window: playback moves to its start at once.
            if (at < this.window.start || at >= this.window.end) {
                this.parked = this.window.start;
                this.leftBehind = false;
            }
        }

        this.restart();
        this.syncPlayer();
        this.paintWindow();
    }

    // Everything that moves the window goes through here; the warp view is told, coalesced.
    setWindow(start, end, { final = false, silent = false } = {}) {
        const moved = start !== this.window.start || end !== this.window.end;

        this.window = { start, end };

        if (final && this.loopOn) {
            if (this.isWhole()) {
                this.loopOn = false;
                this.syncPlayer();
            }

            const at = this.playing ? this.position() : this.parked;

            if (this.loopOn && (at < start || at >= end))
                this.parked = start;

            this.restart();
        }

        if (!this.loopOn || final)
            this.rememberIfZoomed();

        this.paintWindow();
        this.paintPlayhead();

        if (this.drag?.moved)
            this.paintLive();

        // A handle or body drag moves only the browser's own parts; the rows are told when it ends.
        if (moved && !silent && !this.drag?.moved)
            this.pushWindow();
    }

    rememberIfZoomed() {
        if (!this.isWhole())
            this.rememberedZoom = { ...this.window };
    }

    pushWindow() {
        const view = this.view;

        if (!view)
            return;

        if (view.sent?.[0] === this.window.start && view.sent?.[1] === this.window.end && !view.pending)
            return;

        view.pending = [this.window.start, this.window.end];

        if (view.inFlight)
            return;

        const send = async () => {
            while (view.pending) {
                const [start, end] = view.pending;

                view.pending = null;
                view.sent = [start, end];
                view.inFlight = true;

                try {
                    await view.dotnet.invokeMethodAsync("SetWindow", start, end);
                } catch {
                    // The circuit is gone; nothing draws any more.
                }
            }

            view.inFlight = false;
        };

        send();
    }

    snaps() {
        if (this.snapCache?.duration === this.duration && this.snapCache.grid === this.grid)
            return this.snapCache.points;

        const points = [0];
        const { duration, grid } = this;

        if (grid) {
            for (let index = grid.indexAt(0); ; index++) {
                const at = grid.startOf(index);

                if (at >= duration - 0.001)
                    break;

                if (at > 0.001)
                    points.push(at);
            }
        } else {
            for (let second = 1; second < duration - 0.001; second++)
                points.push(second);
        }

        points.push(duration);
        this.snapCache = { duration, grid, points };

        return points;
    }

    indexOf(at) {
        const points = this.snaps();
        let best = 0;

        for (let i = 1; i < points.length; i++) {
            if (Math.abs(points[i] - at) < Math.abs(points[best] - at))
                best = i;
        }

        return best;
    }

    floorIndex(at) {
        const points = this.snaps();
        let index = 0;

        while (index < points.length - 1 && points[index + 1] <= at + 1e-9)
            index++;

        return index;
    }

    // The least width a window may have, in snap indexes.
    minWidth(startIndex) {
        const points = this.snaps();

        if (this.grid)
            return Math.min(1, points.length - 1);

        let width = 1;

        while (startIndex + width < points.length - 1 && points[startIndex + width] - points[startIndex] < MIN_WINDOW_SECONDS)
            width++;

        return width;
    }

    barsLabel(start, end) {
        if (!this.grid)
            return `${clock(start)}–${clock(end)}`;

        const first = this.grid.barAt(start + 0.001);
        const last = this.grid.barAt(end - 0.001);

        return first === last ? `${first}` : `${first}–${last}`;
    }

    // ---- painting ----

    startFrames() {
        if (this.frame)
            return;

        const tick = () => {
            this.frame = requestAnimationFrame(tick);
            this.onFrame();
        };

        this.frame = requestAnimationFrame(tick);
    }

    onFrame() {
        if (!this.player && !this.view) {
            cancelAnimationFrame(this.frame);
            this.frame = 0;

            return;
        }

        const at = this.scrubbing ? this.scrubbing.at : this.playing ? this.position() : this.parked;

        this.paintPosition(at);
        this.paintPlayhead(at);
        this.follow(at);
    }

    paintPosition(at) {
        const { pos } = this.els;

        if (!pos)
            return;

        const total = clock(this.duration);
        const text = this.grid ? `bar ${this.grid.position(at)}` : null;
        const html = `${text ? `<b>${text}</b><span class="t75"> · ` : "<b>"}${clock(at, true)}${text ? "</span>" : "</b>"}<span class="t40"> / ${total}</span>`;

        if (html !== this.lastPosition) {
            pos.innerHTML = html;
            this.lastPosition = html;
        }

        const { wave, rest, mark } = this.els;

        if (wave && this.duration > 0) {
            const fraction = clamp(at / this.duration, 0, 1);

            mark.style.left = `${fraction * 100}%`;
            rest.style.left = `${fraction * 100}%`;
        }

        this.paintPlayWindow(at);
    }

    paintPlayhead(given) {
        const at = given ?? (this.playing ? this.position() : this.parked);
        const { head, overviewHead, goto } = this.els;
        const { start, end } = this.window;

        if (overviewHead && this.duration > 0)
            overviewHead.style.left = `${clamp(at / this.duration, 0, 1) * 100}%`;

        const inside = at >= start - 1e-3 && at <= end + 1e-3;

        if (head) {
            head.hidden = !inside;
            head.style.left = `${((at - start) / (end - start)) * 100}%`;
        }

        if (goto) {
            const outside = !inside && !this.isWhole();

            goto.hidden = !outside;

            if (outside) {
                const label = this.grid ? `Go to the playhead · bar ${this.grid.barAt(at)}` : `Go to the playhead · ${clock(at)}`;

                if (goto.textContent !== label)
                    goto.textContent = label;
            }
        }

        if (inside)
            this.leftBehind = false;
    }

    paintPlayWindow(at) {
        for (const button of this.view?.root.querySelectorAll("[data-play-window]") ?? []) {
            const [start, end] = [Number(button.dataset.start), Number(button.dataset.end)];
            const playingIt = this.playing && at >= start - 1e-3 && at < end;
            const label = button.querySelector(".pw-label");

            if (label && label.textContent !== (playingIt ? "Pause" : "Play window"))
                label.textContent = playingIt ? "Pause" : "Play window";

            button.dataset.on = playingIt ? "1" : "0";
        }
    }

    follow(at) {
        const { start, end } = this.window;

        if (!this.playing || this.loopOn || this.drag || this.leftBehind || this.isWhole() || at < end)
            return;

        // The playhead reached the right edge while following: the window moves on by half its width.
        const snaps = this.snaps();
        const first = this.indexOf(start);
        const last = this.indexOf(end);
        const half = Math.max(1, Math.ceil((last - first) / 2));
        const next = Math.min(first + half, snaps.length - 1 - (last - first));

        if (next === first)
            return;

        this.setWindow(snaps[next], snaps[next + (last - first)]);
    }

    findViewElements() {
        const root = this.view?.root;

        Object.assign(this.els, {
            overview: root?.querySelector("[data-overview]"),
            field: root?.querySelector("[data-field]"),
            head: root?.querySelector("[data-playhead]"),
            seekLine: root?.querySelector("[data-seekline]"),
            overviewHead: root?.querySelector("[data-overview-head]"),
            readout: root?.querySelector("[data-readout]"),
            goto: root?.querySelector("[data-goto]"),
            earlier: root?.querySelector("[data-earlier]"),
            later: root?.querySelector("[data-later]"),
            hint: root?.querySelector("[data-view-hint]"),
        });
    }

    layoutField() {
        const { field, head } = this.els;
        const root = this.view?.root;
        const grid = root?.querySelector(".grid");
        const ruler = grid?.querySelector(".ruler");

        if (!field || !grid || !ruler)
            return;

        const bottoms = [...grid.querySelectorAll(".lane > .plot, .key-strip, .tempo-line, .off-line")];
        const last = bottoms.reduce((best, element) => Math.max(best, element.offsetTop + element.offsetHeight), ruler.offsetTop + ruler.offsetHeight);

        field.style.left = `${ruler.offsetLeft}px`;
        field.style.width = `${ruler.offsetWidth}px`;
        field.style.top = `${ruler.offsetTop}px`;
        field.style.height = `${last - ruler.offsetTop}px`;
        this.fieldBox = { top: ruler.offsetTop, height: last - ruler.offsetTop };

        if (head)
            head.hidden = false;
    }

    paintWindow() {
        const overview = this.els.overview;

        if (!overview || this.duration <= 0)
            return;

        const { start, end } = this.window;
        const [from, to] = [(start / this.duration) * 100, (end / this.duration) * 100];
        const whole = this.isWhole();
        const set = (selector, style) => {
            const element = overview.querySelector(selector);

            if (element)
                Object.assign(element.style, style);
        };

        set(".win", { left: `${from}%`, width: `${to - from}%` });
        set(".dim.a", { left: "0", width: `${whole ? 0 : from}%` });
        set(".dim.b", { left: `${to}%`, right: "0", display: whole ? "none" : "block" });
        overview.querySelector(".win")?.classList.toggle("loop", this.loopOn);

        for (const [selector, label, now] of [
            [".hd.l", "Window start", start],
            [".hd.r", "Window end", end],
        ]) {
            const handle = overview.querySelector(selector);

            handle?.setAttribute("aria-valuenow", String(Math.round(now * 100) / 100));
            handle?.setAttribute("aria-valuetext", this.grid ? `bar ${this.grid.barAt(now + (selector === ".hd.r" ? -0.001 : 0.001))}` : clock(now));
            handle?.setAttribute("aria-label", label);
        }

        const { readout, earlier, later, hint } = this.els;

        if (readout) {
            const text = whole
                ? `whole track · ${clock(this.duration)}`
                : `${this.grid ? `bar${this.barsLabel(start, end).includes("–") ? "s" : ""} ${this.barsLabel(start, end)} · ` : ""}${clock(start)}–${clock(end)}`;

            readout.textContent = text;
            readout.classList.toggle("whole", whole);
        }

        if (earlier)
            earlier.disabled = start <= SNAP;

        if (later)
            later.disabled = end >= this.duration - 0.001;

        if (hint)
            hint.textContent = this.loopOn
                ? `looping these ${this.grid ? "bars" : "seconds"}`
                : whole
                  ? "pull a handle to zoom · double-click for 8 bars"
                  : "drag to move · double-click for the whole track";

        this.paintGhost();
    }

    paintGhost() {
        const ghost = this.els.overview?.querySelector(".ghost");

        if (!ghost)
            return;

        const old = this.drag?.loop;
        const show = Boolean(old) && this.drag?.moved;

        ghost.hidden = !show;

        if (show) {
            ghost.style.left = `${(old.start / this.duration) * 100}%`;
            ghost.style.width = `${((old.end - old.start) / this.duration) * 100}%`;
        }
    }

    paintLanes() {
        for (const speaker of this.view?.root.querySelectorAll("[data-speaker]") ?? [])
            speaker.toggleAttribute("hidden", speaker.dataset.speaker !== this.fileId);
    }

    // ---- the rows, redrawn here while the window is dragged ----
    // The warp view tells the whole track's rows once (setLive); a drag draws them from that over the
    // server's own, which are hidden until the rows come back drawn for the final window. The drawing
    // mirrors WarpView.razor (ruler, lanes, tempo and off-grid rows, axis) and KeyLane.razor.

    setLive(live) {
        for (const lane of live.lanes) {
            if (lane.peaks)
                lane.peaks.bytes = Uint8Array.from(atob(lane.peaks.data), (char) => char.charCodeAt(0));
        }

        this.live = live;
        this.stopLive();
    }

    startLive() {
        const { live } = this;
        const field = this.els.field;
        const grid = this.view?.root.querySelector(".grid");
        const ruler = grid?.querySelector(":scope > .ruler");

        if (this.liveLayers || !live || !field || !ruler || this.duration <= 0)
            return;

        const scopeOf = (element) => [...(element?.attributes ?? [])].find((attribute) => attribute.name.startsWith("b-"))?.name ?? "data-none";
        const own = scopeOf(ruler);
        const layers = [];
        const cover = (target, kind, lane) => {
            if (!target)
                return;

            const layer = target.cloneNode(false);

            layer.removeAttribute("id");
            layer.removeAttribute("data-snaps");
            layer.classList.add("live");
            Object.assign(layer.style, {
                position: "absolute",
                margin: "0",
                left: `${target.offsetLeft - ruler.offsetLeft}px`,
                top: `${target.offsetTop - ruler.offsetTop}px`,
                width: `${target.offsetWidth}px`,
                height: `${target.offsetHeight}px`,
            });

            for (const kept of target.querySelectorAll(".range, .off-label"))
                layer.append(kept.cloneNode(true));

            field.insertBefore(layer, field.firstChild);

            const entry = { kind, target, layer, lane, scope: kind === "keys" ? scopeOf(target) : own, viewScope: own };

            if (kind === "lane") {
                layer.insertAdjacentHTML(
                    "afterbegin",
                    `<svg ${scopeOf(target.querySelector("svg.overview"))} class="overview" viewBox="0 0 832 45" preserveAspectRatio="none"><path ${scopeOf(target.querySelector("svg.overview path") ?? target.querySelector("svg.overview"))} d=""/></svg><div class="dyn" style="position:absolute;inset:0"></div>`,
                );
                entry.path = layer.querySelector("path");
                entry.dyn = layer.querySelector(".dyn");
            }

            layers.push(entry);
        };

        cover(grid.querySelector(":scope > .ruler"), "ruler");
        cover(grid.querySelector(":scope > .key-strip"), "keys");
        cover(grid.querySelector(":scope > .axis"), "axis");
        cover(grid.querySelector(":scope > .tempo-line"), "tempo");
        cover(grid.querySelector(":scope > .off-line"), "off");

        for (const row of grid.querySelectorAll(":scope > .lane")) {
            const lane = live.lanes.find((candidate) => candidate.file === row.dataset.file);

            if (lane?.covered)
                cover(row.querySelector(":scope > .plot"), "lane", lane);
        }

        this.liveLayers = layers;
        this.liveShown = null;
    }

    stopLive() {
        clearTimeout(this.liveTimer);

        for (const { target, layer } of this.liveLayers ?? []) {
            layer.remove();
            target.removeAttribute("data-covered");
        }

        this.liveLayers = null;
    }

    paintLive() {
        const { live } = this;

        if (!live || this.duration <= 0)
            return;

        this.startLive();

        if (!this.liveLayers)
            return;

        const { start: from, end: to } = this.window;

        if (this.liveShown?.[0] === from && this.liveShown?.[1] === to)
            return;

        this.liveShown = [from, to];

        const view = { from, to, zoomed: to - from < live.seconds - 0.001 };

        for (const layer of this.liveLayers) {
            const bands = this.bandsHtml(view, layer, layer.kind === "ruler");

            if (layer.kind === "ruler")
                layer.layer.innerHTML = this.rulerHtml(view, layer.scope) + bands;
            else if (layer.kind === "axis")
                layer.layer.innerHTML = this.timeTicks(from, to)
                    .map((at) => `<span ${layer.scope} class="mono" style="left:${pct(xOf(view, at))}%">${clock(Math.round(at))}</span>`)
                    .join("");
            else if (layer.kind === "keys")
                layer.layer.innerHTML = this.keysHtml(view, layer) + bands;
            else if (layer.kind === "tempo")
                this.paintTempo(layer, view, bands);
            else if (layer.kind === "off")
                this.paintOff(layer, view, bands);
            else
                this.paintLane(layer, view, bands);

            layer.target.setAttribute("data-covered", "");
        }
    }

    bandsHtml(view, { scope, viewScope }, tags) {
        const attribute = viewScope ?? scope;

        return this.live.bands
            .map((band) => {
                const [left, right] = [clampX(view, band.start), clampX(view, band.end)];

                if (right <= left)
                    return "";

                const tag = tags ? `<span ${attribute} class="mono jtag">${escapeHtml(band.tag)}</span>` : "";

                return `<div ${attribute} class="jband ${band.style}${band.open ? " open" : ""}" style="left:${pct(left)}%;width:${pct(right - left)}%">${tag}</div>`;
            })
            .join("");
    }

    timeTicks(from, to) {
        const every = to - from > 120 ? 30 : to - from > 40 ? 10 : 2;
        const ticks = [];

        for (let at = Math.ceil(from / every) * every; at <= to; at += every)
            ticks.push(at);

        return ticks;
    }

    // WarpView.razor's ruler: time ticks without a warp, else the grid's bars, the loop and the clip start.
    rulerHtml(view, scope) {
        const { live, grid } = { live: this.live, grid: this.grid };
        const { from, to } = view;
        const html = [];
        const clipX = live.clip && xOf(view, live.clip.start) >= 0 && xOf(view, live.clip.start) <= 100 ? xOf(view, live.clip.start) : null;
        const toLeft = clipX !== null && clipX > 100 - px(72);
        const underClip = (at) => clipX !== null && (toLeft ? at > clipX - px(90) && at < clipX - px(7) : at > clipX - px(19) && at < clipX + px(66));

        if (live.timeRuler) {
            for (const at of this.timeTicks(from, to)) {
                html.push(`<span ${scope} class="tick numbered" style="left:${pct(xOf(view, at))}%"></span>`);
                html.push(`<span ${scope} class="mono bar-number" style="left:${pct(xOf(view, at))}%">${clock(Math.round(at))}</span>`);
            }
        }

        if (live.loop) {
            const { start, end, on, title } = live.loop;
            const [left, right] = [clampX(view, start), clampX(view, end)];

            if (right > left) {
                html.push(
                    `<div ${scope} class="loop${on ? " on" : ""}${xOf(view, start) < 0 ? " open-left" : ""}${xOf(view, end) > 100 ? " open-right" : ""}" style="left:${pct(left)}%;width:${pct(right - left)}%" title="${escapeHtml(title)}"></div>`,
                );
            }

            if (xOf(view, end) >= 0 && xOf(view, end) <= 92)
                html.push(`<span ${scope} class="mono loop-word" style="left:calc(${pct(xOf(view, end))}% + 4px)">${on ? "loop" : "loop off"}</span>`);
        }

        if (grid && !live.timeRuler) {
            const first = grid.beatAt(from);
            const last = grid.beatAt(to);
            const bars = (last - first) / grid.perBar;
            const [tickEvery, numberEvery] = bars > 48 ? [4, 16] : bars > 14 ? [1, 4] : [1, 1];

            for (let index = grid.indexAt(from); ; index++) {
                const beat = index * grid.perBar;

                if (beat > last)
                    break;

                const at = grid.secondsAt(beat);

                if (index % tickEvery === 0) {
                    const numbered = index % numberEvery === 0;

                    html.push(`<span ${scope} class="tick${numbered ? " numbered" : ""}" style="left:${pct(xOf(view, at))}%"></span>`);

                    if (numbered && !underClip(xOf(view, at)))
                        html.push(`<span ${scope} class="mono bar-number" style="left:${pct(xOf(view, at))}%">${grid.number(index)}</span>`);
                }

                if (bars <= 14) {
                    for (let i = 1; i < grid.numerator; i++)
                        html.push(`<span ${scope} class="tick beat" style="left:${pct(xOf(view, grid.secondsAt(beat + i * grid.unit)))}%"></span>`);
                }
            }
        }

        if (clipX !== null) {
            html.push(`<span ${scope} class="notch" style="left:${pct(clipX)}%" title="${escapeHtml(live.clip.title)}"></span>`);
            html.push(`<span ${scope} class="mono clip-word${toLeft ? " left" : ""}" style="left:${pct(clipX)}%">clip start</span>`);
        }

        return html.join("");
    }

    keysHtml(view, { scope }) {
        return (this.live.keys ?? [])
            .map((band) => {
                const [left, right] = [clampX(view, band.start), clampX(view, band.end)];

                if (right <= left)
                    return "";

                const classes = `key-band${band.fromWindow ? " from-window" : ""}${band.unbacked ? " unbacked" : ""}${band.pickable ? " pickable" : ""}${band.picked ? " picked" : ""}`;
                const label = ((right - left) / 100) * PLOT_PIXELS >= 56 ? `<span ${scope} class="mono key-label">${escapeHtml(band.label)}</span>` : "";

                return `<button ${scope} type="button" tabindex="-1" class="${classes}" style="left:${pct(left)}%;width:${pct(right - left)}%">${label}</button>`;
            })
            .join("");
    }

    // The tempo from the markers: a step for each span between two, the first from the view's start and the last to its end.
    paintTempo(layer, view, bands) {
        const { tempo } = this.live;
        const { scope } = layer;
        const path = [];

        for (const [start, end, y] of tempo?.segments ?? []) {
            const [left, right] = [clampX(view, start ?? view.from), clampX(view, end ?? view.to)];

            if (right > left)
                path.push(`${path.length ? "L" : "M"}${pct((left * COLUMNS) / 100)},${pct(y)}L${pct((right * COLUMNS) / 100)},${pct(y)}`);
        }

        layer.layer.querySelector("svg")?.remove();
        layer.layer.insertAdjacentHTML(
            "afterbegin",
            `<svg ${scope} viewBox="0 0 ${COLUMNS} 40" preserveAspectRatio="none"><line ${scope} class="dashed" x1="0" x2="${COLUMNS}" y1="${pct(tempo?.line ?? 0)}" y2="${pct(tempo?.line ?? 0)}"/><path ${scope} d="${path.join("")}"/></svg>`,
        );
        this.replaceBands(layer, bands);
    }

    paintOff(layer, view, bands) {
        const { scope } = layer;
        const points = (this.live.off ?? []).map(([at, y]) => [xOf(view, at), y]).filter(([at]) => at >= 0 && at <= 100);
        const line = points.length > 1 ? `<polyline ${scope} points="${points.map(([at, y]) => `${pct((at / 100) * COLUMNS)},${pct((y / 100) * 72)}`).join(" ")}"/>` : "";

        layer.layer.querySelector("svg")?.remove();
        layer.layer.querySelectorAll(".off-dot").forEach((dot) => dot.remove());
        layer.layer.insertAdjacentHTML(
            "afterbegin",
            `<svg ${scope} viewBox="0 0 ${COLUMNS} 72" preserveAspectRatio="none"><line ${scope} class="dashed" x1="0" x2="${COLUMNS}" y1="36" y2="36"/>${line}</svg>${points.map(([at, y]) => `<span ${scope} class="off-dot" style="left:${pct(at)}%;top:${pct(y)}%"></span>`).join("")}`,
        );
        this.replaceBands(layer, bands);
    }

    replaceBands(layer, bands) {
        layer.layer.querySelectorAll(":scope > .jband").forEach((band) => band.remove());
        layer.layer.insertAdjacentHTML("beforeend", bands);
    }

    // A lane: each channel's envelope, then its markers, transients, clip start and the bands.
    paintLane({ lane, path, dyn, scope }, view, bands) {
        const { from, to } = view;
        const html = [];

        path.setAttribute("d", this.wavePath(lane.peaks, from, to));

        const visible = lane.markers.map((at) => xOf(view, at)).filter((at) => at >= 0 && at <= 100);
        const flags = visible.every((at, i) => i === 0 || ((at - visible[i - 1]) / 100) * PLOT_PIXELS >= FLAG_ROOM);

        for (const at of lane.markers) {
            const at100 = xOf(view, at);

            if (at100 < 0 || at100 > 100)
                continue;

            html.push(`<span ${scope} class="marker-line${flags ? "" : " dense"}" style="left:${pct(at100)}%"></span>`);
            html.push(`<span ${scope} class="${flags ? "flag" : "flag-tick"}" style="left:${pct(at100)}%"></span>`);
        }

        // Hundreds of them: one path rather than an element each.
        if (view.zoomed) {
            const ticks = [];

            for (const at of lane.transients) {
                const at100 = xOf(view, at);

                if (at100 >= 0 && at100 <= 100)
                    ticks.push(`M${pct((at100 * COLUMNS) / 100)},58v5`);
            }

            html.push(
                `<svg ${scope} viewBox="0 0 ${COLUMNS} 64" preserveAspectRatio="none" style="position:absolute;inset:0;width:100%;height:100%"><path ${scope} d="${ticks.join("")}" fill="none" stroke="var(--text-30)" style="vector-effect:non-scaling-stroke"/></svg>`,
            );
        }

        const clip = xOf(view, lane.clip);

        if (clip >= 0 && clip <= 100)
            html.push(`<div ${scope} class="clip-line" style="left:${pct(clip)}%"></div>`);

        if (lane.parts !== null && lane.parts !== undefined)
            html.push(`<div ${scope} class="differs" style="left:${pct(Math.max(0, xOf(view, lane.parts)))}%"></div>`);

        dyn.innerHTML = html.join("") + bands;
    }

    wavePath({ columns, channels, bytes }, from, to) {
        const perColumn = this.live.seconds / columns;
        const gap = channels > 1 ? 2 : 0;
        const each = (45 - gap * (channels - 1)) / channels;
        const path = [];

        for (let channel = 0; channel < channels; channel++) {
            const center = channel * (each + gap) + each / 2;
            const base = channel * columns * 2;
            const upper = new Array(COLUMNS);
            const lower = new Array(COLUMNS);

            for (let x = 0; x < COLUMNS; x++) {
                const first = Math.floor((from + ((to - from) * x) / COLUMNS) / perColumn);
                const last = Math.max(first, Math.floor((from + ((to - from) * (x + 1)) / COLUMNS) / perColumn) - 1);
                let [low, high] = [0, 0];

                for (let column = Math.max(first, 0); column <= Math.min(last, columns - 1); column++) {
                    const at = base + column * 2;

                    if (bytes[at] > low)
                        low = bytes[at];

                    if (bytes[at + 1] > high)
                        high = bytes[at + 1];
                }

                const max = high / 255;
                const min = Math.min(-low / 255, max - 0.02);

                upper[x] = `${x + 0.5},${(center - (max * each) / 2).toFixed(2)}`;
                lower[COLUMNS - 1 - x] = `${x + 0.5},${(center - (min * each) / 2).toFixed(2)}`;
            }

            path.push(`M${upper.join(" ")} ${lower.join(" ")}Z`);
        }

        return path.join("");
    }

    // ---- the player strip ----

    syncPlayer() {
        const root = this.player?.root;

        if (!root)
            return;

        Object.assign(this.els, {
            pos: root.querySelector("[data-pos]"),
            wave: root.querySelector("[data-wave]"),
            rest: root.querySelector("[data-rest]"),
            mark: root.querySelector("[data-mark]"),
            canvas: root.querySelector("[data-wave-canvas]"),
            menu: root.querySelector("[data-click-menu]"),
        });
        this.lastPosition = null;

        const set = (selector, apply) => {
            const element = root.querySelector(selector);

            if (element)
                apply(element);
        };
        const hasFiles = this.files.length > 0;
        const click = Boolean(this.grid);

        set("[data-click]", (button) => {
            button.disabled = !hasFiles || !click;
            button.setAttribute("aria-pressed", String(this.clickOn && click));
            button.classList.toggle("on", this.clickOn && click);
        });
        set("[data-click-extras]", (element) => (element.hidden = !click || !hasFiles));
        set("[data-no-click]", (element) => (element.hidden = click || !hasFiles));
        set("[data-click-sound] .snd-name", (element) => (element.textContent = this.settings.sound[0].toUpperCase() + this.settings.sound.slice(1)));
        set("[data-click-level]", (input) => (input.value = String(this.settings.clickLevel)));
        set("[data-volume]", (input) => (input.value = String(this.settings.volume)));
        set("[data-loop]", (button) => {
            const whole = this.isWhole();

            button.disabled = !hasFiles || whole;
            button.setAttribute("aria-pressed", String(this.loopOn));
            button.classList.toggle("on", this.loopOn);
        });
        set("[data-loop-note]", (element) => (element.hidden = !hasFiles || !this.isWhole()));
        set("[data-click-menu]", (menu) => {
            for (const item of menu.querySelectorAll("[data-sound]"))
                item.classList.toggle("on", item.dataset.sound === this.settings.sound);
        });

        this.copyShape();
        this.drawWave();
        this.paintPosition(this.playing ? this.position() : this.parked);
    }

    // The slim waveform: the whole file that is playing, once it has been decoded.
    drawWave() {
        const { canvas, wave } = this.els;

        if (!canvas || !wave || !this.buffer || this.bufferId !== this.fileId)
            return;

        const width = Math.max(1, Math.floor(wave.clientWidth));
        const height = Math.max(1, Math.floor(wave.clientHeight));

        canvas.width = width;
        canvas.height = height;

        const context = canvas.getContext("2d");
        const channels = Array.from({ length: this.buffer.numberOfChannels }, (_, i) => this.buffer.getChannelData(i));
        const per = this.buffer.length / width;

        context.clearRect(0, 0, width, height);
        context.fillStyle = "rgba(236, 231, 220, 0.5)";

        for (let x = 0; x < width; x++) {
            let peak = 0;

            for (const data of channels) {
                const stride = Math.max(1, Math.floor(per / 64));

                for (let i = Math.floor(x * per); i < Math.min(data.length, Math.floor((x + 1) * per)); i += stride)
                    peak = Math.max(peak, Math.abs(data[i]));
            }

            const half = Math.max(0.5, Math.min(1, peak) * (height / 2 - 2));

            context.fillRect(x, height / 2 - half, 1, half * 2);
        }

        canvas.hidden = false;
        this.player.root.querySelector("[data-shape]")?.setAttribute("hidden", "");
    }

    onPlayerClick(event) {
        const target = event.target;

        if (target.closest("[data-play]"))
            return this.toggle();

        if (target.closest("[data-click]"))
            return this.setClick(!this.clickOn);

        if (target.closest("[data-loop]"))
            return this.setLoop(!this.loopOn);

        if (target.closest("[data-click-sound]")) {
            this.els.menu.hidden = !this.els.menu.hidden;

            return;
        }

        if (target.closest("[data-sound]")) {
            this.settings.sound = target.closest("[data-sound]").dataset.sound;
            writeSettings(this.settings);
            this.els.menu.hidden = true;
            this.syncPlayer();

            if (this.playing)
                this.swapClickVoice();
            else
                this.preview();

            return;
        }

        if (target.closest("[data-retry]"))
            this.retry();
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

    onPlayerInput(event) {
        const input = event.target;

        if (input.matches("[data-volume]")) {
            this.settings.volume = Number(input.value);
            this.master && (this.master.gain.value = this.settings.volume);
        } else if (input.matches("[data-click-level]")) {
            this.settings.clickLevel = Number(input.value);
        } else {
            return;
        }

        writeSettings(this.settings);
    }

    onKey(event) {
        if (event.key === "Escape" && this.drag) {
            this.cancelDrag();

            return;
        }

        if (event.key !== " " || event.ctrlKey || event.altKey || event.metaKey || !this.files.length)
            return;

        const target = event.target;

        if (target.closest?.("input:not([type=range]), textarea, select, [contenteditable], [role=listbox], [role=slider], [data-click-menu]"))
            return;

        event.preventDefault();
        this.toggle();
    }

    // The slim waveform: a click seeks, a press held and dragged scrubs without a sound until release.
    onWaveDown(event) {
        const wave = event.target.closest("[data-wave]");

        if (!wave || event.button !== 0 || this.duration <= 0)
            return;

        event.preventDefault();
        wave.setPointerCapture(event.pointerId);

        const at = (e) => clamp((e.clientX - wave.getBoundingClientRect().left) / wave.clientWidth, 0, 1) * this.duration;
        const move = (e) => (this.scrubbing = { at: at(e) });
        const up = (e) => {
            wave.removeEventListener("pointermove", move);
            wave.removeEventListener("pointerup", up);
            wave.removeEventListener("pointercancel", up);
            this.scrubbing = null;

            if (e.type === "pointerup")
                this.seek(at(e));
        };

        this.scrubbing = { at: at(event) };
        wave.addEventListener("pointermove", move);
        wave.addEventListener("pointerup", up);
        wave.addEventListener("pointercancel", up);
    }

    // ---- the warp view: seeking ----

    plotTime(event, element) {
        const box = element.getBoundingClientRect();
        const { start, end } = this.window;

        return start + clamp((event.clientX - box.left) / box.width, 0, 1) * (end - start);
    }

    onViewMove(event) {
        if (this.drag)
            return this.onOverviewMove(event);

        const target = event.target;

        if (target.closest("[data-overview]"))
            return this.hoverOverview(event);

        if (target.closest(".ruler.dragging, .jtag, .edge"))
            return this.hideSeekLine();

        const ruler = target.closest(".ruler");
        const lane = target.closest(".lane > .plot");

        if (ruler) {
            const at = this.plotTime(event, ruler);
            const bar = this.grid ? this.snaps()[this.floorIndex(at)] : at;

            this.showSeekLine(bar, 0, this.fieldBox?.height ?? 0);
        } else if (lane) {
            const box = lane.getBoundingClientRect();
            const field = this.els.field.getBoundingClientRect();

            this.showSeekLine(this.plotTime(event, lane), box.top - field.top, box.height);
        } else {
            this.hideSeekLine();
        }
    }

    showSeekLine(at, top, height) {
        const line = this.els.seekLine;

        if (!line)
            return;

        const { start, end } = this.window;

        line.hidden = false;
        line.style.left = `${((at - start) / (end - start)) * 100}%`;
        line.style.top = `${top}px`;
        line.style.height = `${height}px`;
    }

    hideSeekLine() {
        if (this.els.seekLine)
            this.els.seekLine.hidden = true;

        this.clearHover();
    }

    onViewClick(event) {
        const target = event.target;

        if (target.closest("[data-earlier]"))
            return this.pan(-1);

        if (target.closest("[data-later]"))
            return this.pan(1);

        if (target.closest("[data-goto]")) {
            this.leftBehind = false;
            this.followIntoViewForce(this.playing ? this.position() : this.parked);

            return;
        }

        const playWindow = target.closest("[data-play-window]");

        if (playWindow)
            return this.playWindow(playWindow);

        if (target.closest("[data-overview]"))
            return;

        const band = target.closest(".key-band[data-start]");

        if (band) {
            this.seek(Number(band.dataset.start));

            return;
        }

        if (target.closest(".jtag, .edge, button, a, input, select"))
            return;

        const ruler = target.closest(".ruler:not(.dragging)");

        if (ruler) {
            const at = this.plotTime(event, ruler);

            this.seek(this.grid ? this.snaps()[this.floorIndex(at)] : at);

            return;
        }

        const lane = target.closest(".lane > .plot");

        if (lane) {
            const file = lane.closest(".lane").dataset.file;
            const at = this.plotTime(event, lane);

            if (file && file !== this.fileId && this.files.some((playable) => playable.id === file))
                this.selectFile(file, { at, play: true }).then(() => this.paintLanes());
            else
                this.seek(at, { play: true });

            this.paintLanes();
        }
    }

    followIntoViewForce(at) {
        const snaps = this.snaps();
        const width = this.indexOf(this.window.end) - this.indexOf(this.window.start);
        const first = clamp(this.floorIndex(at), 0, snaps.length - 1 - width);

        this.setWindow(snaps[first], snaps[first + width], { final: true });
    }

    playWindow(button) {
        const [start, end] = [Number(button.dataset.start), Number(button.dataset.end)];

        if (button.dataset.on === "1")
            return this.pause();

        if (this.loopOn || this.window.start > start || this.window.end <= start) {
            // The window to loop is the bars the reading covers, outward to whole bars.
            const first = this.floorIndex(start);
            const last = Math.max(first + 1, this.snaps().findIndex((point) => point >= end - 1e-6));
            const snaps = this.snaps();

            if (this.loopOn)
                this.setWindow(snaps[first], snaps[last < 0 ? snaps.length - 1 : last], { final: true });
            else
                this.followIntoView(start);
        }

        this.parked = start;

        if (this.playing)
            this.begin(start);
        else
            this.play();
    }

    // ‹ ›: half the window's width, whole bars, at least one.
    pan(direction) {
        const snaps = this.snaps();
        const first = this.indexOf(this.window.start);
        const last = this.indexOf(this.window.end);
        const half = Math.max(1, Math.ceil((last - first) / 2));
        const next = clamp(first + direction * half, 0, snaps.length - 1 - (last - first));

        this.leftBehind = true;
        this.setWindow(snaps[next], snaps[next + (last - first)], { final: true });
    }

    // ---- the warp view: the window ----

    regionAt(event) {
        const strip = this.els.overview;
        const box = strip.getBoundingClientRect();
        const x = event.clientX - box.left;
        const [xs, xe] = [(this.window.start / this.duration) * box.width, (this.window.end / this.duration) * box.width];
        const inner = Math.min(8, Math.max(0, (xe - xs) / 4));
        const body = xe - xs;

        const nearStart = x >= xs - 8 && x <= xs + (body < 32 ? inner : 8);
        const nearEnd = x <= xe + 8 && x >= xe - (body < 32 ? inner : 8);

        if (nearStart && nearEnd)
            return Math.abs(x - xs) <= Math.abs(x - xe) ? "start" : "end";

        if (nearStart)
            return "start";

        if (nearEnd)
            return "end";

        return x > xs && x < xe ? "body" : "outside";
    }

    hoverOverview(event) {
        if (this.duration <= 0 || !this.els.overview)
            return;

        const region = this.regionAt(event);
        const overview = this.els.overview;

        overview.dataset.hot = region;
        overview.querySelector(".win")?.classList.toggle("hov", region === "body");
        overview.querySelector(".hd.l")?.classList.toggle("hov", region === "start");
        overview.querySelector(".hd.r")?.classList.toggle("hov", region === "end");
    }

    clearHover() {
        const overview = this.els.overview;

        if (!overview || this.drag)
            return;

        delete overview.dataset.hot;
        overview.querySelectorAll(".hov").forEach((element) => element.classList.remove("hov"));
    }

    onOverviewDown(event) {
        const overview = event.target.closest("[data-overview]");

        if (!overview || event.button !== 0 || this.duration <= 0 || this.drag)
            return;

        const region = this.regionAt(event);
        const box = overview.getBoundingClientRect();
        const at = ((event.clientX - box.left) / box.width) * this.duration;

        if (region === "outside" && this.isWhole())
            return;

        if (region !== "outside" && event.target.closest("[role=slider], [role=button]"))
            event.target.focus?.({ preventScroll: true });

        overview.setPointerCapture(event.pointerId);
        event.preventDefault();

        this.drag = {
            overview,
            region,
            pointerId: event.pointerId,
            x0: event.clientX,
            moved: false,
            before: { ...this.window },
            loop: this.loopOn ? { ...this.window } : null,
            grab: region === "start" ? at - this.window.start : region === "end" ? at - this.window.end : at - this.window.start,
            width: this.indexOf(this.window.end) - this.indexOf(this.window.start),
        };

        document.body.classList.add("pb-locked", `pb-locked-${region === "outside" ? "body" : region}`);

        const slider = region === "start" ? ".hd.l" : region === "end" ? ".hd.r" : ".win";

        overview.querySelector(slider)?.classList.add("drag");

        // A press outside the window brings it to the pointer at once, and the same press carries on as a drag.
        if (region === "outside" && !this.isWhole()) {
            this.drag.region = "body";
            this.drag.grab = ((this.window.end - this.window.start) / 2);
            this.drag.moved = true;
            this.dragTo(event);
        }

        const listeners = {
            pointermove: (e) => this.onOverviewMove(e),
            pointerup: (e) => this.finishDrag(e),
            pointercancel: () => this.cancelDrag(),
            lostpointercapture: (e) => this.drag && this.finishDrag(e),
        };

        this.drag.listeners = listeners;

        // The layers are made now, empty, so the first step of the drag only has to fill them.
        this.startLive();

        for (const [name, handler] of Object.entries(listeners))
            overview.addEventListener(name, handler);
    }

    onOverviewMove(event) {
        if (!this.drag)
            return;

        // The button came up outside the window and no pointerup arrived.
        if (event.buttons === 0 && event.pointerType === "mouse")
            return this.finishDrag(event);

        if (!this.drag.moved && Math.abs(event.clientX - this.drag.x0) < DRAG_THRESHOLD)
            return;

        this.drag.moved = true;
        this.dragTo(event);
    }

    dragTo(event) {
        const { overview, region, grab, width } = this.drag;
        const box = overview.getBoundingClientRect();
        const at = clamp(((event.clientX - box.left) / box.width) * this.duration, 0, this.duration);
        const snaps = this.snaps();
        let [start, end] = [this.indexOf(this.window.start), this.indexOf(this.window.end)];
        let tag;

        if (region === "body") {
            const wanted = this.indexOf(clamp(at - grab, 0, this.duration));

            start = clamp(wanted, 0, snaps.length - 1 - width);
            end = start + width;
            tag = { at: (snaps[start] + snaps[end]) / 2, text: this.barsLabel(snaps[start], snaps[end]) };
            this.drag.stopped = false;
        } else if (region === "start") {
            const wanted = this.indexOf(clamp(at - grab, 0, this.duration));

            end = this.indexOf(this.window.end);
            start = Math.min(wanted, end - this.minWidthBefore(end));
            this.drag.stopped = wanted > start;
            tag = { at: snaps[start], text: this.grid ? `from bar ${this.grid.barAt(snaps[start] + 0.001)}` : clock(snaps[start]) };
        } else {
            const wanted = this.indexOf(clamp(at - grab, 0, this.duration));

            start = this.indexOf(this.window.start);
            end = Math.max(wanted, start + this.minWidth(start));
            this.drag.stopped = wanted < end;
            tag = { at: snaps[end], text: this.grid ? `to bar ${this.grid.barAt(snaps[end] - 0.001)}` : clock(snaps[end]) };
        }

        this.drag.overview.querySelector(region === "start" ? ".hd.l" : ".hd.r")?.classList.toggle("stop", Boolean(this.drag.stopped));
        this.setWindow(snaps[start], snaps[end]);
        this.showTag(tag);
    }

    minWidthBefore(endIndex) {
        const points = this.snaps();

        if (this.grid)
            return 1;

        let width = 1;

        while (endIndex - width > 0 && points[endIndex] - points[endIndex - width] < MIN_WINDOW_SECONDS)
            width++;

        return width;
    }

    showTag(tag) {
        const element = this.drag?.overview.querySelector(".tip");

        if (!element)
            return;

        element.hidden = !tag;

        if (tag) {
            element.textContent = tag.text;
            element.style.left = `${(tag.at / this.duration) * 100}%`;
        }
    }

    endDrag() {
        const drag = this.drag;

        if (!drag)
            return null;

        for (const [name, handler] of Object.entries(drag.listeners ?? {}))
            drag.overview.removeEventListener(name, handler);

        try {
            drag.overview.releasePointerCapture(drag.pointerId);
        } catch {
            // Already released.
        }

        document.body.classList.remove("pb-locked", "pb-locked-start", "pb-locked-end", "pb-locked-body");
        drag.overview.querySelectorAll(".drag, .stop").forEach((element) => element.classList.remove("drag", "stop"));

        const tip = drag.overview.querySelector(".tip");

        if (tip)
            tip.hidden = true;

        this.drag = null;
        this.paintGhost();

        return drag;
    }

    finishDrag() {
        const drag = this.endDrag();

        if (!drag)
            return;

        // A press released inside the threshold is a click: no change, no report.
        if (!drag.moved) {
            this.stopLive();

            return;
        }

        // From press to release there was no follow; a window that left the playhead behind stays put.
        const at = this.playing ? this.position() : this.parked;

        this.leftBehind = at < this.window.start || at > this.window.end;
        this.setWindow(this.window.start, this.window.end, { final: true });
        this.pushWindow();
        this.settleLive();
    }

    cancelDrag() {
        const drag = this.endDrag();

        if (drag?.moved) {
            this.setWindow(drag.before.start, drag.before.end, { final: true });
            this.settleLive();
        } else {
            this.stopLive();
        }
    }

    // The drawn rows stay until the server's come back for the final window, or at once when none are coming.
    settleLive() {
        const view = this.view;

        clearTimeout(this.liveTimer);

        if (!this.liveLayers)
            return;

        if (view?.pending || view?.inFlight)
            this.liveTimer = setTimeout(() => this.stopLive(), 2000);
        else
            this.stopLive();
    }

    onOverviewDouble(event) {
        const overview = event.target.closest("[data-overview]");

        if (!overview || this.duration <= 0)
            return;

        this.toggleZoom(((event.clientX - overview.getBoundingClientRect().left) / overview.clientWidth) * this.duration);
    }

    toggleZoom(at) {
        const snaps = this.snaps();

        if (!this.isWhole()) {
            this.rememberedZoom = { ...this.window };
            this.leftBehind = false;
            this.setWindow(0, this.duration, { final: true });

            return;
        }

        let next = this.rememberedZoom;

        if (!next) {
            const width = Math.min(this.grid ? 8 : 8, snaps.length - 1);
            const center = this.indexOf(at);
            const first = clamp(center - Math.floor(width / 2), 0, snaps.length - 1 - width);

            next = { start: snaps[first], end: snaps[first + width] };
        }

        this.leftBehind = false;
        this.setWindow(next.start, next.end, { final: true });
    }

    onOverviewKey(event) {
        const overview = event.target.closest("[data-overview]");

        if (!overview)
            return;

        const slider = event.target.closest("[role=slider]");
        const body = event.target.closest(".win");
        const snaps = this.snaps();
        const first = this.indexOf(this.window.start);
        const last = this.indexOf(this.window.end);
        const step = (event.shiftKey ? 4 : 1) * (event.key === "ArrowLeft" ? -1 : event.key === "ArrowRight" ? 1 : 0);

        if (body && event.key === "Enter") {
            event.preventDefault();

            return this.toggleZoom(this.window.start);
        }

        if (slider && (step || event.key === "Home" || event.key === "End")) {
            event.preventDefault();

            const isStart = slider.classList.contains("l");
            const target = event.key === "Home" ? 0 : event.key === "End" ? snaps.length - 1 : (isStart ? first : last) + step;

            if (isStart)
                this.setWindow(snaps[clamp(target, 0, last - this.minWidthBefore(last))], this.window.end);
            else
                this.setWindow(this.window.start, snaps[clamp(target, first + this.minWidth(first), snaps.length - 1)]);

            this.keyMoved = true;

            return;
        }

        if (body && step) {
            event.preventDefault();

            const shift = clamp(first + step, 0, snaps.length - 1 - (last - first)) - first;

            this.setWindow(snaps[first + shift], snaps[last + shift]);
            this.keyMoved = true;
        }
    }

    // A keyboard move reports on key-up.
    onOverviewKeyUp(event) {
        if (!this.keyMoved || !event.target.closest("[data-overview]"))
            return;

        this.keyMoved = false;

        const at = this.playing ? this.position() : this.parked;

        this.leftBehind = at < this.window.start || at > this.window.end;
        this.setWindow(this.window.start, this.window.end, { final: true });
        this.pushWindow();
    }
}

const shared = new Playback();

export const engine = () => shared;
