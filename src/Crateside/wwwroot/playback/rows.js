// The rows, redrawn here while the window is dragged or scrolls with the playhead. The warp view tells the
// whole track's rows once (setLive); a drag draws them from that over the server's own, which are hidden
// until the rows come back drawn for the final window. Scroll follow draws a strip several views wide and
// slides it. The drawing mirrors WarpView.razor (ruler, lanes, tempo and off-grid rows, axis) and KeyLane.razor.

import { GRID_MIN_PIXELS, COLUMNS, PLOT_PIXELS, FLAG_ROOM, xOf, clampX, clamp, pct, px as plotPx, escapeHtml, clock } from "./shared.js";

// KeyLane's label: the room a band needs to carry one, and its insets in the band (KeyLane.razor.css).
const KEY_LABEL_ROOM = 56;
const KEY_LABEL_INSET = { left: 5, right: 3 };

export const Rows = (Base) =>
    class extends Base {
        live = null;
        liveLayers = null;
        liveShown = null;
        liveTimer = 0;
        // Scroll follow's strip: the stretch of the track it is drawn for, how many views wide that is, and its sliding box.
        strip = null;

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
            const { strip } = this;
            const into = strip?.box ?? field;
            const origin = ruler.getBoundingClientRect();
            const nudge = strip?.nudge ?? { x: 0, y: 0 };
            const groundOf = (element) => {
                for (let behind = element; behind; behind = behind.parentElement) {
                    const color = getComputedStyle(behind).backgroundColor;

                    if (color !== "transparent" && color !== "rgba(0, 0, 0, 0)")
                        return color;
                }

                return "Canvas";
            };
            const cover = (target, kind, lane) => {
                if (!target)
                    return;

                const layer = target.cloneNode(false);
                const box = target.getBoundingClientRect();

                layer.removeAttribute("id");
                layer.removeAttribute("data-snaps");
                layer.classList.add("live");
                Object.assign(layer.style, {
                    position: "absolute",
                    margin: "0",
                    left: `${box.left - origin.left - nudge.x}px`,
                    top: `${box.top - origin.top - nudge.y}px`,
                    width: `${box.width * (strip?.wide ?? 1)}px`,
                    height: `${box.height}px`,
                });

                // Over a solid ground a sliding layer's text keeps the page's subpixel rendering, and a row's own border is not drawn twice.
                if (kind === "ruler" || kind === "keys" || kind === "axis")
                    layer.style.backgroundColor = groundOf(target);

                // A row's fixed labels: copied onto a still layer, left showing through over a sliding one.
                for (const kept of strip ? [] : target.querySelectorAll(".range, .off-label"))
                    layer.append(kept.cloneNode(true));

                into.insertBefore(layer, into.firstChild);

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

            if (this.strip) {
                const { field, braces } = this.els;

                this.strip.box.remove();
                this.strip = null;
                // The braces go back to the window's scale before the field stops cutting them off.
                this.paintLoop();
                field?.style.removeProperty("overflow-x");
                braces?.style.removeProperty("transform");
            }
        }

        // The rows for the window as it stands, over the server's.
        paintLive() {
            const { live } = this;

            if (!live || this.duration <= 0)
                return;

            if (this.strip)
                this.stopLive();

            this.startLive();

            if (!this.liveLayers)
                return;

            const { start: from, end: to } = this.window;

            if (this.liveShown?.[0] === from && this.liveShown?.[1] === to)
                return;

            this.liveShown = [from, to];
            this.drawLive({ from, to, wide: 1 });
        }

        // A strip from `from` to `to`, `wide` views across, in a box that slideStrip moves; the field cuts it off at the plot's sides.
        paintStrip(from, to, wide) {
            const { field } = this.els;

            this.stopLive();

            if (!this.live || !field || this.duration <= 0)
                return;

            const box = document.createElement("div");
            // The box is drawn on a layer of its own (its 3D transform; will-change would cost the text its subpixel
            // rendering), which lands on whole device pixels: it starts on one, so its rows keep the places the server's have.
            const ratio = window.devicePixelRatio || 1;
            const at = field.getBoundingClientRect();
            const toPixel = (value) => (Math.round(value * ratio) - value * ratio) / ratio;
            const nudge = { x: toPixel(at.left), y: toPixel(at.top) };

            Object.assign(box.style, { position: "absolute", left: `${nudge.x}px`, top: `${nudge.y}px`, width: "0", height: "0" });
            field.insertBefore(box, field.firstChild);
            // Cut off at the plot's sides only: the loop's braces sit above the field and the axis hangs under it.
            field.style.overflowX = "clip";
            this.strip = { from, to, wide, box, nudge };
            this.startLive();

            if (!this.liveLayers) {
                this.stopLive();

                return;
            }

            this.drawLive({ from, to, wide });
            this.paintLoop();
        }

        // The strip, and the braces drawn on its scale, moved so that `start` is at the plot's left edge, on a whole device pixel.
        slideStrip(start) {
            const { strip } = this;
            const { field, braces } = this.els;

            if (!strip || !field)
                return;

            const ratio = window.devicePixelRatio || 1;
            const across = this.fieldBox?.width ?? field.offsetWidth;
            const offset = Math.round(((start - strip.from) / (strip.to - strip.from)) * strip.wide * across * ratio) / ratio;
            const transform = `translate3d(${-offset}px,0,0)`;

            if (strip.transform === transform && strip.labels)
                return;

            strip.transform = transform;
            strip.box.style.transform = transform;

            if (braces)
                braces.style.transform = transform;

            this.slideLabels(offset, across);
        }

        // A band's label keeps to the view's left edge while its band runs off it, and is cut off at the view's right:
        // where the server's rows put it, since they clamp a band to the view.
        slideLabels(offset, across) {
            const { strip } = this;

            strip.labels ??= [...strip.box.querySelectorAll(".jtag, .key-label")].map((label) => {
                const band = label.parentElement;
                const wide = parseFloat(band.parentElement.style.width);

                return { label, left: (parseFloat(band.style.left) / 100) * wide, width: (parseFloat(band.style.width) / 100) * wide };
            });

            for (const entry of strip.labels) {
                const { label, left, width } = entry;
                const shift = clamp(offset - left, 0, width);
                const shown = Math.max(0, Math.min(left + width, offset + across) - Math.max(left, offset));
                const placed = `${shift}|${shown}`;

                if (entry.placed === placed)
                    continue;

                entry.placed = placed;

                if (label.classList.contains("key-label")) {
                    label.style.left = `${shift + KEY_LABEL_INSET.left}px`;
                    label.style.right = `${width - shift - shown + KEY_LABEL_INSET.right}px`;
                    label.style.visibility = shown >= KEY_LABEL_ROOM ? "" : "hidden";
                } else {
                    label.style.left = `${shift}px`;
                    label.style.maxWidth = `${shown}px`;
                }
            }
        }

        // What the plot's percentages are of: the window, or the strip while one is drawn.
        plotSpan() {
            const { strip } = this;

            return strip ? { from: strip.from, to: strip.to, wide: strip.wide } : { from: this.window.start, to: this.window.end, wide: 1 };
        }

        drawLive(view) {
            const { from, to, wide } = view;

            view.zoomed = (to - from) / wide < this.live.seconds - 0.001;

            if (this.strip)
                this.strip.labels = null;

            for (const layer of this.liveLayers) {
                const bands = this.bandsHtml(view, layer, layer.kind === "ruler");

                if (layer.kind === "ruler")
                    layer.layer.innerHTML = this.rulerHtml(view, layer.scope) + bands;
                else if (layer.kind === "axis")
                    layer.layer.innerHTML = this.timeTicks(from, to, wide)
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

                layer.target.setAttribute("data-covered", this.strip ? "strip" : "");
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

        // `wide` is how many views the stretch is drawn across: the spacing goes by one view's seconds.
        timeTicks(from, to, wide = 1) {
            const shown = (to - from) / wide;
            const every = shown > 120 ? 30 : shown > 40 ? 10 : 2;
            const ticks = [];

            for (let at = Math.ceil(from / every) * every; at <= to; at += every)
                ticks.push(at);

            return ticks;
        }

        // WarpView.razor's ruler: time ticks without a warp, else the grid's bars, the loop and the clip start.
        rulerHtml(view, scope) {
            const { live, grid } = { live: this.live, grid: this.grid };
            const { from, to, wide } = view;
            const px = (pixels) => plotPx(pixels) / wide;
            const html = [];
            const clipX = live.clip && xOf(view, live.clip.start) >= 0 && xOf(view, live.clip.start) <= 100 ? xOf(view, live.clip.start) : null;
            const toLeft = clipX !== null && clipX > 100 - px(72);
            const underClip = (at) => clipX !== null && (toLeft ? at > clipX - px(90) && at < clipX - px(7) : at > clipX - px(19) && at < clipX + px(66));

            if (live.timeRuler) {
                for (const at of this.timeTicks(from, to, wide)) {
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

                if (xOf(view, end) >= 0 && xOf(view, end) <= 100 - 8 / wide)
                    html.push(`<span ${scope} class="mono loop-word" style="left:calc(${pct(xOf(view, end))}% + 4px)">${on ? "loop" : "loop off"}</span>`);
            }

            if (grid && !live.timeRuler) {
                const first = grid.beatAt(from);
                const last = grid.beatAt(to);
                const bars = (last - first) / grid.perBar / wide;
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

        // WarpView.razor's GridBox: lines behind the rows, bars over beats over subdivisions, as many as stay at the density's spacing.
        gridHtml(view, scope) {
            const minPixels = GRID_MIN_PIXELS[this.settings.grid];
            const { live, grid } = this;
            const { from, to, wide } = view;
            const lines = [];

            if (!minPixels || !live)
                return "";

            if (live.timeRuler) {
                for (const at of this.timeTicks(from, to, wide))
                    lines.push([at, "bar"]);
            } else if (grid) {
                const [first, last] = [grid.beatAt(from), grid.beatAt(to)];
                const multiple = (beat, unit) => Math.abs(beat / unit - Math.round(beat / unit)) < 1e-4;
                const step = this.gridStep(from, to, minPixels, wide);

                for (let n = step ? Math.ceil(first / step - 1e-6) : 0; step && n * step <= last + 1e-6; n++) {
                    const beat = n * step;
                    const kind = multiple(beat, grid.perBar) ? "bar" : multiple(beat, grid.unit) ? "beat" : "subdivision";

                    lines.push([grid.secondsAt(beat), kind]);
                }
            }

            if (lines.length === 0)
                return "";

            return `<div ${scope} class="gl-box" aria-hidden="true">${lines.map(([at, kind]) => `<span ${scope} class="gl ${kind}" style="left:${pct(xOf(view, at))}%"></span>`).join("")}</div>`;
        }

        // The grid's spacing in beats over a view: the finest of 1/16, 1/8, beat and bar that stays `minPixels` apart, else every 2nd, 4th, 8th… bar.
        gridStep(from, to, minPixels, wide = 1) {
            const { grid } = this;
            const middle = (grid.beatAt(from) + grid.beatAt(to)) / 2;
            const apart = (step) => ((grid.secondsAt(middle + step) - grid.secondsAt(middle)) / (to - from)) * PLOT_PIXELS * wide;
            const step = [grid.unit / 4, grid.unit / 2, grid.unit, grid.perBar].find((candidate) => apart(candidate) >= minPixels);

            if (step)
                return step;

            for (let bars = 2; bars <= 1024; bars *= 2) {
                if (apart(grid.perBar * bars) >= minPixels)
                    return grid.perBar * bars;
            }

            return null;
        }

        // The grid goes first in its row, behind everything else drawn there.
        putGrid(layer, view) {
            layer.layer.querySelectorAll(":scope > .gl-box").forEach((box) => box.remove());
            layer.layer.insertAdjacentHTML("afterbegin", this.gridHtml(view, layer.scope));
        }

        keysHtml(view, { scope }) {
            return (this.live.keys ?? [])
                .map((band) => {
                    const [left, right] = [clampX(view, band.start), clampX(view, band.end)];

                    if (right <= left)
                        return "";

                    const classes = `key-band${band.fromWindow ? " from-window" : ""}${band.unbacked ? " unbacked" : ""}${band.pickable ? " pickable" : ""}${band.picked ? " picked" : ""}`;
                    const label = ((right - left) / 100) * PLOT_PIXELS * view.wide >= KEY_LABEL_ROOM ? `<span ${scope} class="mono key-label">${escapeHtml(band.label)}</span>` : "";

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
            this.putGrid(layer, view);
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
        paintLane(layer, view, bands) {
            const { lane, path, dyn, scope } = layer;
            const { from, to, wide } = view;
            const columns = Math.round(COLUMNS * wide);
            const html = [];

            path.ownerSVGElement.setAttribute("viewBox", `0 0 ${columns} 45`);
            path.setAttribute("d", this.wavePath(lane.peaks, from, to, columns));

            const visible = lane.markers.map((at) => xOf(view, at)).filter((at) => at >= 0 && at <= 100);
            const flags = visible.every((at, i) => i === 0 || ((at - visible[i - 1]) / 100) * PLOT_PIXELS * wide >= FLAG_ROOM);

            for (const at of lane.markers) {
                const at100 = xOf(view, at);

                if (at100 < 0 || at100 > 100)
                    continue;

                html.push(`<span ${scope} class="marker-line${flags ? "" : " dense"}" style="left:${pct(at100)}%"></span>`);
                html.push(`<span ${scope} class="${flags ? "flag" : "flag-tick"}" style="left:${pct(at100)}%"></span>`);
            }

            if (view.zoomed) {
                const ticks = [];

                for (const at of lane.transients) {
                    const at100 = xOf(view, at);

                    if (at100 >= 0 && at100 <= 100)
                        ticks.push(`M${pct((at100 * COLUMNS) / 100)},58v5`);
                }

                html.push(
                    `<svg ${scope} class="transients" viewBox="0 0 ${COLUMNS} 64" preserveAspectRatio="none"><path ${scope} d="${ticks.join("")}"/></svg>`,
                );
            }

            const clip = xOf(view, lane.clip);

            if (clip >= 0 && clip <= 100)
                html.push(`<div ${scope} class="clip-line" style="left:${pct(clip)}%"></div>`);

            if (lane.parts !== null && lane.parts !== undefined)
                html.push(`<div ${scope} class="differs" style="left:${pct(Math.max(0, xOf(view, lane.parts)))}%"></div>`);

            dyn.innerHTML = html.join("") + bands;
            this.putGrid(layer, view);
        }

        // `across` columns of the plot, each the loudest of the table's columns it covers: WavePeaks.Envelope's arithmetic, so the server's lanes match.
        wavePath({ columns, channels, bytes, perColumn }, from, to, across) {
            const gap = channels > 1 ? 2 : 0;
            const each = (45 - gap * (channels - 1)) / channels;
            const path = [];

            for (let channel = 0; channel < channels; channel++) {
                const center = channel * (each + gap) + each / 2;
                const base = channel * columns * 2;
                const upper = new Array(across);
                const lower = new Array(across);

                for (let x = 0; x < across; x++) {
                    const first = Math.floor((from + ((to - from) * x) / across) / perColumn);
                    const last = Math.max(first, Math.floor((from + ((to - from) * (x + 1)) / across) / perColumn) - 1);
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

                    upper[x] = `${x + 0.5},${pct(center - (max * each) / 2)}`;
                    lower[across - 1 - x] = `${x + 0.5},${pct(center - (min * each) / 2)}`;
                }

                path.push(`M${upper.join(" ")} ${lower.join(" ")}Z`);
            }

            return path.join("");
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
    };
