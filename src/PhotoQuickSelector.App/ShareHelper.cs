using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using WinRT;

namespace PhotoQuickSelector_App;

/// <summary>
/// 写真ファイルの共有（Alt+S）。設定済みの外部アプリ（<see cref="AppSettings.SharePath"/>）があれば
/// それを起動し、無ければ Windows 標準の共有シートを表示する（SPEC §3-8 / §6-3）。
/// 共有シートは WinUI 3 では <c>DataTransferManager.ShowShareUI()</c> を直接呼べず、
/// <c>IDataTransferManagerInterop</c> で対象ウィンドウの HWND を渡す相互運用が必要。
/// </summary>
public static class ShareHelper
{
    /// <summary>
    /// <paramref name="filePath"/> を共有する。<paramref name="settings"/> の SharePath が
    /// 設定済み（かつ存在）なら外部アプリ起動、そうでなければ共有シートを表示する。
    /// </summary>
    public static async Task ShareAsync(string filePath, AppSettings settings)
    {
        if (string.IsNullOrEmpty(filePath)) return;

        var sharePath = settings?.SharePath;
        // 設定済み exe があればそれを起動（ファイルパスを引数で渡す）。
        if (!string.IsNullOrWhiteSpace(sharePath) && File.Exists(sharePath))
        {
            try
            {
                Process.Start(new ProcessStartInfo(sharePath)
                {
                    Arguments = $"\"{filePath}\"",
                    UseShellExecute = true,
                });
            }
            catch { /* 起動失敗は黙殺（クラッシュさせない） */ }
            return;
        }

        // 未設定（または exe 消失）→ Windows 標準の共有シート。
        await ShowShareSheetAsync(filePath);
    }

    private static async Task ShowShareSheetAsync(string filePath)
    {
        StorageFile file;
        try
        {
            file = await StorageFile.GetFileFromPathAsync(filePath);
        }
        catch
        {
            return; // ファイル消失等。
        }

        try
        {
            var hwnd = App.WindowHandle;
            var interop = DataTransferManager.As<IDataTransferManagerInterop>();
            var managerPtr = interop.GetForWindow(hwnd, _dtmIid);
            var manager = MarshalInterface<DataTransferManager>.FromAbi(managerPtr);

            manager.DataRequested += (_, args) =>
            {
                var deferral = args.Request.GetDeferral();
                args.Request.Data.Properties.Title = file.Name;
                args.Request.Data.SetStorageItems(new[] { file });
                deferral.Complete();
            };

            interop.ShowShareUIForWindow(hwnd);
        }
        catch
        {
            // 共有シート表示失敗（環境依存）はアプリを止めない。
        }
    }

    // --- IDataTransferManagerInterop 相互運用 ---

    // DataTransferManager の IID（GetForWindow に渡す riid）。
    private static readonly Guid _dtmIid =
        new(0xa5caee9b, 0x8708, 0x49d1, 0x8d, 0x36, 0x67, 0xd2, 0x5a, 0x8d, 0xa0, 0x0c);

    [ComImport]
    [Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDataTransferManagerInterop
    {
        IntPtr GetForWindow([In] IntPtr appWindow, [In] ref Guid riid);
        void ShowShareUIForWindow([In] IntPtr appWindow);
    }
}
