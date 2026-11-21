# Handoff: crateside brand (option 1a)

## What this is
The interim brand: a **mark** (three crate slats seen edge-on, the middle one pulled forward in amber) and a **wordmark** (the word `crateside` in the app's own IBM Plex Sans 600). Chosen to keep things simple; other directions live in the design session and may replace it later, so **route every use through one component and one asset folder** so a swap is a file change.

## Assets (put in `src/Crateside/wwwroot/brand/`)
- `mark.svg` — the mark alone, transparent, 24×24 viewBox. Use in the header.
- `mark-tile.svg` — mark on a rounded `#23211a` tile. App-icon / OG use.
- `lockup.svg` — mark + wordmark, 236×40. README/GitHub only (text is live SVG text; the header should NOT use this — it renders the word in HTML so it stays selectable and on-token).
- `favicon.svg` — 16×16 viewBox, bars thickened so three lines survive at 16px. **This is the browser-tab icon.**
- `favicon-16.png`, `favicon-32.png` — raster fallbacks; `favicon-180.png` — apple-touch-icon; `favicon-512.png` — PWA/OG.

## Header (all pages share the header component)
Replace the bare word in the brand cell with mark + word, one flex row:
- `display:flex; align-items:center; gap:8px`
- mark: `<img src="brand/mark.svg" width="18" height="18" alt="">` (decorative; the word is the label)
- word: unchanged — `crateside`, 15px/600, letter-spacing .02em, `--text`
- the whole row is the link to `/`
Do not recolor the mark per page or state. Amber is fixed brand amber `#e8b45a` here even though amber means "your turn" elsewhere — the header is where you look when it is your turn.

## Browser tab
In `App.razor` `<head>`:
```html
<link rel="icon" href="brand/favicon.svg" type="image/svg+xml">
<link rel="icon" href="brand/favicon-32.png" sizes="32x32" type="image/png">
<link rel="apple-touch-icon" href="brand/favicon-180.png">
<meta name="theme-color" content="#1b1a15">
```
Tab title stays `crateside` (lowercase) — or `{page} · crateside` per page.

## Rules
- Casing: always lowercase `crateside` in product UI.
- Colors: ink `#ece7dc`, amber `#e8b45a`, tile `#23211a`. No other colors in the mark; no gradients; no outlines.
- Minimum size: 16px (favicon variant) / 18px (mark.svg). Below that, use the favicon variant.
- Clear space around the mark: half its height.
- README placeholder `docs/brand/crateside-lockup.svg` → point it at `lockup.svg` (or copy the file there).
