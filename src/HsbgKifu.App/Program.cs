namespace HsbgKifu.App;

/// <summary>常駐アプリ（タスクトレイ）。二重に起動しない（監視が 2 つ動くと同じゲームを 2 回書くため）。</summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, @"Local\hsbg-kifu", out var created);
        if (!created)
        {
            MessageBox.Show("hsbg-kifu はもう動いています（タスクトレイのアイコンから操作できます）。", "hsbg-kifu", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext());
    }
}
