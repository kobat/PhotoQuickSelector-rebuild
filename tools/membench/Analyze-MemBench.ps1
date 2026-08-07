<#
.SYNOPSIS
    tools/membench/results/*.tsv（MemoryLog の出力。Run-MemBench.ps1 が集めたもの）を集計し、
    条件ごとの比較表を results/summary.md へ Markdown で出力する。

.DESCRIPTION
    TSV スキーマは docs/HISTORY.md「メモリ時系列ログ」節の通り（本スクリプト冒頭のコメントにも列順を転記）。
    1 ファイル＝1 条件（Run-MemBench.ps1 が membench.json の tag をファイル名にする）。
    条件（budget/pool/thr/hhl）は NOTE bench 行（MainPage.RunMemBenchAsync が書く
    "tag=... budget=... pool=... thr=... hhl=..." 形式）から読み取る。

.PARAMETER ResultsDir
    集計対象の tsv があるフォルダ（既定はこのスクリプトと同じフォルダの results）。

.EXAMPLE
    .\Analyze-MemBench.ps1
#>
param(
    [string]$ResultsDir = (Join-Path $PSScriptRoot 'results')
)

$ErrorActionPreference = 'Stop'

# SAMPLE 列順（0始まり。0=elapsedMs, 1="SAMPLE"）:
#   2 managed, 3 committed, 4 private, 5 ws, 6 native, 7 gen0, 8 gen1, 9 gen2,
#   10 lohSize, 11 lohFrag, 12 cacheBytes, 13 cacheCount, 14 inflightCount,
#   15 poolBytes, 16 poolCount, 17 poolHit, 18 poolMiss, 19 discardedBytes,
#   20 janitorDirty, 21 allocatedBytes, 22 pohSize
$ColWs = 5
$ColPoolHit = 17
$ColPoolMiss = 18

# カクつき判定: 250ms 周期のはずが伸びた（UI スレッド駆動なので伸び＝UI 停止）とみなす閾値。
# 実測では通常時のギャップは 300ms 未満・~250ms の GC 停止でも 324ms 程度（タイマー位相で吸収される
# ため停止時間がそのまま上乗せされるわけではない）。400 だと 200ms 級の停止を取りこぼすので 350。
$StutterGapThresholdMs = 350

function ConvertTo-MiB([double]$bytes) {
    return [Math]::Round($bytes / 1MB, 1)
}

function Parse-BenchDetail([string]$detail) {
    # "tag=baseline budget=2.5 pool=20 thr=512 hhl=3.5" 形式を key/value に分解する。
    $map = @{}
    foreach ($token in ($detail -split '\s+')) {
        if ([string]::IsNullOrWhiteSpace($token)) { continue }
        $kv = $token -split '=', 2
        if ($kv.Count -eq 2) {
            $map[$kv[0]] = $kv[1]
        }
    }
    return $map
}

function Analyze-MemBenchFile([System.IO.FileInfo]$file) {
    $samples = New-Object System.Collections.Generic.List[Object]
    $notes = New-Object System.Collections.Generic.List[Object]
    $gcRows = New-Object System.Collections.Generic.List[Object]
    $decodeRows = New-Object System.Collections.Generic.List[Object]
    $discardTotalBytes = 0.0

    # MemoryLog は BOM なし UTF-8 で書く。既定（システムコードページ）読みだと非 ASCII のファイル名等が
    # 文字化けするため -Encoding UTF8 を明示する。
    foreach ($line in (Get-Content -Path $file.FullName -Encoding UTF8)) {
        if ($line.Length -eq 0 -or $line[0] -eq '#') { continue }
        $cols = $line -split "`t"
        if ($cols.Count -lt 2) { continue }
        $recordType = $cols[1]

        switch ($recordType) {
            'SAMPLE' {
                if ($cols.Count -le $ColPoolMiss) { continue }
                $samples.Add([PSCustomObject]@{
                    ElapsedMs = [long]$cols[0]
                    Ws        = [double]$cols[$ColWs]
                    PoolHit   = [double]$cols[$ColPoolHit]
                    PoolMiss  = [double]$cols[$ColPoolMiss]
                })
            }
            'NOTE' {
                $kind = $cols[2]
                $detail = ''
                if ($cols.Count -gt 3) { $detail = $cols[3] }
                $notes.Add([PSCustomObject]@{
                    ElapsedMs = [long]$cols[0]
                    Kind      = $kind
                    Detail    = $detail
                })
            }
            'GC' {
                if ($cols.Count -lt 4) { continue }
                $gcRows.Add([PSCustomObject]@{
                    ElapsedMs   = [long]$cols[0]
                    Kind        = $cols[2]
                    GcElapsedMs = [double]$cols[3]
                })
            }
            'DECODE' {
                if ($cols.Count -lt 5) { continue }
                $decodeRows.Add([PSCustomObject]@{
                    ElapsedMs       = [long]$cols[0]
                    DecodeElapsedMs = [double]$cols[4]
                })
            }
            'DISCARD' {
                if ($cols.Count -lt 3) { continue }
                $discardTotalBytes += [double]$cols[2]
            }
        }
    }

    if ($samples.Count -eq 0) {
        Write-Warning "$($file.Name): SAMPLE 行が見つかりません。スキップします。"
        return $null
    }

    # 既にファイル出現順＝時系列順（SAMPLE は 250ms 周期タイマーが単一スレッドで書き出す）。
    # PS 5.1 の Sort-Object は同一キーのタイ・ブレークが不安定（挿入順を保証しない）なため
    # 並べ替えは行わない（下の $stepNotes も同じ理由）。
    $sortedSamples = $samples

    # --- 条件（tag/budget/pool/thr/hhl）。NOTE bench 行が無い旧ログはファイル名だけで代替する。 ---
    $benchNote = $notes | Where-Object { $_.Kind -eq 'bench' } | Select-Object -First 1
    $tag = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
    $budget = ''; $pool = ''; $thr = ''; $hhl = ''
    if ($benchNote) {
        $map = Parse-BenchDetail $benchNote.Detail
        if ($map.ContainsKey('tag')) { $tag = $map['tag'] }
        if ($map.ContainsKey('budget')) { $budget = $map['budget'] }
        if ($map.ContainsKey('pool')) { $pool = $map['pool'] }
        if ($map.ContainsKey('thr')) { $thr = $map['thr'] }
        if ($map.ContainsKey('hhl')) { $hhl = $map['hhl'] }
    }

    # --- WS ピーク／終了時の床 ---
    $wsPeakBytes = ($sortedSamples | Measure-Object -Property Ws -Maximum).Maximum
    $wsFloorBytes = ($sortedSamples | Select-Object -Last 1).Ws

    # --- sweep 区間中の WS 平均（NOTE bench-step の sweep 区間の SAMPLE のみ） ---
    # bench-step/bench-end は RunMemBenchAsync の単一スレッド await チェーンから逐次発生するため、
    # ファイル出現順のままで区間境界として使える（$stepNotes[i+1] を「次の境界」として扱う）。
    # @() で強制配列化（該当が 1 件だけだとスカラーになり .Count/添字アクセスが壊れるため）。
    $stepNotes = @($notes | Where-Object { $_.Kind -eq 'bench-step' -or $_.Kind -eq 'bench-end' })
    $lastElapsedMs = ($sortedSamples | Select-Object -Last 1).ElapsedMs
    $sweepWsValues = New-Object System.Collections.Generic.List[double]
    for ($i = 0; $i -lt $stepNotes.Count; $i++) {
        $n = $stepNotes[$i]
        if ($n.Kind -ne 'bench-step') { continue }
        if (-not $n.Detail.StartsWith('sweep')) { continue }
        $rangeStart = $n.ElapsedMs
        $rangeEnd = $lastElapsedMs
        if ($i + 1 -lt $stepNotes.Count) { $rangeEnd = $stepNotes[$i + 1].ElapsedMs }
        foreach ($s in $sortedSamples) {
            if ($s.ElapsedMs -ge $rangeStart -and $s.ElapsedMs -lt $rangeEnd) {
                $sweepWsValues.Add($s.Ws)
            }
        }
    }
    $sweepWsAvgBytes = 0.0
    if ($sweepWsValues.Count -gt 0) {
        $sweepWsAvgBytes = ($sweepWsValues | Measure-Object -Average).Average
    }

    # --- カクつき（SAMPLE 間の経過ms差が閾値超え） ---
    $stutterCount = 0
    $stutterMaxGapMs = 0
    for ($i = 1; $i -lt $sortedSamples.Count; $i++) {
        $gap = $sortedSamples[$i].ElapsedMs - $sortedSamples[$i - 1].ElapsedMs
        if ($gap -gt $StutterGapThresholdMs) {
            $stutterCount++
            if ($gap -gt $stutterMaxGapMs) { $stutterMaxGapMs = $gap }
        }
    }

    # --- GC 行の kind 別集計 ---
    $gcSummaryParts = New-Object System.Collections.Generic.List[string]
    $gcGroups = $gcRows | Group-Object Kind
    foreach ($g in $gcGroups) {
        $maxMs = ($g.Group | Measure-Object -Property GcElapsedMs -Maximum).Maximum
        $sumMs = ($g.Group | Measure-Object -Property GcElapsedMs -Sum).Sum
        $gcSummaryParts.Add(("{0}:{1}回/max{2:0}ms/計{3:0}ms" -f $g.Name, $g.Count, $maxMs, $sumMs))
    }
    $gcSummary = '(なし)'
    if ($gcSummaryParts.Count -gt 0) { $gcSummary = $gcSummaryParts -join '; ' }

    # --- DECODE 件数・平均 ---
    $decodeCount = $decodeRows.Count
    $decodeAvgMs = 0.0
    if ($decodeCount -gt 0) {
        $decodeAvgMs = ($decodeRows | Measure-Object -Property DecodeElapsedMs -Average).Average
    }

    # --- プールヒット率（先頭/末尾サンプルの累積 hit/miss の差分から算出） ---
    $first = $sortedSamples | Select-Object -First 1
    $last = $sortedSamples | Select-Object -Last 1
    $dHit = $last.PoolHit - $first.PoolHit
    $dMiss = $last.PoolMiss - $first.PoolMiss
    $poolHitRatePercent = $null
    if (($dHit + $dMiss) -gt 0) {
        $poolHitRatePercent = [Math]::Round(100.0 * $dHit / ($dHit + $dMiss), 1)
    }

    return [PSCustomObject]@{
        Tag             = $tag
        Budget          = $budget
        Pool            = $pool
        Thr             = $thr
        Hhl             = $hhl
        WsPeakMB        = ConvertTo-MiB $wsPeakBytes
        SweepWsAvgMB    = ConvertTo-MiB $sweepWsAvgBytes
        WsFloorMB       = ConvertTo-MiB $wsFloorBytes
        StutterCount    = $stutterCount
        StutterMaxGapMs = $stutterMaxGapMs
        GcSummary       = $gcSummary
        DecodeCount     = $decodeCount
        DecodeAvgMs     = [Math]::Round($decodeAvgMs, 1)
        PoolHitRate     = $poolHitRatePercent
        DiscardTotalMB  = ConvertTo-MiB $discardTotalBytes
    }
}

if (-not (Test-Path $ResultsDir)) {
    throw "results フォルダが見つかりません: $ResultsDir"
}

$files = Get-ChildItem -Path $ResultsDir -Filter '*.tsv' | Sort-Object Name
if ($files.Count -eq 0) {
    Write-Warning "$ResultsDir に .tsv が見つかりません。先に Run-MemBench.ps1 を実行してください。"
    return
}

$rows = New-Object System.Collections.Generic.List[Object]
foreach ($file in $files) {
    $row = Analyze-MemBenchFile $file
    if ($row) { $rows.Add($row) }
}

if ($rows.Count -eq 0) {
    Write-Warning '集計できる行がありませんでした。'
    return
}

$rows | Format-Table -AutoSize | Out-String -Width 4096 | Write-Output

$mdLines = New-Object System.Collections.Generic.List[string]
$mdLines.Add('# membench 結果サマリ')
$mdLines.Add('')
$mdLines.Add(("生成日時: {0}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
$mdLines.Add('')
$mdLines.Add('| tag | budget(GB) | pool(%) | thr(MB) | hhl(GB) | WSピーク(MB) | sweep中WS平均(MB) | 終了時の床(MB) | カクつき回数 | カクつき最大(ms) | GC内訳 | DECODE件数 | DECODE平均(ms) | プールヒット率(%) | DISCARD合計(MB) |')
$mdLines.Add('|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|')
foreach ($r in $rows) {
    $poolHitText = 'N/A'
    if ($null -ne $r.PoolHitRate) { $poolHitText = $r.PoolHitRate }
    $mdLines.Add("| $($r.Tag) | $($r.Budget) | $($r.Pool) | $($r.Thr) | $($r.Hhl) | $($r.WsPeakMB) | $($r.SweepWsAvgMB) | $($r.WsFloorMB) | $($r.StutterCount) | $($r.StutterMaxGapMs) | $($r.GcSummary) | $($r.DecodeCount) | $($r.DecodeAvgMs) | $poolHitText | $($r.DiscardTotalMB) |")
}

$summaryPath = Join-Path $ResultsDir 'summary.md'
($mdLines -join "`r`n") | Set-Content -Path $summaryPath -Encoding utf8
Write-Output ''
Write-Output "summary.md を書き出しました: $summaryPath"
