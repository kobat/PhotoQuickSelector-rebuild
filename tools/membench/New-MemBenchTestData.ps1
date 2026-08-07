<#
.SYNOPSIS
    membench 用の合成テスト画像フォルダを生成する（プール比率の効果が画像サイズのばらつきに
    依存することを検証するため）。

.DESCRIPTION
    バッファ再利用プール（PixelBufferPool）は「デコード後のピクセルバイト数（幅×高さ×4）が
    完全一致」したときだけ再利用できる。そのためプール比率の最適値はフォルダ内の画像サイズの
    ばらつきに依存する:
      - 全画像が同一サイズ（最良ケース）: 少数の buffer で全ヒット
      - 2 サイズ混在（例: 岩国_100MSDCF＝α1 と OM-1）: 両サイズを複数本ずつ抱えられる容量が必要
      - 全画像が異なるサイズ（最悪ケース）: 一度もヒットしない＝プールは死蔵在庫になる
    本スクリプトは最良/最悪ケースの合成フォルダを System.Drawing で生成する。中身はグラデーション
    ＋ランダム矩形（JPEG が数百 KB〜数 MB になる程度の絵柄。メモリ挙動を決めるのはデコード後の
    ピクセルバイト数であって絵柄ではない）。EXIF は付かない（アプリは EXIF 無し JPEG も表示可）。

.PARAMETER OutputFolder
    生成先フォルダ（無ければ作成。既存の UNIQ*.JPG / SAME*.JPG は上書き）。

.PARAMETER Mode
    unique = 全枚数の寸法を 1 枚ずつ変える（最悪ケース）／same = 全枚数を同一寸法にする（最良ケース）。

.PARAMETER Count
    生成枚数（既定 60。sweep 60 秒×2 枚/秒＝120 ナビのピンポンで全域を往復できる枚数）。

.EXAMPLE
    .\New-MemBenchTestData.ps1 -OutputFolder 'D:\Users\kobat\tmp_ClaudeCode用\membench_uniq' -Mode unique
    .\New-MemBenchTestData.ps1 -OutputFolder 'D:\Users\kobat\tmp_ClaudeCode用\membench_same' -Mode same
#>
param(
    [Parameter(Mandatory = $true)][string]$OutputFolder,
    [Parameter(Mandatory = $true)][ValidateSet('unique', 'same')][string]$Mode,
    [int]$Count = 60,
    # 基準寸法は Sony α1 の 8640x5760（デコード後 199MB）に合わせる。unique では 1 枚ごとに
    # 幅 -16 / 高さ -8 して全寸法（＝ピクセルバイト数）を相異にする（60 枚目で 7696x5288＝163MB）。
    [int]$BaseWidth = 8640,
    [int]$BaseHeight = 5760
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not (Test-Path $OutputFolder)) {
    New-Item -ItemType Directory -Path $OutputFolder | Out-Null
}

$jpegCodec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() |
    Where-Object { $_.MimeType -eq 'image/jpeg' } | Select-Object -First 1
$encParams = New-Object System.Drawing.Imaging.EncoderParameters(1)
$encParams.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter(
    [System.Drawing.Imaging.Encoder]::Quality, [long]85)

$rand = New-Object System.Random(20260808)  # 再現性のため固定シード

for ($i = 0; $i -lt $Count; $i++) {
    if ($Mode -eq 'unique') {
        $w = $BaseWidth - 16 * $i
        $h = $BaseHeight - 8 * $i
        $prefix = 'UNIQ'
    } else {
        $w = $BaseWidth
        $h = $BaseHeight
        $prefix = 'SAME'
    }

    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        # 枚ごとに色相の違うグラデーション＋ランダム矩形 40 個（真っ平らな JPEG にしない程度の絵柄）。
        $c1 = [System.Drawing.Color]::FromArgb(255, (($i * 37) % 256), (($i * 91) % 256), (($i * 151) % 256))
        # 各チャンネルの式は $i>=0 で常に非負になる形にする（PowerShell の % は負の被除数で負を返す）。
        $c2 = [System.Drawing.Color]::FromArgb(255, (($i * 203) % 256), (($i * 17) % 256), ((128 + $i * 71) % 256))
        $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
        $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, 45.0)
        try { $g.FillRectangle($brush, $rect) } finally { $brush.Dispose() }

        for ($r = 0; $r -lt 40; $r++) {
            $rc = [System.Drawing.Color]::FromArgb(120, $rand.Next(256), $rand.Next(256), $rand.Next(256))
            $rb = New-Object System.Drawing.SolidBrush($rc)
            try {
                $rw = $rand.Next(200, 2000); $rh = $rand.Next(200, 2000)
                $g.FillRectangle($rb, $rand.Next(0, $w - $rw), $rand.Next(0, $h - $rh), $rw, $rh)
            } finally { $rb.Dispose() }
        }

        $name = '{0}{1:0000}.JPG' -f $prefix, ($i + 1)
        $path = Join-Path $OutputFolder $name
        $bmp.Save($path, $jpegCodec, $encParams)
        Write-Output ("{0}  {1}x{2}  ({3:0.0}MB decoded)" -f $name, $w, $h, ($w * $h * 4 / 1MB))
    }
    finally {
        $g.Dispose()
        $bmp.Dispose()
    }
}

Write-Output ''
Write-Output "生成完了: $OutputFolder（$Count 枚・mode=$Mode）"
