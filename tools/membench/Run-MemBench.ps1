<#
.SYNOPSIS
    PhotoQuickSelector の membench（Debug 限定の自動ベンチマークモード）を条件スイープ実行するハーネス。

.DESCRIPTION
    conditions.json に列挙したキャッシュ/メモリ設定の組ごとに、Debug 版アプリの設定フォルダへ
    membench.json トリガーを書き込んで `dotnet run --no-build` を起動する。アプリは
    src/PhotoQuickSelector.App/MemBench.cs（MainPage.MainPage_Loaded）が起動時に membench.json を
    消費し、指定フォルダの読み込み→メモリログ記録→sweep/idle ステップ実行→ログのコピー→自動終了、
    という一連を自分で行う（詳細は docs/HISTORY.md「メモリベンチマークモード（membench）」節）。

    settings.json はテスト実行のたびに書き換えるため、開始前にバックアップし終了時（finally）に
    必ず復元する。

.PARAMETER FolderPath
    ベンチ対象の写真フォルダ（既定はユーザーが実機でメモリ増加傾向を確認した実データフォルダ）。

.PARAMETER StartFile
    開始時に焦点を当てるファイル名（membench.json の startFile。既定 DSC03220.JPG から右送りで
    増加傾向を辿る、ユーザーの再現手順に合わせた既定値）。空文字を渡すと先頭写真から開始する。

.PARAMETER ConditionsPath
    条件一覧 JSON（既定はこのスクリプトと同じフォルダの conditions.json）。

.PARAMETER Tags
    指定時はそのタグの条件のみ実行する（省略時は conditions.json の全件）。

.EXAMPLE
    .\Run-MemBench.ps1
    .\Run-MemBench.ps1 -Tags baseline,budget1.5
#>
param(
    [string]$FolderPath = 'D:\Users\kobat\tmp_ClaudeCode用\岩国_100MSDCF',
    [string]$StartFile = 'DSC03220.JPG',
    [string]$ConditionsPath = (Join-Path $PSScriptRoot 'conditions.json'),
    [string[]]$Tags,
    # 写真送りの間隔（ms）。既定 500 ＝連打抑制の設定からの逆算値: 持続デコード上限は
    # RateBudget(3) ÷ RateWindowMs(1500) ＝ 2 枚/秒 → 間隔 500ms。加えて停止判定
    # LoadSettleDelay(150ms) を超えるため毎ナビ後に settle→先読みが走り、次のナビは
    # キャッシュ済み即表示になる＝描画スキップなし（旧既定 120ms は 150ms 未満で settle が
    # 一度も発火せず、即デコード経路がレート制限に張り付いて間引かれていた）。
    [int]$NavIntervalMs = 500
)

$ErrorActionPreference = 'Stop'

$ScriptDir = $PSScriptRoot
$RepoRoot = Split-Path -Parent (Split-Path -Parent $ScriptDir)
$CsprojDir = Join-Path $RepoRoot 'src\PhotoQuickSelector.App'
$ResultsDir = Join-Path $ScriptDir 'results'
$RunTimeoutMs = 8 * 60 * 1000  # 8分。sweep/idle 合計 175 秒＋アプリ起動/終了の猶予を大きく見込む。

if (-not (Test-Path $CsprojDir)) {
    throw "csproj ディレクトリが見つかりません: $CsprojDir"
}
if (-not (Test-Path $ResultsDir)) {
    New-Item -ItemType Directory -Path $ResultsDir | Out-Null
}

