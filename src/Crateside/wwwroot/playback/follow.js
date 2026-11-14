// Following the playhead: Off, or Page, where the window turns a page when the playhead leaves it.

import { clamp, writeSettings } from "./shared.js";

export const Follow = (Base) =>
    class extends Base {
        // What the view does now; moving it by hand makes this "off" while the remembered choice stays.
        following = "off";

        // A pick from the header or the menu is remembered.
        setFollow(mode) {
            this.settings.follow = mode;
            writeSettings(this.settings);
            this.following = mode;
            this.paintFollow();

            if (mode !== "off")
                this.followIntoView(this.playing ? this.position() : this.parked);
        }

        // Moving the view by hand lets go of the playhead.
        releaseFollow() {
            if (this.following === "off")
                return;

            this.following = "off";
            this.paintFollow();
        }

        resumeFollow() {
            this.following = this.settings.follow;
            this.paintFollow();
        }

        paintFollow() {
            for (const button of this.view?.root.querySelectorAll("[data-follow]") ?? []) {
                const on = String(button.dataset.follow === this.following);

                button.setAttribute(button.getAttribute("role") === "menuitemradio" ? "aria-checked" : "aria-pressed", on);
            }
        }

        // Each frame of playback.
        follow(at) {
            if (!this.playing || this.following === "off" || this.drag || this.wheel || this.isWhole())
                return;

            const { start, end } = this.window;

            if (at < start - 1e-3 || at >= end)
                this.turnPage(at);
        }

        // The window, at its width, starts on the playhead's bar: a whole window on when the playhead ran off its right
        // edge, and wherever a jump (the loop's, say) put it otherwise. The rows are drawn here until the server's arrive.
        turnPage(at) {
            const snaps = this.snaps();
            const width = this.indexOf(this.window.end) - this.indexOf(this.window.start);
            const first = clamp(this.floorIndex(at), 0, snaps.length - 1 - width);

            if (snaps[first] === this.window.start)
                return;

            this.setWindow(snaps[first], snaps[first + width], { silent: true });
            this.paintLive();
            this.pushWindow();
            this.settleLive();
        }
    };
