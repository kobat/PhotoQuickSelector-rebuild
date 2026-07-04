using Microsoft.Windows.ApplicationModel.Resources;

namespace PhotoQuickSelector_App;

/// <summary>
/// コード側のローカライズ文字列アクセサ。XAML は x:Uid（resw 直結）を使い、
/// コードビハインド／ViewModel の文言はこのヘルパ経由で resw
/// （<c>Strings/ja-JP|en-US/Resources.resw</c>）から取得する。
/// 言語は起動時の PrimaryLanguageOverride（<see cref="AppSettings.Language"/>）で決まる。
/// </summary>
internal static class Loc
{
    private static readonly ResourceLoader Loader = new();

    /// <summary>
    /// キーの文字列を取得する。見つからない／失敗時はキー自身を返す
    /// （UI が空文字にならないためのフォールバック。キーのタイポ検出も兼ねる）。
    /// </summary>
    public static string Get(string key)
    {
        try
        {
            var s = Loader.GetString(key);
            return string.IsNullOrEmpty(s) ? key : s;
        }
        catch
        {
            return key;
        }
    }

    /// <summary>フォーマット付き（resw 側に {0} 等のプレースホルダを持つ）。</summary>
    public static string Get(string key, params object?[] args)
    {
        try
        {
            return string.Format(Get(key), args);
        }
        catch (FormatException)
        {
            return Get(key);
        }
    }
}
