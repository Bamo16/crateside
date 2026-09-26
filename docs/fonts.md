# Fonts

IBM Plex Sans and IBM Plex Mono, self-hosted in `src/Crateside/wwwroot/fonts/` and declared by
`src/Crateside/wwwroot/fonts.css`. Which faces, subsets and weights, and why, is
[ADR 0036](adr/0036-fonts-are-self-hosted-plex-latin-subsets-only.md). This file is how to rebuild
them.

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