# --- Debug パッケージの設定フォルダを解決する ---
# 実体は %LOCALAPPDATA%\Packages\<PFN>\LocalCache\Local\PhotoQuickSelector.Dev\settings.json
# （AppSettings.SettingsFolder の packaged 版リダイレクト先。CLAUDE.md「packaged のパス/プロセス」節）。
function Resolve-DebugSettingsFolder {
    $manifestPath = Join-Path $CsprojDir 'Package.appxmanifest'
    $identityName = $null
    if (Test-Path $manifestPath) {
        [xml]$manifestXml = Get-Content -Path $manifestPath -Raw -Encoding UTF8
        $identityName = $manifestXml.Package.Identity.Name
    }

    $pkg = $null
    if ($identityName) {
        $pkg = Get-AppxPackage -Name $identityName -ErrorAction SilentlyContinue | Select-Object -First 1
    }
    if (-not $pkg) {
        # Identity Name は GUID 表記のことがあり Get-AppxPackage -Name が引っかからない場合のフォールバック。
        $pkg = Get-AppxPackage | Where-Object { $_.Name -like '*PhotoQuickSelector*' } | Select-Object -First 1
    }

    $pfn = $null
    if ($pkg) {
        $pfn = $pkg.PackageFamilyName
    }
    if (-not $pfn) {
        $packagesRoot = Join-Path $env:LOCALAPPDATA 'Packages'
        $candidates = Get-ChildItem -Path $packagesRoot -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -like '*PhotoQuickSelector*' -or ($identityName -and $_.Name -like "*$identityName*") }
        if ($candidates) {
            $pfn = $candidates[0].Name
        }
    }

    if (-not $pfn) {
        throw 'PhotoQuickSelector の MSIX パッケージ登録が見つかりません。先に一度 BuildAndRun.ps1（または dotnet run）で手動起動してから再実行してください。'
    }

    $settingsFolder = Join-Path (Join-Path $env:LOCALAPPDATA "Packages\$pfn\LocalCache\Local") 'PhotoQuickSelector.Dev'
    $settingsPath = Join-Path $settingsFolder 'settings.json'
    if (-not (Test-Path $settingsPath)) {
        throw "settings.json が見つかりません（$settingsPath）。先に一度 Debug 版アプリを起動して設定ファイルを作らせてから再実行してください。"
    }
    return $settingsFolder
}

Write-Output '残骸プロセスを掃除しています...'
Stop-Process -Name 'PhotoQuickSelector.App' -Force -ErrorAction SilentlyContinue

Write-Output 'Debug 設定フォルダを解決しています...'
$SettingsFolder = Resolve-DebugSettingsFolder
$SettingsPath = Join-Path $SettingsFolder 'settings.json'
$BackupPath = Join-Path $SettingsFolder 'settings.membench-backup.json'
$MemBenchJsonPath = Join-Path $SettingsFolder 'membench.json'
Write-Output "  -> $SettingsFolder"

# 既存バックアップがあれば上書きしない（前回の異常終了で settings.json が既に書き換わっている場合に
# バックアップまで壊れた状態で上書きしてしまうのを避けるため）。
if (-not (Test-Path $BackupPath)) {
    Copy-Item -Path $SettingsPath -Destination $BackupPath
    Write-Output 'settings.json をバックアップしました。'
} else {
    Write-Output '既存のバックアップを再利用します（上書きしません）。'
}

