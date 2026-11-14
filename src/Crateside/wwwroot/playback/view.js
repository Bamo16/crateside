// The warp view's own parts: the playhead, seeking by click, the grid pick, the right-click menu and the zoom keys.

import { ZOOM_STEP, clamp, clock, writeSettings } from "./shared.js";

export const View = (Base) =>
    class extends Base {
        // The warp view has drawn: put back everything that lives on the elements it drew.
        viewRendered() {
            this.stopLive();
            this.findViewElements();
            this.layoutField();
            this.paintWindow();
            this.paintFollow();
            this.paintLanes();
            this.copyShape();
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

            const keys = target.closest(".key-strip");

            // Under the sliding strip the key's bands are not the server's to click.
            if (keys && this.strip) {
                const at = this.plotTime(event, keys);
                const under = this.live?.keys?.find((key) => at >= key.start && at < key.end);

                if (under)
                    this.seek(under.start);

                return;
            }

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

            const at = this.playing ? this.position() : this.parked;
            const { start, end } = this.window;

            event.preventDefault();
            this.zoomBy(zoomIn ? 1 / ZOOM_STEP : ZOOM_STEP, at >= start && at <= end ? at : undefined);
        }
    };
