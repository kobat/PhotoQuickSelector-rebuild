# Generates docs/SHORTCUTS.md from the single source of truth: shortcuts.json (repo root).
#
# ASCII-only by design: this script contains NO Japanese literals, because Windows
# PowerShell 5.1 misreads UTF-8 (no BOM) script files as ANSI and fails to parse them.
# All human-facing Japanese text (title, labels, group names, key/description) is read
# from shortcuts.json at runtime as UTF-8, which is safe.
#
# Usage:  powershell -ExecutionPolicy Bypass -File tools\gen-shortcuts.ps1
# Run this whenever you edit shortcuts.json so docs/SHORTCUTS.md stays in sync.

$ErrorActionPreference = 'Stop'

$root    = Split-Path -Parent $PSScriptRoot
$srcPath = Join-Path $root 'shortcuts.json'
$outDir  = Join-Path $root 'docs'
$outPath = Join-Path $outDir 'SHORTCUTS.md'

if (-not (Test-Path $srcPath)) { throw "Source not found: $srcPath" }

# -Encoding UTF8 forces UTF-8 decoding (default Get-Content would mangle Japanese as ANSI).
$data = Get-Content -Raw -Encoding UTF8 $srcPath | ConvertFrom-Json

$keysLabel = $data.keysLabel
$descLabel = $data.descriptionLabel

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("# $($data.title)")
[void]$sb.AppendLine()
[void]$sb.AppendLine('<!-- This file is generated from shortcuts.json by tools/gen-shortcuts.ps1. Do not edit directly. -->')
[void]$sb.AppendLine()

foreach ($group in $data.groups) {
    [void]$sb.AppendLine("## $($group.title)")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("| $keysLabel | $descLabel |")
    [void]$sb.AppendLine('|---|---|')
    foreach ($item in $group.items) {
        # Escape any pipe in cell text so it doesn't break the Markdown table.
        $keys = ($item.keys        -replace '\|', '\|')
        $desc = ($item.description -replace '\|', '\|')
        [void]$sb.AppendLine("| ``$keys`` | $desc |")
    }
    [void]$sb.AppendLine()
}

if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

# Write UTF-8 without BOM (preferred for Markdown on GitHub).
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($outPath, $sb.ToString(), $utf8NoBom)

Write-Host "Generated: $outPath"
