// The view's window: what moves it (zoom, pan), the bars it snaps to, and telling the warp view.

import { SNAP, MIN_WINDOW_SECONDS, OVERVIEW_MIN_PIXELS, ZOOM_STEP, WHEEL_PER_STEP, WHEEL_SETTLE_MS, clamp, PLOT_PIXELS, clock } from "./shared.js";

export const Window = (Base) =>
    class extends Base {
        window = { start: 0, end: 0 };
        rememberedZoom = null;
        wheel = null;

        // A seek to a spot outside the window brings the window, at its width, to start on that bar.
        followIntoView(at) {
            const { start, end } = this.window;

            if (!this.view || (at >= start && at < end) || this.isWhole())
                return;

            const snaps = this.snaps();
            const width = this.indexOf(end) - this.indexOf(start);
            const first = clamp(this.floorIndex(at), 0, snaps.length - 1 - width);

            this.setWindow(snaps[first], snaps[first + width], { final: true });
        }

        isWhole() {
            return this.window.start <= SNAP && this.window.end >= this.duration - 0.001;
        }

        // Everything that moves the window goes through here; the warp view is told, coalesced.
        setWindow(start, end, { final = false, silent = false } = {}) {
            const moved = start !== this.window.start || end !== this.window.end;

            this.window = { start, end };

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

        paintWindow() {
            const overview = this.els.overview;

            if (!overview || this.duration <= 0)
                return;

            const { start, end } = this.window;
            const whole = this.isWhole();

            this.overviewBox = null;
            this.paintOverviewWindow();

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
                hint.textContent = whole ? "pull a handle to zoom · double-click for 8 bars" : "drag to move · double-click for the whole track";

            this.paintLoop();
        }

        // The overview's window and the dimming either side of it, placed in one write on whole device pixels, so both
        // edges move on the same frame. It is never drawn narrower than OVERVIEW_MIN_PIXELS: a tighter window keeps
        // that size, centred on the range it stands for.
        paintOverviewWindow() {
            const overview = this.els.overview;

            if (!overview || this.duration <= 0)
                return;

            const across = overview.clientWidth;
            const { start, end } = this.window;
            const shown = this.overviewBox;

            if (shown && shown.across === across && shown.start === start && shown.end === end)
                return;

            const ratio = window.devicePixelRatio || 1;
            const snap = (pixels) => Math.round(pixels * ratio) / ratio;
            const [from, to] = [(start / this.duration) * across, (end / this.duration) * across];
            const width = snap(Math.max(to - from, Math.min(OVERVIEW_MIN_PIXELS, across)));
            const left = snap(clamp((from + to - width) / 2, 0, across - width));
            const whole = this.isWhole();
            const set = (selector, style) => {
                const element = overview.querySelector(selector);

                if (element)
                    Object.assign(element.style, style);
            };

            this.overviewBox = { across, start, end, left, width };
            set(".win", { left: "0", width: `${width}px`, transform: `translate3d(${left}px,0,0)` });
            set(".dim.a", { left: "0", width: `${whole ? 0 : left}px` });
            set(".dim.b", { left: `${left + width}px`, right: "0", display: whole ? "none" : "block" });
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

        // − and +, their keys and the menu's items: one step about the playhead, which goes to the window's centre
        // unless the track's start or end is in the way.
        zoomBy(factor) {
            if (this.duration <= 0)
                return;

            if (this.canScroll())
                return this.zoomScrolling(this.window, factor, true);

            const at = this.playing ? this.position() : this.parked;
            const half = (this.window.end - this.window.start) / 2;
            const next = this.zoomed({ start: at - half, end: at + half }, factor, at, true);

            this.setWindow(next.start, next.end, { final: true });
        }

        // While the view scrolls a zoom changes only the window's width, by whole bars from `base`: the playhead keeps
        // its place on screen, and the scroll draws its strip again on the next frame.
        zoomScrolling(base, factor, force) {
            const at = this.position();
            const { start, end } = this.window;
            const place = clamp((at - start) / (end - start), 0, 1);
            const half = (base.end - base.start) / 2;
            const next = this.zoomed({ start: at - half, end: at + half }, factor, at, force);
            const width = next.end - next.start;
            const from = clamp(at - place * width, 0, this.duration - width);

            this.window = { start: from, end: from + width };
            this.rememberIfZoomed();
        }

        // Ctrl + wheel zooms about the bar under the pointer, or about the playhead while the view follows it in
        // playback; shift + wheel pans; a plain wheel scrolls the page.
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

            const scrolls = this.wheel?.scrolls ?? (mode === "zoom" && this.canScroll());

            if (!scrolls)
                this.settleScroll();

            const at = this.playing ? this.position() : this.parked;
            const followed = this.playing && this.following !== "off" && at >= this.window.start && at <= this.window.end;

            this.wheel ??= {
                mode,
                scrolls,
                base: { ...this.window },
                anchor: followed ? at : ruler ? this.plotTime(event, ruler) : (this.window.start + this.window.end) / 2,
                total: 0,
                timer: 0,
            };

            const gesture = this.wheel;

            gesture.total += delta;
            clearTimeout(gesture.timer);
            gesture.timer = setTimeout(() => this.endWheel(), WHEEL_SETTLE_MS);

            const { base } = gesture;
            let next;

            if (gesture.scrolls) {
                if (this.canScroll())
                    this.zoomScrolling(base, ZOOM_STEP ** (gesture.total / WHEEL_PER_STEP), false);

                return;
            }

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
                if (mode === "pan")
                    this.releaseFollow();

                this.setWindow(next.start, next.end);
            }
        }

        endWheel() {
            const gesture = this.wheel;

            if (!gesture)
                return;

            clearTimeout(gesture.timer);
            this.wheel = null;

            // A zoom while scrolling moved nothing the server draws; it hears when the scroll ends.
            if (gesture.scrolls)
                return;

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

            this.releaseFollow();
            this.setWindow(snaps[next], snaps[next + (last - first)], { final: true });
        }

        toggleZoom(at) {
            const snaps = this.snaps();

            if (!this.isWhole()) {
                this.rememberedZoom = { ...this.window };
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

            this.setWindow(next.start, next.end, { final: true });
        }
    };
