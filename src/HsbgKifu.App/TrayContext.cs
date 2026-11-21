using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using HsbgKifu.Config;
using HsbgKifu.Diagnostics;
using HsbgKifu.Model;

namespace HsbgKifu.App;

/// <summary>
/// タスクトレイのアイコンとメニュー。KifuService（Power.log の監視と記録の書き込み）を別のスレッドで動かす。
/// 通知（バルーン）は問題があるときだけ出す（ゲームのたびには出さない。最後の記録はメニューとアイコンのヒントに出す）。
/// </summary>
internal sealed class TrayContext : ApplicationContext
{
    private static readonly string LogPath = Path.Combine(KifuConfig.DefaultDirectory, "kifu.log");

    private readonly SynchronizationContext _ui;
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _status = new() { Enabled = false };
    private readonly ToolStripMenuItem _last = new("最後の記録: まだありません") { Enabled = false };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 5000 };
    private readonly TextKifuLog _log = new(TextWriter.Null, LogPath);
    private KifuConfig _config;
    private KifuService? _service;
    private bool _warnedStopped;

    public TrayContext()
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _config = KifuConfig.Load(KifuConfig.DefaultPath, out var problem);
        if (problem != null) _log.Warn(problem);

        var menu = new ContextMenuStrip();
        menu.Items.Add(_status);
        menu.Items.Add(_last);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("記録のフォルダを開く", null, (_, _) => Open(_config.ResolveOutputDir()));
        menu.Items.Add("動作記録を開く", null, (_, _) => Open(LogPath));
        menu.Items.Add("設定...", null, async (_, _) => await ShowSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("終了", null, async (_, _) => await Quit());

        _icon = new NotifyIcon { Icon = DrawIcon(), Text = "hsbg-kifu", ContextMenuStrip = menu, Visible = true };
        _icon.DoubleClick += (_, _) => Open(_config.ResolveOutputDir());
        _timer.Tick += (_, _) => UpdateStatus();
        _timer.Start();

        _log.Info($"hsbg-kifu {Watch.KifuVersion.Current} を起動しました");
        StartService();
        var logConfig = LogConfigCheck.Problems();
        if (logConfig.Count > 0)
            Balloon("Power.log が出ない設定です", string.Join("\n", logConfig) + "\nHDT を起動するか、設定を直してから Hearthstone を起動し直してください。", ToolTipIcon.Warning);
    }

    private void StartService()
    {
        try
        {
            _service = new KifuService(_config, _log);
            _service.RecordWritten += (log, path) => _ui.Post(_ => OnRecordWritten(log, path), null);
            _service.Start();
            _warnedStopped = false;
        }
        catch (Exception e)
        {
            _log.Error("監視を始められませんでした", e);
            Balloon("監視を始められませんでした", e.Message, ToolTipIcon.Error);
        }
        UpdateStatus();
    }

    private async Task StopService()
    {
        if (_service == null) return;
        await _service.StopAsync();
        _service = null;
    }

    private void OnRecordWritten(GameLog log, string path)
    {
        var start = log.Game.StartedAt?.LocalDateTime.ToString("M/d HH:mm") ?? "?";
        var place = log.Result.Placement is { } p ? $"{p} 位" : "順位不明";
        var mmr = log.Result.MmrAfter is { } after ? $"、MMR {log.Result.MmrBefore?.ToString() ?? "?"} → {after}" : "";
        var hero = log.Player.Hero.Name ?? log.Player.Hero.CardId ?? "";
        _last.Text = $"最後の記録: {start} {hero} {place}{mmr}";
        _icon.Text = Clip($"hsbg-kifu: {start} {place}{mmr}");
    }

    private void UpdateStatus()
    {
        var running = _service?.Running == true;
        _status.Text = running ? $"監視中: {_service!.LogsDirectory}" : "止まっています（動作記録を見てください）";
        if (!running && _service != null && !_warnedStopped)
        {
            _warnedStopped = true;
            Balloon("監視が止まりました", "動作記録（メニューの「動作記録を開く」）を見てください。", ToolTipIcon.Error);
        }
    }

    private async Task ShowSettings()
    {
        var startupBefore = SafeStartupEnabled();
        using var form = new SettingsForm(_config.Clone(), startupBefore);
        if (form.ShowDialog() != DialogResult.OK) return;
        if (form.StartWithWindows != startupBefore) SetStartup(form.StartWithWindows);
        if (!form.ConfigChanged) return; // サインイン時の起動だけ変えたときは、監視をやり直さない
        try
        {
            form.Result.Save(KifuConfig.DefaultPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show("設定を保存できませんでした: " + e.Message, "hsbg-kifu", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        _config = form.Result;
        _log.Info("設定を変えたので、監視をやり直します");
        await StopService();
        StartService();
    }

    private void SetStartup(bool enabled)
    {
        try
        {
            Startup.Set(enabled);
            _log.Info(enabled ? "Windows へのサインイン時に起動するようにしました" : "Windows へのサインイン時に起動しないようにしました");
        }
        catch (Exception e)
        {
            _log.Error("サインイン時の起動を設定できませんでした", e);
            MessageBox.Show("サインイン時の起動を設定できませんでした: " + e.Message, "hsbg-kifu", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool SafeStartupEnabled()
    {
        try { return Startup.Enabled; }
        catch (Exception) { return false; }
    }

    private async Task Quit()
    {
        _timer.Stop();
        _icon.Visible = false;
        await StopService(); // 後の MMR を待っている記録は今の値で書き直す
        _log.Info("hsbg-kifu を終了しました");
        ExitThread();
    }

    private void Balloon(string title, string text, ToolTipIcon icon)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text;
        _icon.BalloonTipIcon = icon;
        _icon.ShowBalloonTip(10000);
    }

    private void Open(string path)
    {
        try
        {
            if (Directory.Exists(path) || File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else MessageBox.Show("まだありません: " + path, "hsbg-kifu", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception e)
        {
            _log.Error("開けませんでした: " + path, e);
        }
    }

    /// <summary>NotifyIcon.Text は 127 文字まで。</summary>
    private static string Clip(string s) => s.Length <= 127 ? s : s[..127];

    /// <summary>
    /// アイコン（画像ファイルを持たず、起動時に描く）: 濃い青の角丸に、金色の縁取りと「BG」。
    /// Hearthstone の雰囲気（夜空のような青と金）に寄せた配色だけで、ロゴの形や絵は使わない。
    /// </summary>
    private static Icon DrawIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var path = RoundedRect(new RectangleF(0.5f, 0.5f, 31, 31), 7);
            using (var fill = new LinearGradientBrush(new Rectangle(0, 0, 32, 32), Color.FromArgb(0x2B, 0x47, 0x8C), Color.FromArgb(0x10, 0x1B, 0x3D), LinearGradientMode.Vertical))
                g.FillPath(fill, path);
            using (var border = new Pen(Color.FromArgb(0xE8, 0xB8, 0x4A), 2f))
                g.DrawPath(border, path);
            using var font = new Font("Segoe UI", 17, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            var area = new RectangleF(0, 1, 32, 32);
            using (var shadow = new SolidBrush(Color.FromArgb(0x60, 0, 0, 0)))
                g.DrawString("BG", font, shadow, new RectangleF(1, 2, 32, 32), format);
            using (var text = new LinearGradientBrush(new Rectangle(0, 8, 32, 16), Color.FromArgb(0xFF, 0xE4, 0x8A), Color.FromArgb(0xD9, 0x96, 0x2B), LinearGradientMode.Vertical))
                g.DrawString("BG", font, text, area, format);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _icon.Dispose();
        }
        base.Dispose(disposing);
    }
}
