// The overview strip: its window's body and handles, by pointer and by keyboard.

import { MIN_WINDOW_SECONDS, DRAG_THRESHOLD, clamp, clock } from "./shared.js";

export const Overview = (Base) =>
    class extends Base {
        drag = null;
        dragging = null;

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

        onOverviewDouble(event) {
            const overview = event.target.closest("[data-overview]");

            if (!overview || this.duration <= 0)
                return;

            this.toggleZoom(((event.clientX - overview.getBoundingClientRect().left) / overview.clientWidth) * this.duration);
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
    };
