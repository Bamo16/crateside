// What the playback modules share: constants, the plot's arithmetic, formatting and the remembered settings.

export const SNAP = 1e-6;
export const LOOKAHEAD = 0.15;
export const START_DELAY = 0.06;
export const MIN_WINDOW_SECONDS = 4;
// One zoom step, and how far the wheel must turn (in pixels of deltaY) for one.
export const ZOOM_STEP = 1.5;
export const WHEEL_PER_STEP = 100;
export const WHEEL_SETTLE_MS = 150;
// The beat grid: the fewest pixels between two lines at each density (GridLines.MinPixels in C#).
export const GRID_MIN_PIXELS = { wide: 64, medium: 36, narrow: 20 };
export const DRAG_THRESHOLD = 3;
export const SETTINGS_KEY = "crateside.playback";
// The music is leveled to this loudness (LUFS) and never raised, so the click, at full scale on top,
// stays clear of a loud master; a file with no reading is lowered by the fallback instead.
export const MUSIC_TARGET_LUFS = -18;
export const MUSIC_FALLBACK_DB = -6;

export const clamp = (value, low, high) => Math.min(high, Math.max(low, value));

// What the rows are drawn with while the window is dragged (WarpView.razor's constants and helpers).
export const COLUMNS = 832;
export const PLOT_PIXELS = 832;
export const FLAG_ROOM = 9;
export const xOf = (view, at) => ((at - view.from) / (view.to - view.from)) * 100;
export const clampX = (view, at) => clamp(xOf(view, at), 0, 100);
export const pct = (value) => String(Math.round(value * 1000) / 1000);
export const px = (pixels) => (pixels / PLOT_PIXELS) * 100;
export const escapeHtml = (text) => String(text).replace(/[&<>"]/g, (char) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[char]);


export const two = (value) => String(value).padStart(2, "0");

export function clock(at, hundredths) {
    const whole = Math.max(0, hundredths ? at : Math.floor(at));
    const minutes = Math.floor(whole / 60);

    return hundredths
        ? `${minutes}:${two(Math.floor(whole % 60))}.${two(Math.floor((whole % 1) * 100))}`
        : `${minutes}:${two(Math.floor(whole % 60))}`;
}

export function readSettings() {
    try {
        return JSON.parse(localStorage.getItem(SETTINGS_KEY) ?? "{}");
    } catch {
        return {};
    }
}

export function writeSettings(settings) {
    try {
        localStorage.setItem(SETTINGS_KEY, JSON.stringify(settings));
    } catch {
        // Private windows and blocked site data keep nothing; the page works without.
    }
}
