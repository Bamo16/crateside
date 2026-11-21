// The warp view's own parts: the playhead, seeking by click, the grid pick, the right-click menu and the zoom keys.

import { QUANTIZE, ZOOM_STEP, clamp, clock, writeSettings } from "./shared.js";

export const View = (Base) =>
    class extends Base {
        // The warp view has drawn: put back everything that lives on the elements it drew.
        // While the window is still moving, or a newer one is waiting to be sent, the server's rows are for an older window: the drawn ones stay.
        viewRendered() {
            const moving = Boolean(this.wheel || this.drag?.moved || this.view?.pending);

            this.stopLive();
            this.findViewElements();
            this.layoutField();
            this.paintWindow();
            this.paintFollow();
            this.paintLanes();
            this.copyShape();

            if (moving) {
                this.liveShown = null;
                this.paintLive();
            }
        }

        paintPlayhead(given) {
            const at = given ?? (this.playing ? this.position() : this.parked);
            const { head, overviewHead, goto } = this.els;
            const { start, end } = this.window;

            if (overviewHead && this.duration > 0)
                overviewHead.style.left = `${clamp(at / this.duration, 0, 1) * 100}%`;

            const inside = at >= start - 1e-3 && at <= end + 1e-3;

            if (head) {
                const ratio = window.devicePixelRatio || 1;
                const across = this.fieldBox?.width ?? 0;
                const x = Math.round((((at - start) / (end - start)) * across - 0.5) * ratio) / ratio;

                // The line and its head ride one transform on whole device pixels, so they never round apart.
                head.hidden = !inside;
                head.style.transform = `translate3d(${x}px,0,0)`;
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
                braces: root?.querySelector("[data-braces]"),
                overviewLoop: root?.querySelector("[data-overview-loop]"),
            });
            this.judging = Boolean(root?.querySelector(".ruler.dragging"));
        }

        layoutField() {
            const { field, head } = this.els;
            const root = this.view?.root;
            const grid = root?.querySelector(".grid");
            const ruler = grid?.querySelector(".ruler");

            if (!field || !grid || !ruler)
                return;

            const bottoms = [...grid.querySelectorAll(".lane > .plot, .key-strip, .chord-strip, .tempo-line, .off-line")];
            // Fractional boxes, not offsetTop and its kin: those round, and the live rows would sit a part-pixel off the rows they cover.
            const parent = ruler.offsetParent ?? grid;
            const origin = parent.getBoundingClientRect();
            const box = ruler.getBoundingClientRect();
            const last = bottoms.reduce((best, element) => Math.max(best, element.getBoundingClientRect().bottom), box.bottom);
            const [left, top] = [box.left - origin.left - parent.clientLeft, box.top - origin.top - parent.clientTop];

            field.style.left = `${left}px`;
            field.style.width = `${box.width}px`;
            field.style.top = `${top}px`;
            field.style.height = `${last - box.top}px`;
            this.fieldBox = { top, width: box.width, height: last - box.top };

            if (head)
                head.hidden = false;
        }

        paintLanes() {
            for (const speaker of this.view?.root.querySelectorAll("[data-speaker]") ?? [])
                speaker.toggleAttribute("hidden", speaker.dataset.speaker !== this.fileId);
        }

        plotTime(event, element) {
            const box = element.getBoundingClientRect();
            const { start, end } = this.window;

            return start + clamp((event.clientX - box.left) / box.width, 0, 1) * (end - start);
        }

        onViewMove(event) {
            if (this.drag)
                return this.onOverviewMove(event);

            if (this.braceDrag)
                return;

            const target = event.target;

            if (target.closest("[data-overview]"))
                return this.hoverOverview(event);

            if (target.closest(".ruler.dragging, .jtag, .edge, .notch.grip"))
                return this.hideSeekLine();

            const row = target.closest(".ruler:not(.dragging), .lane .plot");

            if (row) {
                this.showSeekLine(this.quantized(this.plotTime(event, row)), 0, this.fieldBox?.height ?? 0);
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

            if (target.closest("[data-zoom-in]"))
                return this.zoomBy(1 / ZOOM_STEP);

            if (target.closest("[data-zoom-out]"))
                return this.zoomBy(ZOOM_STEP);

            if (target.closest("[data-grid]"))
                return this.setGridDensity(target.closest("[data-grid]").dataset.grid);

            if (target.closest("[data-later]"))
                return this.pan(1);

            if (target.closest("[data-loop-view]"))
                return this.loopTheView();

            if (target.closest("[data-brace]"))
                return;

            if (target.closest("[data-follow]"))
                return this.setFollow(target.closest("[data-follow]").dataset.follow);

            if (target.closest("[data-goto]")) {
                this.resumeFollow();
                this.followIntoViewForce(this.playing ? this.position() : this.parked);

                return;
            }

            const playWindow = target.closest("[data-play-window]");

            if (playWindow)
                return this.playWindow(playWindow);

            if (target.closest("[data-overview]"))
                return;

            if (target.closest(".jtag, .edge, .notch.grip, button, a, input, select"))
                return;

            // The ruler plays from where it is clicked, as Live's scrub area does; the waveform only moves the playhead.
            const ruler = target.closest(".ruler:not(.dragging)");
            const row = ruler ?? target.closest(".lane .plot");

            if (row)
                this.seek(this.quantized(this.plotTime(event, row)), { play: Boolean(ruler) });
        }

        // The time a click at <at> lands on under the player's Click lands pick: the grid line at or before it, or exactly there with no grid.
        quantized(at) {
            const pick = this.settings.quantize in QUANTIZE ? this.settings.quantize : "bar";
            const step = QUANTIZE[pick];

            if (!this.grid || step === null)
                return at;

            if (step === "bar")
                return this.snaps()[this.floorIndex(at)];

            return Math.max(0, this.grid.secondsAt(Math.floor(this.grid.beatAt(at) / step + 1e-6) * step));
        }

        playWindow(button) {
            const [start, end] = [Number(button.dataset.start), Number(button.dataset.end)];

            if (button.dataset.on === "1")
                return this.pause();

            if (this.loopOn) {
                // The loop is the bars the reading covers, outward to whole bars.
                const snaps = this.snaps();
                const first = this.floorIndex(start);
                const last = Math.max(first + 1, snaps.findIndex((point) => point >= end - 1e-6));

                this.loop = { start: snaps[first], end: snaps[last < 0 ? snaps.length - 1 : last] };
                this.paintLoop();
            }

            this.followIntoView(start);
            this.parked = start;

            if (this.playing)
                this.begin(start);
            else
                this.play();
        }

        // The grid's density, remembered like the player's settings; the server redraws its rows, a drag's live rows follow.
        setGridDensity(density) {
            this.settings.grid = density;
            writeSettings(this.settings);
            this.liveShown = null;

            for (const button of this.view?.root.querySelectorAll("[data-grid]") ?? []) {
                button.setAttribute("aria-pressed", String(button.dataset.grid === density));
                button.setAttribute("aria-checked", String(button.dataset.grid === density));
            }

            this.view?.dotnet.invokeMethodAsync("SetGridDensity", density).catch(() => {});

            if (this.liveLayers)
                this.paintLive();
        }

        // The right-click menu replaces the browser's over the warp rows, and stays inside the viewport.
        onViewMenu(event) {
            const menu = this.view?.root.querySelector("[data-warp-menu]");

            if (!menu || !event.target.closest(".grid"))
                return;

            event.preventDefault();
            menu.hidden = false;
            menu.style.left = `${Math.max(8, Math.min(event.clientX, window.innerWidth - menu.offsetWidth - 8))}px`;
            menu.style.top = `${Math.max(8, Math.min(event.clientY, window.innerHeight - menu.offsetHeight - 8))}px`;
        }

        hideViewMenu() {
            const menu = this.view?.root.querySelector("[data-warp-menu]");

            if (menu)
                menu.hidden = true;
        }

        // + and - zoom about the playhead when it is in the window, else the centre; Ctrl keeps the browser's page zoom.
        onZoomKey(event) {
            if (event.key === "Escape")
                return this.hideViewMenu();

            const zoomIn = event.key === "+" || event.key === "=";

            if ((!zoomIn && event.key !== "-") || event.ctrlKey || event.altKey || event.metaKey || !this.view || this.duration <= 0)
                return;

            if (event.target.closest?.("input:not([type=range]), textarea, select, [contenteditable]"))
                return;

            event.preventDefault();
            this.zoomBy(zoomIn ? 1 / ZOOM_STEP : ZOOM_STEP);
        }
    };