try {
    Write-Output 'dotnet build を実行しています...'
    Push-Location $CsprojDir
    try {
        dotnet build PhotoQuickSelector.App.csproj
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build に失敗しました（終了コード $LASTEXITCODE）。"
        }
    } finally {
        Pop-Location
    }

    if (-not (Test-Path $ConditionsPath)) {
        throw "条件ファイルが見つかりません: $ConditionsPath"
    }
    $allConditions = Get-Content -Path $ConditionsPath -Raw -Encoding UTF8 | ConvertFrom-Json

    $conditions = $allConditions
    if ($Tags -and $Tags.Count -gt 0) {
        $conditions = $allConditions | Where-Object { $Tags -contains $_.tag }
        if (-not $conditions) {
            throw "-Tags で指定したタグが conditions.json に見つかりません: $($Tags -join ', ')"
        }
    }

    $completedTags = @()
    $failedTags = @()

    foreach ($cond in $conditions) {
        $tag = $cond.tag
        Write-Output ''
        Write-Output "=== [$tag] 開始 ==="

        # settings.json の該当 4 項目だけ上書きする（他の設定項目は保持）。App は BOM なし UTF-8 で
        # 書く（AppSettings.Save）ため、-Encoding UTF8 を省くと PS 5.1 既定（システムコードページ）で
        # 日本語フォルダパス（RecentFolders 等）が文字化けする。
        $settingsJson = Get-Content -Path $SettingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $settingsJson.CacheBudgetGB = $cond.settings.CacheBudgetGB
        $settingsJson.CachePoolRatioPercent = $cond.settings.CachePoolRatioPercent
        $settingsJson.BlockingGcThresholdMB = $cond.settings.BlockingGcThresholdMB
        $settingsJson.HeapHardLimitGB = $cond.settings.HeapHardLimitGB
        ($settingsJson | ConvertTo-Json -Depth 20) | Set-Content -Path $SettingsPath -Encoding utf8

        $outputPath = Join-Path $ResultsDir "$tag.tsv"
        # 最初の sweep は右送り（MoveNext）開始のまま＝ユーザーの再現手順（StartFile から右連打で
        # 増加傾向が出る区間を通す）に合わせるため、ステップの並びは変えない。
        $membench = [ordered]@{
            folderPath = $FolderPath
            tag        = $tag
            startFile  = $StartFile
            outputPath = $outputPath
            settleMs   = 5000
            steps      = @(
                @{ kind = 'sweep'; durationMs = 60000; intervalMs = $NavIntervalMs },
                @{ kind = 'idle';  durationMs = 30000 },
                @{ kind = 'sweep'; durationMs = 60000; intervalMs = $NavIntervalMs },
                @{ kind = 'idle';  durationMs = 25000 }
            )
        }
        ($membench | ConvertTo-Json -Depth 10) | Set-Content -Path $MemBenchJsonPath -Encoding utf8

        if (Test-Path $outputPath) {
            Remove-Item -Path $outputPath -Force
        }

        $startedAt = Get-Date
        $proc = Start-Process -FilePath 'dotnet' -ArgumentList @('run', '--no-build') `
            -WorkingDirectory $CsprojDir -PassThru -NoNewWindow
        $finished = $proc.WaitForExit($RunTimeoutMs)
        if (-not $finished) {
            Write-Warning "[$tag] タイムアウト（$($RunTimeoutMs / 1000)秒）。プロセスを強制終了します。"
            try { $proc.Kill() } catch { }
            Start-Sleep -Seconds 2
            Stop-Process -Name 'PhotoQuickSelector.App' -Force -ErrorAction SilentlyContinue
        }
        $elapsed = (Get-Date) - $startedAt

        if (Test-Path $outputPath) {
            Write-Output ("[{0}] OK（{1:0.0}秒） -> {2}" -f $tag, $elapsed.TotalSeconds, $outputPath)
            $completedTags += $tag
        } else {
            $errorPath = Join-Path $SettingsFolder 'membench.error.txt'
            $detail = ''
            if (Test-Path $errorPath) {
                $detail = " 詳細: $errorPath"
            }
            Write-Warning ("[{0}] NG（{1:0.0}秒）: 結果 TSV が見つかりません。{2}" -f $tag, $elapsed.TotalSeconds, $detail)
            $failedTags += $tag
        }
    }

    Write-Output ''
    Write-Output '=== 実行結果 ==='
    Write-Output ("成功: {0}" -f ($(if ($completedTags.Count -gt 0) { $completedTags -join ', ' } else { '(なし)' })))
    Write-Output ("失敗: {0}" -f ($(if ($failedTags.Count -gt 0) { $failedTags -join ', ' } else { '(なし)' })))
    Write-Output "results フォルダ: $ResultsDir"
}
finally {
    if (Test-Path $BackupPath) {
        Copy-Item -Path $BackupPath -Destination $SettingsPath -Force
        Write-Output ''
        Write-Output 'settings.json を元の内容へ復元しました。'
    }
    if (Test-Path $MemBenchJsonPath) {
        # 最後の実行がタイムアウト等で membench.json を消費し切れなかった場合の後始末。
        Remove-Item -Path $MemBenchJsonPath -Force -ErrorAction SilentlyContinue
    }
}
