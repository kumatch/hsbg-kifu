using Microsoft.Win32;

namespace HsbgKifu.App;

/// <summary>
/// Windows へのサインイン時に起動する設定（自分のユーザーの <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> の値）。既定は OFF で、
/// メニューで選んだときだけ書く。ほかのレジストリには触らない。
/// </summary>
internal static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "hsbg-kifu";

    private static string Command => $"\"{Environment.ProcessPath}\"";

    public static bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string v && string.Equals(v, Command, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, Command);
        else if (key.GetValue(ValueName) != null) key.DeleteValue(ValueName);
    }
}
