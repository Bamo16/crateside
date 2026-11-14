// The rows, redrawn here while the window is dragged. The warp view tells the whole track's rows once
// (setLive); a drag draws them from that over the server's own, which are hidden until the rows come back
// drawn for the final window. The drawing mirrors WarpView.razor (ruler, lanes, tempo and off-grid rows,
// axis) and KeyLane.razor.

import { GRID_MIN_PIXELS, COLUMNS, PLOT_PIXELS, FLAG_ROOM, xOf, clampX, pct, px, escapeHtml, clock } from "./shared.js";

export const Rows = (Base) =>
    class extends Base {
        live = null;
        liveLayers = null;
        liveShown = null;
        liveTimer = 0;

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

        // WarpView.razor's GridBox: lines behind the rows, bars over beats over subdivisions, as many as stay at the density's spacing.
        gridHtml(view, scope) {
            const minPixels = GRID_MIN_PIXELS[this.settings.grid];
            const { live, grid } = this;
            const { from, to } = view;
            const lines = [];

            if (!minPixels || !live)
                return "";

            if (live.timeRuler) {
                for (const at of this.timeTicks(from, to))
                    lines.push([at, "bar"]);
            } else if (grid) {
                const [first, last] = [grid.beatAt(from), grid.beatAt(to)];
                const multiple = (beat, unit) => Math.abs(beat / unit - Math.round(beat / unit)) < 1e-4;
                const step = this.gridStep(from, to, minPixels);

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
        gridStep(from, to, minPixels) {
            const { grid } = this;
            const middle = (grid.beatAt(from) + grid.beatAt(to)) / 2;
            const apart = (step) => ((grid.secondsAt(middle + step) - grid.secondsAt(middle)) / (to - from)) * PLOT_PIXELS;
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
            this.putGrid(layer, view);
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
