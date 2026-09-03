# Fonts

crateside uses **IBM Plex Sans** for text and **IBM Plex Mono** for every number, key, timestamp
and count — those are read as values and compared down a column, which proportional digits defeat.

Both are **self-hosted**, in `src/Crateside/wwwroot/fonts/`, declared by
`src/Crateside/wwwroot/fonts.css`. Self-hosted rather than linked from Google Fonts because the
bench is a local tool that should not need the internet to render correctly, and because a
third-party request on every page load buys nothing here.

**Latin and latin-ext only.** Google serves six subsets; the other four are Cyrillic, Greek and
Vietnamese. Twelve files, about 309 KB. Note that IBM Plex has no CJK coverage at all, so a track
like `電音部, Moe Shop - Eat Sleep Dance` falls back to the system font for its Japanese — that is
expected and not worth solving, since the fallback renders it correctly.

## Regenerating

Run from the repository root. It refetches the CSS, keeps the two Latin subsets, downloads each
face, and rewrites `fonts.css` to point at the local copies.

```powershell
$ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36"
$url = "https://fonts.googleapis.com/css2?family=IBM+Plex+Mono:wght@400;500;600&family=IBM+Plex+Sans:wght@400;500;600&display=swap"
$css = (Invoke-WebRequest -Uri $url -UserAgent $ua -UseBasicParsing).Content
$dir = "src\Crateside\wwwroot\fonts"
New-Item -ItemType Directory -Force -Path $dir | Out-Null

$rx = [regex]'/\*\s*(?<subset>[a-z0-9-]+)\s*\*/\s*@font-face\s*\{(?<body>[^}]*)\}'
$out = New-Object System.Collections.Generic.List[string]
foreach ($m in $rx.Matches($css)) {
  $subset = $m.Groups['subset'].Value
  if ($subset -notin 'latin', 'latin-ext') { continue }
  $body = $m.Groups['body'].Value
  $family = [regex]::Match($body, "font-family:\s*'([^']+)'").Groups[1].Value
  $weight = [regex]::Match($body, 'font-weight:\s*(\d+)').Groups[1].Value
  $src = [regex]::Match($body, 'url\((https://[^)]+\.woff2)\)').Groups[1].Value
  $range = [regex]::Match($body, 'unicode-range:\s*([^;]+);').Groups[1].Value.Trim()
  $file = "$(($family -replace ' ', '-').ToLower())-$weight-$subset.woff2"
  Invoke-WebRequest -Uri $src -OutFile (Join-Path $dir $file) -UseBasicParsing
  $out.Add("@font-face {`n    font-family: '$family';`n    font-style: normal;`n    font-weight: $weight;`n    font-display: swap;`n    src: url('fonts/$file') format('woff2');`n    unicode-range: $range;`n}")
}
Set-Content -Path "src\Crateside\wwwroot\fonts.css" -Value (($out -join "`n`n") + "`n") -Encoding utf8NoBOM
```

**The user-agent matters.** Google Fonts serves `woff2` only to a browser it recognises; with
PowerShell's default agent it returns TTF, which is several times the size for no benefit. Put the
header comment back on `fonts.css` afterwards — the script above writes only the `@font-face`
blocks.

## Weights

400, 500 and 600, for both families. The design uses no others: 600 for the brand and for BPM,
500 for row text and labels, 400 for prose. Adding a weight means adding four files, so it is
worth being deliberate about.
