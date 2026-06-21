using System;
using System.IO;
using System.Runtime.InteropServices;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// Win32 共通ダイアログ <c>IFileOpenDialog</c>（<c>FOS_PICKFOLDERS</c>）を使ったフォルダ選択。
/// WinRT の <see cref="Windows.Storage.Pickers.FolderPicker"/> は任意パスから開けない
/// （<c>SuggestedStartLocation</c> は <c>PickerLocationId</c> 列挙のみ）ため、開始フォルダを
/// 指定して開きたいケースで使う。本プロジェクトは発行時もトリミング無効なので、従来型
/// <c>[ComImport]</c> の COM 相互運用で安全に使える。
/// </summary>
internal static class NativeFolderPicker
{
    /// <summary>
    /// <paramref name="startPath"/> を初期表示してフォルダ選択ダイアログを開く。選択された
    /// フォルダの実パスを返す。キャンセル・失敗時は <c>null</c>。<paramref name="startPath"/> が
    /// 存在しない場合は直近の存在する親フォルダから開く。
    /// </summary>
    public static string? PickFolder(nint ownerHwnd, string? startPath)
    {
        IFileOpenDialog? dialog = null;
        try
        {
            dialog = (IFileOpenDialog)new FileOpenDialogRcw();

            dialog.GetOptions(out var options);
            dialog.SetOptions(options | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM);

            // 開始フォルダ（存在する直近の祖先まで遡る）。
            var start = ResolveExisting(startPath);
            if (start != null &&
                SHCreateItemFromParsingName(start, IntPtr.Zero, typeof(IShellItem).GUID, out var item) == 0 &&
                item is IShellItem shellItem)
            {
                dialog.SetFolder(shellItem);
            }

            // Show はモーダル。キャンセルは ERROR_CANCELLED(0x800704C7) を返す。
            int hr = dialog.Show(ownerHwnd);
            if (hr != 0) return null;

            dialog.GetResult(out var resultItem);
            resultItem.GetDisplayName(SIGDN_FILESYSPATH, out var path);
            return path;
        }
        catch
        {
            return null;   // COM 生成失敗・キャンセル例外などはアプリを止めず null。
        }
        finally
        {
            if (dialog != null) Marshal.ReleaseComObject(dialog);
        }
    }

    /// <summary>存在する直近の祖先フォルダを返す（無ければ null）。</summary>
    private static string? ResolveExisting(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var current = Path.GetFullPath(path);
            while (!string.IsNullOrEmpty(current))
            {
                if (Directory.Exists(current)) return current;
                var parent = Path.GetDirectoryName(current);
                if (string.Equals(parent, current, StringComparison.OrdinalIgnoreCase)) break;
                current = parent ?? "";
            }
        }
        catch
        {
            // 不正パスは開始フォルダ指定なし扱い。
        }
        return null;
    }

    // --- COM 相互運用 ---

    private const uint FOS_PICKFOLDERS = 0x00000020;
    private const uint FOS_FORCEFILESYSTEM = 0x00000040;
    private const uint SIGDN_FILESYSPATH = 0x80058000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        string pszPath, IntPtr pbc, [In] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    private class FileOpenDialogRcw { }

    [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        // --- IModalWindow ---
        [PreserveSig] int Show(nint parent);

        // --- IFileDialog ---
        void SetFileTypes();                                   // 未使用（スロット確保）
        void SetFileTypeIndex(uint iFileType);                 // 未使用
        void GetFileTypeIndex(out uint piFileType);            // 未使用
        void Advise();                                         // 未使用
        void Unadvise();                                       // 未使用
        void SetOptions(uint fos);
        void GetOptions(out uint pfos);
        void SetDefaultFolder(IShellItem psi);                // 未使用
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);                  // 未使用
        void GetCurrentSelection(out IShellItem ppsi);        // 未使用
        void SetFileName(string pszName);                     // 未使用
        void GetFileName(out string pszName);                 // 未使用
        void SetTitle(string pszTitle);                       // 未使用
        void SetOkButtonLabel(string pszText);                // 未使用
        void SetFileNameLabel(string pszLabel);               // 未使用
        void GetResult(out IShellItem ppsi);
        void AddPlace();                                       // 未使用
        void SetDefaultExtension(string pszDefaultExtension); // 未使用
        void Close(int hr);                                   // 未使用
        void SetClientGuid(ref Guid guid);                    // 未使用
        void ClearClientData();                               // 未使用
        void SetFilter();                                     // 未使用

        // --- IFileOpenDialog ---
        void GetResults(out IntPtr ppenum);                  // 未使用
        void GetSelectedItems(out IntPtr ppsai);             // 未使用
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv); // 未使用
        void GetParent(out IShellItem ppsi);                 // 未使用
        void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs); // 未使用
        void Compare(IShellItem psi, uint hint, out int piOrder);   // 未使用
    }
}
