# Generates docs/SHORTCUTS.md (Japanese) and docs/SHORTCUTS.en.md (English) from the
# single source of truth: shortcuts.json (repo root).
#
# shortcuts.json schema: each localizable text is either a plain string (same in all
# languages) or an object like {"ja": "...", "en": "..."}.
#
# ASCII-only by design: this script contains NO Japanese literals, because Windows
# PowerShell 5.1 misreads UTF-8 (no BOM) script files as ANSI and fails to parse them.
# All human-facing text (title, labels, group names, key/description) is read
# from shortcuts.json at runtime as UTF-8, which is safe.
#
# Usage:  powershell -ExecutionPolicy Bypass -File tools\gen-shortcuts.ps1
# Run this whenever you edit shortcuts.json so the generated docs stay in sync.

$ErrorActionPreference = 'Stop'

$root    = Split-Path -Parent $PSScriptRoot
$srcPath = Join-Path $root 'shortcuts.json'
$outDir  = Join-Path $root 'docs'

if (-not (Test-Path $srcPath)) { throw "Source not found: $srcPath" }

# -Encoding UTF8 forces UTF-8 decoding (default Get-Content would mangle Japanese as ANSI).
$data = Get-Content -Raw -Encoding UTF8 $srcPath | ConvertFrom-Json

# Picks a localized value: plain string -> as is; object -> $lang, then 'ja', else ''.
function Pick($val, $lang) {
    if ($null -eq $val) { return '' }
    if ($val -is [string]) { return $val }
    $p = $val.PSObject.Properties[$lang]
    if ($null -ne $p -and $p.Value) { return [string]$p.Value }
    $pj = $val.PSObject.Properties['ja']
    if ($null -ne $pj -and $pj.Value) { return [string]$pj.Value }
    return ''
}

if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

# Japanese keeps the historical file name (README links to docs/SHORTCUTS.md).
$outputs = @(
    @{ Lang = 'ja'; File = 'SHORTCUTS.md' },
    @{ Lang = 'en'; File = 'SHORTCUTS.en.md' }
)

foreach ($out in $outputs) {
    $lang      = $out.Lang
    $outPath   = Join-Path $outDir $out.File
    $keysLabel = Pick $data.keysLabel $lang
    $descLabel = Pick $data.descriptionLabel $lang

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("# $(Pick $data.title $lang)")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('<!-- This file is generated from shortcuts.json by tools/gen-shortcuts.ps1. Do not edit directly. -->')
    [void]$sb.AppendLine()

    foreach ($group in $data.groups) {
        [void]$sb.AppendLine("## $(Pick $group.title $lang)")
        [void]$sb.AppendLine()
        [void]$sb.AppendLine("| $keysLabel | $descLabel |")
        [void]$sb.AppendLine('|---|---|')
        foreach ($item in $group.items) {
            # Escape any pipe in cell text so it doesn't break the Markdown table.
            $keys = ((Pick $item.keys $lang)        -replace '\|', '\|')
            $desc = ((Pick $item.description $lang) -replace '\|', '\|')
            [void]$sb.AppendLine("| ``$keys`` | $desc |")
        }
        [void]$sb.AppendLine()
    }

    [System.IO.File]::WriteAllText($outPath, $sb.ToString(), $utf8NoBom)
    Write-Host "Generated: $outPath"
}
