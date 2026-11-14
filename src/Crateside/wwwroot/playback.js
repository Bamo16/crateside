// Playback on Track details (#105, ADR 0094): one file at a time, decoded with Web Audio and played
// from memory, with a synthesized click scheduled on the same clock. The player strip and the warp
// view are Blazor components; this module owns everything that moves at the pointer's or the audio's
// pace (the playhead, seeking, the view's window) and tells them only what they must draw.
// Each concern is a class in ./playback/ that this one is built from; they share its state through `this`.

import { AudioEngine } from "./playback/audio.js";
import { Loop } from "./playback/loop.js";
import { Follow } from "./playback/follow.js";
import { Overview } from "./playback/overview.js";
import { PlayerStrip } from "./playback/player.js";
import { Rows } from "./playback/rows.js";
import { View } from "./playback/view.js";
import { Window } from "./playback/window.js";
import { makeGrid } from "./playback/grid.js";
import { SNAP, readSettings } from "./playback/shared.js";

class Playback extends View(Rows(Overview(Follow(Loop(Window(PlayerStrip(AudioEngine(class {})))))))) {
    settings = { volume: 0.8, clickLevel: 0.6, sound: "classic", grid: "medium", follow: "page", ...readSettings() };
    trackKey = null;
    files = [];
    fileId = null;
    duration = 0;
    grid = null;
    player = null;
    view = null;
    els = {};
    frame = 0;

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
        this.loop = null;
        this.rememberedZoom = null;
        this.window = { start: 0, end: 0 };
    }

    mountView(root, dotnet) {
        this.unmountView();
        this.view = { root, dotnet, abort: new AbortController(), inFlight: false, pending: null };
        this.following = this.settings.follow;

        const { signal } = this.view.abort;

        root.addEventListener("click", (event) => this.onViewClick(event), { signal });
        root.addEventListener("pointermove", (event) => this.onViewMove(event), { signal });
        root.addEventListener("pointerleave", () => this.hideSeekLine(), { signal });
        root.addEventListener("pointerdown", (event) => this.onOverviewDown(event), { signal });
        root.addEventListener("pointerdown", (event) => this.onBraceDown(event), { signal });
        root.addEventListener("keydown", (event) => this.onBraceKey(event), { signal });
        root.addEventListener("dblclick", (event) => this.onOverviewDouble(event), { signal });
        root.addEventListener("keydown", (event) => this.onOverviewKey(event), { signal });
        root.addEventListener("keyup", (event) => this.onOverviewKeyUp(event), { signal });
        // Not passive: ctrl + wheel is ours, and the browser's page zoom must not also fire.
        root.addEventListener("wheel", (event) => this.onWheel(event), { signal, passive: false });
        root.addEventListener("contextmenu", (event) => this.onViewMenu(event), { signal });
        document.addEventListener("click", () => this.hideViewMenu(), { signal });
        document.addEventListener("keydown", (event) => this.onZoomKey(event), { signal });

        // The server draws the grid, and cannot read what the browser remembers.
        if (this.settings.grid !== "medium")
            dotnet.invokeMethodAsync("SetGridDensity", this.settings.grid).catch(() => {});

        this.observer = new ResizeObserver(() => this.layoutField());
        this.observer.observe(root);
        this.startFrames();
    }

    unmountView() {
        if (!this.view)
            return;

        this.stopLive();
        clearTimeout(this.wheel?.timer);
        this.wheel = null;
        this.view.abort.abort();
        this.observer?.disconnect();
        this.view = null;
        this.drag = null;
        this.braceDrag = null;
        this.scrolling = null;
        document.body.classList.remove("pb-locked", "pb-locked-start", "pb-locked-end", "pb-locked-body");
    }

    setTrack(trackKey, files, defaultId) {
        const before = `${this.trackKey}|${this.fileId}`;

        if (this.trackKey !== trackKey) {
            this.stopAudio();
            this.playing = this.wantPlay = false;
            this.buffer = this.bufferId = this.loading = null;
            this.parked = 0;
            this.failure = null;
            this.loopOn = false;
            this.loop = null;
            this.rememberedZoom = null;
            this.fileId = null;

            // The warp view may have told its grid for this track before the player told its name.
            if (this.trackKey !== null) {
                this.duration = 0;
                this.grid = null;
                this.snapCache = null;
                this.window = { start: 0, end: 0 };
            }

            this.trackKey = trackKey;
        }

        this.files = files;

        if (!files.some((file) => file.id === this.fileId))
            this.fileId = defaultId ?? files[0]?.id ?? null;

        this.syncPlayer();
        this.prefetch();

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

        if (this.loop && this.loop.end > this.duration + SNAP) {
            this.loop = null;
            this.loopOn = false;
        }

        this.laneFiles = laneFiles ?? [];
        this.syncPlayer();
        this.viewRendered();
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
        this.follow(at);
        this.paintOverviewWindow();
        this.paintPlayhead(at);
        this.paintBeat();
    }
}

const shared = new Playback();

export const engine = () => shared;
