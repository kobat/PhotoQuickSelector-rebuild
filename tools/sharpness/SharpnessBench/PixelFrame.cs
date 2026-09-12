namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// <see cref="WicPixelDecoder"/> が返すデコード結果（正立済み BGRA8・密詰め）。
/// App 本体の <c>PreviewBitmapCache.cs</c> にある同名クラスのミラー。
/// 本ツールは Win2D/WinUI に依存しないため、そちらをリンクできず（<c>PreviewBitmapCache.cs</c> は
/// WinUI 依存）、この最小定義だけをここに複製している。フィールド構成は必ず一致させること。
/// </summary>
internal sealed class PixelFrame
{
    public PixelFrame(byte[] bytes, int width, int height)
    {
        Bytes = bytes;
        Width = width;
        Height = height;
    }

    public byte[] Bytes { get; }
    public int Width { get; }
    public int Height { get; }
}
