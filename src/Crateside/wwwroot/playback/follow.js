// Following the playhead: Off, Page (the window turns a page when the playhead leaves it) or Scroll (the view moves under a still playhead).

import { clamp, writeSettings } from "./shared.js";

// The strip Scroll slides: this many views wide, beginning this many before the window.
const STRIP_VIEWS = 6;
const STRIP_LEAD = 0.5;
// While scrolling, the overview's window and the readout are redrawn this often rather than every frame.
const SCROLL_REPORT_MS = 100;

export const Follow = (Base) =>
    class extends Base {
        // What the view does now; moving it by hand makes this "off" while the remembered choice stays.
        following = "off";
        // While the view scrolls: the window's width in seconds and in bars, which it goes back to when the scroll ends.
        scrolling = null;

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
            if (this.canScroll())
                return this.scroll(at);

            this.settleScroll();

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

            this.showWindow(snaps[first], snaps[first + width]);
        }

        // A window the page chose itself: drawn here at once, and the server told.
        showWindow(start, end) {
            this.setWindow(start, end, { silent: true });
            this.liveShown = null;
            this.paintLive();
            this.pushWindow();
            this.settleLive();
        }

        // Scroll needs playback, a zoomed view and nothing else moving the window; an open judging form reads the ruler the server drew.
        canScroll() {
            return (
                this.following === "scroll" &&
                this.playing &&
                !this.drag &&
                (!this.wheel || this.wheel.scrolls) &&
                !this.judging &&
                Boolean(this.live && this.view && this.els.field) &&
                this.duration > 0 &&
                this.window.end - this.window.start < this.duration - 0.001
            );
        }

        // The window keeps its width and moves so the playhead stays at its centre; near the track's start and end,
        // and until it first reaches the centre, the playhead moves instead. Only the strip slides: nothing is redrawn
        // until it runs out, and the server is not told until the scroll ends.
        scroll(at) {
            const width = this.window.end - this.window.start;

            if (!this.scrolling || Math.abs(width - this.scrolling.span) > 1e-6)
                this.scrolling = { span: width, bars: this.indexOf(this.window.end) - this.indexOf(this.window.start), reported: 0 };

            const { span } = this.scrolling;
            let start = this.window.start;

            if (at < start - 1e-3 || at > start + span / 2)
                start = at - span / 2;

            start = clamp(start, 0, this.duration - span);

            const { strip } = this;

            if (!strip || Math.abs((strip.to - strip.from) / strip.wide - span) > 1e-6 || start < strip.from - 1e-6 || start + span > strip.to + 1e-6) {
                const from = Math.max(0, Math.min(start - span * STRIP_LEAD, this.duration - span * STRIP_VIEWS));
                const to = Math.min(this.duration, from + span * STRIP_VIEWS);

                this.paintStrip(from, to, (to - from) / span);
                this.scrolling.reported = 0;
            }

            this.window = { start, end: start + span };
            this.slideStrip(start);

            const now = performance.now();

            if (now - this.scrolling.reported >= SCROLL_REPORT_MS) {
                this.scrolling.reported = now;
                this.paintWindow();
            }
        }

        // The scroll ends (playback stopped, follow changed, a hand on the window): the window goes to the nearest bars.
        settleScroll() {
            if (!this.scrolling)
                return;

            const { span } = this.scrolling;
            const { start, end } = this.window;
            // A zoom the scroll never drew leaves its bar count behind.
            const bars = Math.abs(end - start - span) > 1e-6 ? Math.max(1, this.indexOf(end) - this.indexOf(start)) : this.scrolling.bars;
            const snaps = this.snaps();
            const first = clamp(this.indexOf(this.window.start), 0, snaps.length - 1 - bars);

            this.scrolling = null;
            this.showWindow(snaps[first], snaps[first + bars]);
        }
    };
