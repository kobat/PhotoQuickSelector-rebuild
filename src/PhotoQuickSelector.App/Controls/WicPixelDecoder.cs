using System;
using System.Runtime.InteropServices;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// JPEG バイト列を WIC の COM 直接呼び出しでデコードし、正立済み（EXIF Orientation 適用）の
/// BGRA8（Premultiplied・密詰め＝stride は幅×4）ピクセルを返す。
/// <para>
/// WinRT の <c>BitmapDecoder</c>/<c>InMemoryRandomAccessStream</c> を使わないのは、これらの RCW が
/// デコード 1 回ごとに ~20MB のネイティブ（ストリームバッファ＋デコーダ内部）をファイナライザ実行まで
/// 抱え続け、連続ナビで GB 級に滞留するため（GC の起動側では抑えられないことを実機で確認済み。
/// 経緯は HISTORY.md「ネイティブメモリ肥大」各節）。ここでは全 COM オブジェクトを
/// <see cref="Marshal.FinalReleaseComObject"/> で確定解放するため、デコード後にネイティブは残らない
/// （GC・ファイナライザ非依存）。
/// </para>
/// <para>
/// 圧縮バイトはピン止めした managed 配列を <c>IWICStream.InitializeFromMemory</c> に直接参照させる
/// （ネイティブ側へのコピー自体が無い）。同期 API なのでワーカースレッドから呼ぶこと
/// （UI スレッドで呼ぶとデコード時間ぶんブロックする）。
/// </para>
/// <para>
/// COM interop は必要メソッドだけ正確に宣言し、呼ばないスロットは引数なしのプレースホルダで
/// 埋めてある（vtable の位置さえ合っていれば呼ばない限り無害）。vtable 順序・GUID・enum 値は
/// Windows SDK 10.0.26100.0 の wincodec.h と突き合わせて確認済み。
/// </para>
/// </summary>
internal static class WicPixelDecoder
{
    /// <summary>
    /// <paramref name="jpegBytes"/> をデコードして <see cref="PixelFrame"/> を返す。
    /// ヘッダ宣言寸法（Orientation 適用後）×4 バイトが <paramref name="maxPixelBytes"/> を超える場合は
    /// 確保前に null を返す（解凍爆弾対策）。デコード失敗（非画像・破損）は COMException を投げる
    /// （呼び出し側の包括 catch で null 扱いにする）。
    /// </summary>
    /// <param name="jpegBytes">圧縮画像の全バイト。</param>
    /// <param name="maxPixelBytes">デコード後ピクセルの上限バイト数。</param>
    /// <param name="rentBuffer">必要バイト長（幅×高さ×4 ちょうど）の byte[] を返す確保関数（プールの Rent）。</param>
    public static PixelFrame? Decode(byte[] jpegBytes, long maxPixelBytes, Func<int, byte[]> rentBuffer)
    {
        IWICImagingFactory? factory = null;
        IWICStream? stream = null;
        IWICBitmapDecoder? decoder = null;
        IWICBitmapFrameDecode? frame = null;
        IWICMetadataQueryReader? meta = null;
        IWICColorContext[]? srcContexts = null;
        IWICColorContext? dstContext = null;
        IWICColorTransform? transform = null;
        IWICBitmapSource? cached = null;
        IWICBitmapFlipRotator? rotator = null;
        IWICFormatConverter? converter = null;

        var pin = GCHandle.Alloc(jpegBytes, GCHandleType.Pinned);
        try
        {
            // 生成はスレッド不問（呼び出しワーカーの MTA でよい）。共有キャッシュにしないのは
            // スレッド間共有の安全性を WIC が保証しないため（生成コストはデコードに対し無視できる）。
            factory = (IWICImagingFactory)Activator.CreateInstance(
                Type.GetTypeFromCLSID(ClsidWicImagingFactory, throwOnError: true)!)!;

            HR(factory.CreateStream(out stream));
            HR(stream.InitializeFromMemory(pin.AddrOfPinnedObject(), (uint)jpegBytes.Length));
            HR(factory.CreateDecoderFromStream(stream, IntPtr.Zero, WicDecodeMetadataCacheOnDemand, out decoder));
            HR(decoder.GetFrame(0, out frame));
            HR(frame.GetSize(out uint rawW, out uint rawH));

            // Orientation（274）と EXIF ColorSpace（40961）。タグ無し・読取失敗は既定値
            //（正立扱い・色管理あり＝安全側）に落とす。クエリ文字列は WinRT 時代と同じ app1 経路。
            int orientation = 1;
            bool srgb = false;
            if (frame.GetMetadataQueryReader(out meta) >= 0 && meta != null)
            {
                int o = ReadUShort(meta, "/app1/ifd/{ushort=274}", 1);
                if (o is >= 1 and <= 8) orientation = o;
                srgb = ReadUShort(meta, "/app1/ifd/exif/{ushort=40961}", 0) == 1;
            }

            // 解凍爆弾ガード: 確保（CreateBitmapFromSource／Rent）の前に宣言寸法で弾く。
            bool swap = orientation >= 5;
            long orientedW = swap ? rawH : rawW, orientedH = swap ? rawW : rawH;
            if (orientedW * orientedH * 4 > maxPixelBytes) return null;

            var source = (IWICBitmapSource)frame;

            // sRGB（EXIF ColorSpace=1）は色変換をスキップ（sRGB→sRGB でもデコード全体の約7割を占める）。
            // それ以外は埋め込みプロファイル（無ければ WIC が EXIF 由来のコンテキストを合成して返す）
            // から sRGB への変換を試み、初期化に失敗したら変換なしで続行（従来の安全側挙動と同等）。
            if (!srgb)
            {
                if (frame.GetColorContexts(0, null, out uint contextCount) >= 0 && contextCount > 0)
                {
                    srcContexts = new IWICColorContext[contextCount];
                    for (int i = 0; i < contextCount; i++)
                    {
                        HR(factory.CreateColorContext(out var ctx));
                        srcContexts[i] = ctx;
                    }
                    HR(frame.GetColorContexts(contextCount, srcContexts, out _));
                    HR(factory.CreateColorContext(out dstContext));
                    HR(dstContext.InitializeFromExifColorSpace(1)); // 1 = sRGB
                    HR(factory.CreateColorTransformer(out transform));
                    HR(frame.GetPixelFormat(out var frameFormat));
                    if (transform.Initialize(source, srcContexts[0], dstContext, in frameFormat) >= 0)
                        source = (IWICBitmapSource)transform;
                }
            }

            // 回転が要る場合はいったん WIC ビットマップ（ネイティブ・確定解放）へ全デコードしてから回す。
            // JPEG デコーダは上から順にしか行を出せず、FlipRotator が直接ぶら下がると行の要求順が乱れて
            // 帯ごとの再デコードが起きるため（WIC の既知の落とし穴）、ランダムアクセス可能な中間を挟む。
            if (orientation != 1)
            {
                HR(factory.CreateBitmapFromSource(source, WicBitmapCacheOnLoad, out cached));
                HR(factory.CreateBitmapFlipRotator(out rotator));
                HR(rotator.Initialize(cached, TransformFor(orientation)));
                source = (IWICBitmapSource)rotator;
            }

            HR(factory.CreateFormatConverter(out converter));
            HR(converter.Initialize(source, in PixelFormat32bppPBGRA,
                WicBitmapDitherTypeNone, IntPtr.Zero, 0.0, WicBitmapPaletteTypeCustom));

            var converterSource = (IWICBitmapSource)converter;
            HR(converterSource.GetSize(out uint outW, out uint outH));
            long need = (long)outW * outH * 4;
            if (need > maxPixelBytes || need > int.MaxValue) return null;

            int width = (int)outW, height = (int)outH;
            var buffer = rentBuffer((int)need);
            // 出力バッファは自前でピン止めして生ポインタで渡す。CopyPixels を [Out] byte[] 宣言で
            // 呼ぶとマーシャリングでアクセス違反になる（実測＝プロセスごと落ちる）ため必ずこの形で。
            var outPin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                HR(converterSource.CopyPixels(IntPtr.Zero, (uint)(width * 4), (uint)need, outPin.AddrOfPinnedObject()));
            }
            finally
            {
                outPin.Free();
            }
            return new PixelFrame(buffer, width, height);
        }
        finally
        {
            // 逆順で全 COM 参照を確定解放（参照カウント方式なので順序は本質ではないが読みやすさのため）。
            Release(converter);
            Release(rotator);
            Release(cached);
            Release(transform);
            Release(dstContext);
            if (srcContexts != null) foreach (var c in srcContexts) Release(c);
            Release(meta);
            Release(frame);
            Release(decoder);
            Release(stream);
            Release(factory);
            pin.Free(); // デコーダ解放後まで圧縮バイトを固定（WIC は CopyPixels 中に遅延読みする）
        }
    }

    /// <summary>EXIF Orientation（1〜8）→ 正立させる WICBitmapTransformOptions。</summary>
    /// <remarks>
    /// WIC は「回転→反転」の順で適用する前提の値（5=転置・7=反転置）。ミラー系（2/4/5/7）は
    /// カメラ実写ではほぼ出現せず未検証（間違っていても鏡像になるだけで落ちはしない）。
    /// </remarks>
    private static uint TransformFor(int orientation) => orientation switch
    {
        2 => WicTransformFlipHorizontal,
        3 => WicTransformRotate180,
        4 => WicTransformFlipVertical,
        5 => WicTransformRotate90 | WicTransformFlipHorizontal,
        6 => WicTransformRotate90,
        7 => WicTransformRotate270 | WicTransformFlipHorizontal,
        8 => WicTransformRotate270,
        _ => WicTransformRotate0,
    };

    /// <summary>メタデータから ushort 値を読む。タグ無し・型違いは <paramref name="fallback"/>。</summary>
    private static ushort ReadUShort(IWICMetadataQueryReader meta, string query, ushort fallback)
    {
        var pv = default(PropVariant);
        try
        {
            if (meta.GetMetadataByName(query, ref pv) < 0) return fallback;
            // VT_UI2(18)/VT_UI4(19)。値は PROPVARIANT の共用体先頭（p1 の下位バイト）。
            return pv.vt is 18 or 19 ? unchecked((ushort)(nuint)pv.p1) : fallback;
        }
        finally
        {
            PropVariantClear(ref pv);
        }
    }

    /// <summary>失敗 HRESULT を例外化する（S_FALSE 等の正の成功コードは通す）。</summary>
    private static void HR(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    /// <summary>RCW の COM 参照を即時に全解放する（null は無視）。</summary>
    private static void Release(object? o)
    {
        if (o != null) Marshal.FinalReleaseComObject(o);
    }

    // ---- 定数（wincodec.h と突き合わせ済み）----

    private static readonly Guid ClsidWicImagingFactory = new("cacaf262-9370-4615-a13b-9f5539da4c0a");
    private static readonly Guid PixelFormat32bppPBGRA = new("6fddc324-4e03-4bfe-b185-3d77768dc910");

    private const uint WicDecodeMetadataCacheOnDemand = 0;
    private const uint WicBitmapCacheOnLoad = 2;
    private const uint WicBitmapDitherTypeNone = 0;
    private const uint WicBitmapPaletteTypeCustom = 0;
    private const uint WicTransformRotate0 = 0;
    private const uint WicTransformRotate90 = 0x1;
    private const uint WicTransformRotate180 = 0x2;
    private const uint WicTransformRotate270 = 0x3;
    private const uint WicTransformFlipHorizontal = 0x8;
    private const uint WicTransformFlipVertical = 0x10;

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);

    /// <summary>PROPVARIANT（x64=24 / x86=16 バイト。Sequential で両対応）。値の共用体は <see cref="p1"/> から。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort vt;
        public ushort r1, r2, r3;
        public IntPtr p1;
        public IntPtr p2;
    }

    // ---- COM インターフェイス宣言 ----
    // 呼ばないスロットは引数なしプレースホルダ（位置さえ合っていれば呼ばない限り無害）。
    // 継承は使わずフラットに宣言する（.NET の COM interop は基底メソッドの並びを自前で
    // 書き下ろすのが最も事故が少ない）。別インターフェイス型として使う箇所はキャストで QI させる。

    [ComImport, Guid("ec5ec8a9-c395-4314-9c77-54d7a935ff70"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWICImagingFactory
    {
        [PreserveSig] int CreateDecoderFromFilename_();
        [PreserveSig] int CreateDecoderFromStream(IWICStream pIStream, IntPtr pguidVendor, uint metadataOptions, out IWICBitmapDecoder ppIDecoder);
        [PreserveSig] int CreateDecoderFromFileHandle_();
        [PreserveSig] int CreateComponentInfo_();
        [PreserveSig] int CreateDecoder_();
        [PreserveSig] int CreateEncoder_();
        [PreserveSig] int CreatePalette_();
        [PreserveSig] int CreateFormatConverter(out IWICFormatConverter ppIFormatConverter);
        [PreserveSig] int CreateBitmapScaler_();
        [PreserveSig] int CreateBitmapClipper_();
        [PreserveSig] int CreateBitmapFlipRotator(out IWICBitmapFlipRotator ppIBitmapFlipRotator);
        [PreserveSig] int CreateStream(out IWICStream ppIWICStream);
        [PreserveSig] int CreateColorContext(out IWICColorContext ppIWICColorContext);
        [PreserveSig] int CreateColorTransformer(out IWICColorTransform ppIWICColorTransform);
        [PreserveSig] int CreateBitmap_();
        [PreserveSig] int CreateBitmapFromSource(IWICBitmapSource pIBitmapSource, uint option, out IWICBitmapSource ppIBitmap);
        // 以降のスロット（CreateBitmapFromSourceRect〜）は未使用のため宣言しない。
    }

    [ComImport, Guid("135FF860-22B7-4ddf-B0F6-218F4F299A43"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWICStream
    {
        // ISequentialStream + IStream（Read〜Clone の 11 スロット）
        [PreserveSig] int Read_();
        [PreserveSig] int Write_();
        [PreserveSig] int Seek_();
        [PreserveSig] int SetSize_();
        [PreserveSig] int CopyTo_();
        [PreserveSig] int Commit_();
        [PreserveSig] int Revert_();
        [PreserveSig] int LockRegion_();
        [PreserveSig] int UnlockRegion_();
        [PreserveSig] int Stat_();
        [PreserveSig] int Clone_();
        // IWICStream
        [PreserveSig] int InitializeFromIStream_();
        [PreserveSig] int InitializeFromFilename_();
        [PreserveSig] int InitializeFromMemory(IntPtr pbBuffer, uint cbBufferSize);
        // InitializeFromIStreamRegion は未使用。
    }

    [ComImport, Guid("9EDDE9E7-8DEE-47ea-99DF-E6FAF2ED44BF"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWICBitmapDecoder
    {
        [PreserveSig] int QueryCapability_();
        [PreserveSig] int Initialize_();
        [PreserveSig] int GetContainerFormat_();
        [PreserveSig] int GetDecoderInfo_();
        [PreserveSig] int CopyPalette_();
        [PreserveSig] int GetMetadataQueryReader_();
        [PreserveSig] int GetPreview_();
        [PreserveSig] int GetColorContexts_();
        [PreserveSig] int GetThumbnail_();
        [PreserveSig] int GetFrameCount_();
        [PreserveSig] int GetFrame(uint index, out IWICBitmapFrameDecode ppIBitmapFrame);
    }

    [ComImport, Guid("00000120-a8f2-4877-ba0a-fd2b6645fb94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWICBitmapSource
    {
        [PreserveSig] int GetSize(out uint puiWidth, out uint puiHeight);
        [PreserveSig] int GetPixelFormat(out Guid pPixelFormat);
        [PreserveSig] int GetResolution_();
        [PreserveSig] int CopyPalette_();
        [PreserveSig] int CopyPixels(IntPtr prc, uint cbStride, uint cbBufferSize, IntPtr pbBuffer);
    }

    [ComImport, Guid("3B16811B-6A43-4ec9-A813-3D930C13B940"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWICBitmapFrameDecode
    {
        // IWICBitmapSource
        [PreserveSig] int GetSize(out uint puiWidth, out uint puiHeight);
        [PreserveSig] int GetPixelFormat(out Guid pPixelFormat);
        [PreserveSig] int GetResolution_();
        [PreserveSig] int CopyPalette_();
        [PreserveSig] int CopyPixels_();
        // IWICBitmapFrameDecode
        [PreserveSig] int GetMetadataQueryReader(out IWICMetadataQueryReader ppIMetadataQueryReader);
        [PreserveSig] int GetColorContexts(uint cCount,
            [In, Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IWICColorContext[]? ppIColorContexts,
            out uint pcActualCount);
        [PreserveSig] int GetThumbnail_();
    }

    [ComImport, Guid("30989668-E1C9-4597-B395-458EEDB808DF"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWICMetadataQueryReader
    {
        [PreserveSig] int GetContainerFormat_();
        [PreserveSig] int GetLocation_();
        [PreserveSig] int GetMetadataByName([MarshalAs(UnmanagedType.LPWStr)] string wzName, ref PropVariant pvarValue);
        [PreserveSig] int GetEnumerator_();
    }

    [ComImport, Guid("00000301-a8f2-4877-ba0a-fd2b6645fb94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWICFormatConverter
    {
        // IWICBitmapSource
        [PreserveSig] int GetSize_();
        [PreserveSig] int GetPixelFormat_();
        [PreserveSig] int GetResolution_();
        [PreserveSig] int CopyPalette_();
        [PreserveSig] int CopyPixels_();
        // IWICFormatConverter
        [PreserveSig] int Initialize(IWICBitmapSource pISource, in Guid dstFormat, uint dither, IntPtr pIPalette, double alphaThresholdPercent, uint paletteTranslate);
        [PreserveSig] int CanConvert_();
    }

    [ComImport, Guid("5009834F-2D6A-41ce-9E1B-17C5AFF7A782"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWICBitmapFlipRotator
    {
        // IWICBitmapSource
        [PreserveSig] int GetSize_();
        [PreserveSig] int GetPixelFormat_();
        [PreserveSig] int GetResolution_();
        [PreserveSig] int CopyPalette_();
        [PreserveSig] int CopyPixels_();
        // IWICBitmapFlipRotator
        [PreserveSig] int Initialize(IWICBitmapSource pISource, uint options);
    }

    [ComImport, Guid("3C613A02-34B2-44ea-9A7C-45AEA9C6FD6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWICColorContext
    {
        [PreserveSig] int InitializeFromFilename_();
        [PreserveSig] int InitializeFromMemory_();
        [PreserveSig] int InitializeFromExifColorSpace(uint value);
        [PreserveSig] int GetType_();
        [PreserveSig] int GetProfileBytes_();
        [PreserveSig] int GetExifColorSpace_();
    }

    [ComImport, Guid("B66F034F-D0E2-40ab-B436-6DE39E321A94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWICColorTransform
    {
        // IWICBitmapSource
        [PreserveSig] int GetSize_();
        [PreserveSig] int GetPixelFormat_();
        [PreserveSig] int GetResolution_();
        [PreserveSig] int CopyPalette_();
        [PreserveSig] int CopyPixels_();
        // IWICColorTransform
        [PreserveSig] int Initialize(IWICBitmapSource pIBitmapSource, IWICColorContext pIContextSource, IWICColorContext pIContextDest, in Guid pixelFmtDest);
    }
}
