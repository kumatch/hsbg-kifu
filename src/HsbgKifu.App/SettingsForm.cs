using HsbgKifu.Config;

namespace HsbgKifu.App;

/// <summary>設定画面（WinForms をコードで組む。プラグインの UI/SettingsWindow の項目を引き継ぐ）。OK を押すと出力先を確かめる。</summary>
internal sealed class SettingsForm : Form
{
    private readonly KifuConfig _config;
    private readonly TextBox _outputDir = new() { Width = 420 };
    private readonly TextBox _hearthstoneDir = new() { Width = 420, PlaceholderText = "空なら HDT の設定から探します" };
    private readonly CheckBox _events = new() { Text = "events.jsonl（ゲーム中の出来事の一覧）を書く", AutoSize = true };
    private readonly CheckBox _powerLog = new() { Text = "記録の横に power.log.gz を書く（同じロビーの全員の BattleTag が入ります。人に渡さないでください）", AutoSize = true };
    private readonly CheckBox _hand = new() { Text = "ショップ終了時の手札を記録する", AutoSize = true };
    private readonly NumericUpDown _mmrWait = new() { Minimum = 0, Maximum = 600, Width = 80 };
    private readonly CheckBox _startup = new() { Text = "Windows へのサインイン時に起動する", AutoSize = true };
    private readonly string _original;

    public SettingsForm(KifuConfig config, bool startWithWindows)
    {
        _config = config;
        _original = Newtonsoft.Json.JsonConvert.SerializeObject(config);
        _startup.Checked = startWithWindows;
        Text = "hsbg-kifu の設定";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        _outputDir.Text = config.OutputDir;
        _hearthstoneDir.Text = config.HearthstoneDir ?? "";
        _events.Checked = config.WriteEventsJsonl;
        _powerLog.Checked = config.WritePowerLog;
        _hand.Checked = config.SnapshotHand;
        _mmrWait.Value = Math.Clamp(config.MmrAfterWaitSec, 0, 600);

        var layout = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Fill };
        void Row(string label, Control control, params Control[] buttons)
        {
            layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) });
            layout.Controls.Add(control);
            var panel = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
            panel.Controls.AddRange(buttons);
            layout.Controls.Add(panel);
        }
        Row("記録の出力先", _outputDir, Button("参照...", (_, _) => Browse(_outputDir)), Button("既定に戻す", (_, _) => _outputDir.Text = OutputDirectory.Default));
        Row("Hearthstone のフォルダ", _hearthstoneDir, Button("参照...", (_, _) => Browse(_hearthstoneDir)));
        Row("後の MMR を待つ秒数", _mmrWait);
        foreach (var c in new Control[] { _events, _powerLog, _hand, _startup })
        {
            layout.Controls.Add(new Label());
            layout.Controls.Add(c);
            layout.SetColumnSpan(c, 2);
        }
        var note = new Label
        {
            Text = "中継ログ（記録の出力先の .slices）は常に残ります。MMR は HDT の BgsLastGames.xml から読みます（HDT と一緒に使ってください）。",
            AutoSize = true, MaximumSize = new Size(640, 0), Margin = new Padding(0, 10, 0, 0),
        };
        layout.Controls.Add(new Label());
        layout.Controls.Add(note);
        layout.SetColumnSpan(note, 2);

        var ok = new Button { Text = "保存", DialogResult = DialogResult.None, AutoSize = true };
        ok.Click += (_, _) => Save();
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Bottom };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(layout);
        Controls.Add(buttons);
    }

    /// <summary>保存した設定（OK のとき）。</summary>
    public KifuConfig Result => _config;

    /// <summary>config.json の項目が変わったか（OK のとき）。</summary>
    public bool ConfigChanged => Newtonsoft.Json.JsonConvert.SerializeObject(_config) != _original;

    /// <summary>サインイン時の起動（レジストリに書くので config.json には入れない）。</summary>
    public bool StartWithWindows => _startup.Checked;

    private static Button Button(string text, EventHandler onClick)
    {
        var b = new Button { Text = text, AutoSize = true };
        b.Click += onClick;
        return b;
    }

    private static void Browse(TextBox target)
    {
        using var dialog = new FolderBrowserDialog { UseDescriptionForTitle = true, Description = "フォルダを選んでください" };
        var current = Environment.ExpandEnvironmentVariables(target.Text);
        if (Directory.Exists(current)) dialog.InitialDirectory = current;
        if (dialog.ShowDialog() == DialogResult.OK) target.Text = dialog.SelectedPath;
    }

    private void Save()
    {
        var error = OutputDirectory.Check(_outputDir.Text, out var resolved);
        if (error != null)
        {
            MessageBox.Show(error, "記録の出力先", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var hs = _hearthstoneDir.Text.Trim();
        if (hs.Length > 0 && !Directory.Exists(Path.Combine(Environment.ExpandEnvironmentVariables(hs), "Logs")))
        {
            MessageBox.Show("このフォルダに Logs フォルダがありません（Hearthstone のフォルダを選んでください）。", "Hearthstone のフォルダ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _config.OutputDir = OutputDirectory.ToPortable(resolved, profile); // config.json にユーザー名を残さない
        _config.HearthstoneDir = hs.Length > 0 ? OutputDirectory.ToPortable(hs, profile) : null;
        _config.WriteEventsJsonl = _events.Checked;
        _config.WritePowerLog = _powerLog.Checked;
        _config.SnapshotHand = _hand.Checked;
        _config.MmrAfterWaitSec = (int)_mmrWait.Value;
        DialogResult = DialogResult.OK;
        Close();
    }
}
