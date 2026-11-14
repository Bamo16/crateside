// The loop: a range of its own between two braces over the ruler, apart from the view's window.

import { SNAP, GRID_MIN_PIXELS, DRAG_THRESHOLD, clamp, clock } from "./shared.js";

export const Loop = (Base) =>
    class extends Base {
        loopOn = false;
        loop = null;
        braceDrag = null;

        setLoop(on) {
            if (on && !this.loop && this.duration > 0)
                this.loop = { ...this.window };

            this.loopOn = on && Boolean(this.loop);
            this.applyLoop();
            this.syncPlayer();
        }

        // The braces take the view's window, whether or not the loop is on.
        loopTheView() {
            if (this.duration <= 0)
                return;

            this.loop = { ...this.window };
            this.applyLoop();
        }

        // A change to the loop reaches the audio: a playhead outside a loop that is on goes to its start.
        applyLoop() {
            const at = this.playing ? this.position() : this.parked;

            if (this.loopOn && this.loop && (at < this.loop.start || at >= this.loop.end))
                this.seek(this.loop.start);
            else if (this.loopOn || this.session?.loop)
                this.restart();

            this.paintLoop();
        }

        // What the braces snap to, as positions along the track: the grid's lines in beats (bars with the grid off), or whole seconds.
        loopScale() {
            const { grid } = this;

            if (!grid || this.live?.timeRuler)
                return { at: (position) => position, of: (seconds) => seconds, step: 1 };

            const minPixels = GRID_MIN_PIXELS[this.settings.grid];
            const step = (minPixels && this.gridStep(this.window.start, this.window.end, minPixels)) || grid.perBar;

            return { at: grid.secondsAt, of: grid.beatAt, step };
        }

        paintLoop() {
            const { braces, overviewLoop } = this.els;
            const { loop, duration } = this;
            const shown = Boolean(loop) && duration > 0;

            if (overviewLoop) {
                overviewLoop.hidden = !shown;

                if (shown) {
                    overviewLoop.style.left = `${(loop.start / duration) * 100}%`;
                    overviewLoop.style.width = `${((loop.end - loop.start) / duration) * 100}%`;
                    overviewLoop.classList.toggle("on", this.loopOn);
                }
            }

            if (!braces)
                return;

            const { start: from, end: to } = this.window;
            const x = (at) => ((at - from) / (to - from)) * 100;
            const visible = shown && loop.end > from && loop.start < to;

            braces.hidden = !visible;

            if (!visible)
                return;

            const [left, right] = [clamp(x(loop.start), 0, 100), clamp(x(loop.end), 0, 100)];
            const [startIn, endIn] = [loop.start >= from - SNAP, loop.end <= to + SNAP];

            braces.classList.toggle("on", this.loopOn);
            braces.style.left = `${left}%`;
            braces.style.width = `${right - left}%`;
            braces.classList.toggle("open-left", !startIn);
            braces.classList.toggle("open-right", !endIn);

            for (const [name, at, label] of [
                ["start", loop.start, "Loop start"],
                ["end", loop.end, "Loop end"],
            ]) {
                const handle = braces.querySelector(`[data-brace="${name}"]`);

                handle?.setAttribute("aria-label", label);
                handle?.setAttribute("aria-valuenow", String(Math.round(at * 100) / 100));
                handle?.setAttribute("aria-valuetext", this.grid ? `bar ${this.grid.position(at + 1e-6)}` : clock(at));
            }

            braces.querySelector('[data-brace="body"]')?.setAttribute("title", this.loopTitle());
        }

        loopTitle() {
            const { start, end } = this.loop;
            const span = this.grid ? `${this.grid.position(start + 1e-6)} to ${this.grid.position(end + 1e-6)}` : `${clock(start)}–${clock(end)}`;

            return `Loop · ${span}${this.loopOn ? "" : " · off"}`;
        }

        onBraceDown(event) {
            const part = event.target.closest?.("[data-brace]");

            if (!part || event.button !== 0 || !this.loop || this.braceDrag || this.drag)
                return;

            event.preventDefault();
            event.stopPropagation();
            part.setPointerCapture(event.pointerId);
            part.focus?.({ preventScroll: true });

            const region = part.dataset.brace;
            const listeners = {
                pointermove: (move) => this.onBraceMove(move),
                pointerup: () => this.finishBraceDrag(),
                pointercancel: () => this.cancelBraceDrag(),
                lostpointercapture: () => this.braceDrag && this.finishBraceDrag(),
            };

            this.braceDrag = {
                part,
                region,
                pointerId: event.pointerId,
                x0: event.clientX,
                moved: false,
                before: { ...this.loop },
                grabbed: this.plotTime(event, this.els.field),
                scale: this.loopScale(),
                listeners,
            };
            document.body.classList.add("pb-locked", `pb-locked-${region}`);
            this.hideSeekLine();

            for (const [name, handler] of Object.entries(listeners))
                part.addEventListener(name, handler);
        }

        onBraceMove(event) {
            const drag = this.braceDrag;

            if (!drag)
                return;

            if (event.buttons === 0 && event.pointerType === "mouse")
                return this.finishBraceDrag();

            if (!drag.moved && Math.abs(event.clientX - drag.x0) < DRAG_THRESHOLD)
                return;

            drag.moved = true;
            this.loop = this.bracesMoved(drag, this.plotTime(event, this.els.field));
            this.paintLoop();
        }

        // The loop a drag gives: an end goes to the grid line nearest the pointer and stops a step short of the other; the bar moves both by whole steps.
        bracesMoved({ region, before, grabbed, scale }, pointer) {
            const { at, of, step } = scale;
            const [low, high] = [of(0), of(this.duration)];
            const [start, end] = [of(before.start), of(before.end)];
            const line = (position) => Math.round(position / step) * step;
            const seconds = (position) => clamp(at(position), 0, this.duration);

            if (region === "start") {
                const wanted = of(pointer) <= low ? low : line(of(pointer));

                return { start: seconds(Math.min(wanted, end - step)), end: before.end };
            }

            if (region === "end") {
                const wanted = of(pointer) >= high ? high : line(of(pointer));

                return { start: before.start, end: seconds(Math.max(wanted, start + step)) };
            }

            const steps = Math.round((of(pointer) - of(grabbed)) / step);
            const shift = clamp(steps * step, Math.ceil((low - start) / step - SNAP) * step, Math.floor((high - end) / step + SNAP) * step);

            return { start: seconds(start + shift), end: seconds(end + shift) };
        }

        endBraceDrag() {
            const drag = this.braceDrag;

            if (!drag)
                return null;

            for (const [name, handler] of Object.entries(drag.listeners))
                drag.part.removeEventListener(name, handler);

            try {
                drag.part.releasePointerCapture(drag.pointerId);
            } catch {
                // Already released.
            }

            document.body.classList.remove("pb-locked", "pb-locked-start", "pb-locked-end", "pb-locked-body");
            this.braceDrag = null;

            return drag;
        }

        finishBraceDrag() {
            if (this.endBraceDrag()?.moved)
                this.applyLoop();
        }

        cancelBraceDrag() {
            const drag = this.endBraceDrag();

            if (drag?.moved) {
                this.loop = drag.before;
                this.paintLoop();
            }
        }

        // ← and → move a focused brace, or the bar between them, a grid line; Esc puts a dragged one back.
        onBraceKey(event) {
            const part = event.target.closest?.("[data-brace]");

            if (!part || !this.loop)
                return;

            if (event.key === "Escape" && this.braceDrag)
                return this.cancelBraceDrag();

            const direction = event.key === "ArrowLeft" ? -1 : event.key === "ArrowRight" ? 1 : 0;

            if (!direction || this.braceDrag)
                return;

            event.preventDefault();

            const scale = this.loopScale();
            const region = part.dataset.brace;
            const from = region === "end" ? this.loop.end : this.loop.start;
            const pointer = scale.at(scale.of(from) + direction * scale.step * (event.shiftKey ? 4 : 1));

            this.loop = this.bracesMoved({ region, before: { ...this.loop }, grabbed: from, scale }, clamp(pointer, 0, this.duration));
            this.applyLoop();
        }
    };
