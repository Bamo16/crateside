// The view's window: what moves it (zoom, pan, follow), the bars it snaps to, and telling the warp view.

import { SNAP, MIN_WINDOW_SECONDS, ZOOM_STEP, WHEEL_PER_STEP, WHEEL_SETTLE_MS, clamp, PLOT_PIXELS, clock } from "./shared.js";

export const Window = (Base) =>
    class extends Base {
        loopOn = false;
        window = { start: 0, end: 0 };
        rememberedZoom = null;
        leftBehind = false;
        wheel = null;

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

            if (this.drag?.moved || this.wheel)
                this.paintLive();

            // A handle or body drag, or a turn of the wheel, moves only the browser's own parts; the rows are told when it ends.
            if (moved && !silent && !this.drag?.moved && !this.wheel)
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

        followIntoViewForce(at) {
            const snaps = this.snaps();
            const width = this.indexOf(this.window.end) - this.indexOf(this.window.start);
            const first = clamp(this.floorIndex(at), 0, snaps.length - 1 - width);

            this.setWindow(snaps[first], snaps[first + width], { final: true });
        }

        // The window a zoom of `factor` (above 1 widens it) gives from `base`, keeping `anchor` at the same place on screen,
        // on whole bars and at least one wide. `force` makes a step that rounds back to where it began move a bar.
        zoomed(base, factor, anchor, force) {
            const snaps = this.snaps();
            const last = snaps.length - 1;
            const width = base.end - base.start;
            const wanted = Math.min(width * factor, this.duration);
            const start = clamp(anchor - ((anchor - base.start) / width) * wanted, 0, this.duration - wanted);
            let first = clamp(this.indexOf(start), 0, last);
            let end = clamp(this.indexOf(start + wanted), 0, last);

            if (force && first === this.indexOf(base.start) && end === this.indexOf(base.end)) {
                if (factor > 1) {
                    if (first > 0)
                        first--;
                    else if (end < last)
                        end++;
                } else if (end - first > this.minWidth(first)) {
                    end--;
                }
            }

            const least = this.minWidth(Math.min(first, last - 1));

            if (end - first < least) {
                end = Math.min(last, first + least);
                first = Math.max(0, end - least);
            }

            return { start: snaps[first], end: snaps[end] };
        }

        // − and +: one step about the window's centre, or about `anchor`.
        zoomBy(factor, anchor) {
            if (this.duration <= 0)
                return;

            const { start, end } = this.window;
            const next = this.zoomed({ start, end }, factor, anchor ?? (start + end) / 2, true);

            this.leftBehind = true;
            this.setWindow(next.start, next.end, { final: true });
        }

        // Ctrl + wheel zooms about the bar under the pointer, shift + wheel pans; a plain wheel scrolls the page.
        // A gesture is the turns until the wheel rests: the window is worked out from where it began, so a pinch's
        // small deltas add up to steps instead of each rounding away, and the rows are told once, when it ends.
        onWheel(event) {
            if ((!event.ctrlKey && !event.shiftKey) || this.duration <= 0 || !this.view)
                return;

            event.preventDefault();
            this.hideViewMenu();

            const ruler = this.view.root.querySelector(".grid > .ruler");
            const mode = event.ctrlKey ? "zoom" : "pan";
            const raw = mode === "pan" && Math.abs(event.deltaX) > Math.abs(event.deltaY) ? event.deltaX : event.deltaY;
            const delta = raw * (event.deltaMode === 1 ? 33 : event.deltaMode === 2 ? 400 : 1);

            if (this.wheel && this.wheel.mode !== mode)
                this.endWheel();

            this.wheel ??= {
                mode,
                base: { ...this.window },
                anchor: ruler ? this.plotTime(event, ruler) : (this.window.start + this.window.end) / 2,
                total: 0,
                timer: 0,
            };

            const gesture = this.wheel;

            gesture.total += delta;
            clearTimeout(gesture.timer);
            gesture.timer = setTimeout(() => this.endWheel(), WHEEL_SETTLE_MS);

            const { base } = gesture;
            let next;

            if (mode === "zoom") {
                next = this.zoomed(base, ZOOM_STEP ** (gesture.total / WHEEL_PER_STEP), gesture.anchor, false);
            } else {
                const snaps = this.snaps();
                const span = this.indexOf(base.end) - this.indexOf(base.start);
                const shifted = base.start + (gesture.total / (ruler?.clientWidth || PLOT_PIXELS)) * (base.end - base.start);
                const at = clamp(this.indexOf(clamp(shifted, 0, this.duration)), 0, snaps.length - 1 - span);

                next = { start: snaps[at], end: snaps[at + span] };
            }

            if (next.start !== this.window.start || next.end !== this.window.end) {
                this.leftBehind = true;
                this.setWindow(next.start, next.end);
            }
        }

        endWheel() {
            const gesture = this.wheel;

            if (!gesture)
                return;

            clearTimeout(gesture.timer);
            this.wheel = null;

            const { start, end } = this.window;

            this.setWindow(start, end, { final: true });

            if (start !== gesture.base.start || end !== gesture.base.end) {
                this.pushWindow();
                this.settleLive();
            } else {
                this.stopLive();
            }
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
    };
