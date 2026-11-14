// The player strip: its controls, the position readout and the slim waveform.

import { clamp, clock, writeSettings } from "./shared.js";

export const PlayerStrip = (Base) =>
    class extends Base {
        // Until the file is decoded, the slim waveform borrows the overview strip's shape.
        copyShape() {
            const source = this.view?.root.querySelector("[data-overview] svg path");
            const target = this.player?.root.querySelector("[data-shape] path");

            if (source && target)
                target.setAttribute("d", source.getAttribute("d") ?? "");
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
                beat: root.querySelector("[data-beat]"),
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
                button.disabled = !hasFiles;
                button.setAttribute("aria-pressed", String(this.loopOn));
                button.classList.toggle("on", this.loopOn);
            });
            set("[data-click-menu]", (menu) => {
                for (const item of menu.querySelectorAll("[data-sound]"))
                    item.classList.toggle("on", item.dataset.sound === this.settings.sound);
            });

            this.copyShape();
            this.drawWave();
            this.paintPosition(this.playing ? this.position() : this.parked);
        }

        // The beat light in the Metronome button: its two dots swap as each click is heard, the lit one larger on a
        // bar's first beat. A click is heard its latency after the time it was scheduled for.
        paintBeat() {
            const { beat } = this.els;

            if (!beat)
                return;

            const shown = this.playing && this.clickOn && Boolean(this.session);

            if (beat.hidden === shown)
                beat.hidden = !shown;

            if (!shown)
                return;

            const heard = this.ctx.currentTime - (this.ctx.outputLatency || 0) - (this.ctx.baseLatency || 0);
            const { beats } = this.session;

            while (beats.length && beats[0].when <= heard) {
                const { accent } = beats.shift();

                this.beatSide = this.beatSide === "a" ? "b" : "a";
                beat.dataset.side = this.beatSide;
                beat.classList.toggle("down", accent);
            }
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
    };
