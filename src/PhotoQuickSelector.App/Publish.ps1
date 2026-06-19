<#
.SYNOPSIS
  PhotoQuickSelector を unpackaged 自己完結 EXE として発行する。

.DESCRIPTION
  配布形態は SPEC §0 の (A) unpackaged（MSIX なし）・.NET / Windows App SDK 同梱。
  既定はフォルダ配布（堅実・起動が速い）。-SingleFile で単一ファイル EXE。

.PARAMETER SingleFile
  指定すると単一ファイル EXE（win-x64-singlefile プロファイル）で発行する。

.PARAMETER Runtime
  ランタイム識別子。既定 win-x64（win-x86 / win-arm64 も可）。

.EXAMPLE
  .\Publish.ps1                 # フォルダ配布（win-x64）
  .\Publish.ps1 -SingleFile     # 単一ファイル EXE（win-x64）
#>
param(
    [switch]$SingleFile,
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$proj = Join-Path $PSScriptRoot "PhotoQuickSelector.App.csproj"

# Runtime -> Platform（pubxml の Platform に対応）
$platform = switch ($Runtime) {
    "win-x64"   { "x64" }
    "win-x86"   { "x86" }
    "win-arm64" { "ARM64" }
    default     { throw "未対応の Runtime: $Runtime" }
}

if ($SingleFile) {
    if ($Runtime -ne "win-x64") { throw "単一ファイルプロファイルは現状 win-x64 のみ用意" }
    $profile = "win-x64-singlefile"
} else {
    $profile = "win-$platform"
}

Write-Host "発行: profile=$profile runtime=$Runtime" -ForegroundColor Cyan
dotnet publish $proj -c Release -p:Platform=$platform -p:PublishProfile=$profile
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

$outDir = if ($SingleFile) {
    Join-Path $PSScriptRoot "bin\Release\net10.0-windows10.0.26100.0\$Runtime\publish-singlefile"
} else {
    Join-Path $PSScriptRoot "bin\Release\net10.0-windows10.0.26100.0\$Runtime\publish"
}
Write-Host "完了: $outDir" -ForegroundColor Green
