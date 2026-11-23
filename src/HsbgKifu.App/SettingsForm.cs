using HsbgKifu.Capture;
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
    private readonly ComboBox _retention = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly List<int?> _retentionValues = new();
    private readonly Func<bool, Task<KifuWriterService.PruneResult>> _prune;
    private readonly string _original;

    /// <param name="config">今の設定（の写し）</param>
    /// <param name="startWithWindows">サインイン時に起動するようになっているか</param>
    /// <param name="prune">一時ログの整理（期間に関係なく）。引数が true なら消さずに件数と大きさだけ</param>
    public SettingsForm(KifuConfig config, bool startWithWindows, Func<bool, Task<KifuWriterService.PruneResult>> prune)
    {
        _config = config;
        _prune = prune;
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
        foreach (var days in KifuConfig.SlicesRetentionChoices) AddRetention(days, $"{days} 日");
        AddRetention(null, "消さない");
        var current = config.SlicesRetentionDays is int d && d > 0 ? d : (int?)null;
        if (!_retentionValues.Contains(current)) AddRetention(current, $"{current} 日"); // config.json を手で書き換えた値
        _retention.SelectedIndex = _retentionValues.IndexOf(current);

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
        Button pruneButton = null!;
        pruneButton = Button("一時ログを整理する...", async (_, _) => await PruneNow(pruneButton));
        Row("一時ログを残す期間", _retention, pruneButton);
        foreach (var c in new Control[] { _events, _powerLog, _hand, _startup })
        {
            layout.Controls.Add(new Label());
            layout.Controls.Add(c);
            layout.SetColumnSpan(c, 2);
        }
        var note = new Label
        {
            Text = "一時ログ（記録の出力先の .slices）は、記録の横に power.log.gz ができたゲームと記録にならなかったゲームの分を、残す期間を過ぎたら消します。" +
                   "「一時ログを整理する」は期間に関係なく消します（始まってから 3 時間以内のゲームは残します）。消した一時ログからは記録を作り直せません。\n" +
                   "MMR は HDT の BgsLastGames.xml から読みます（HDT と一緒に使ってください）。",
            AutoSize = true, MaximumSize = new Size(640, 0), Margin = new Padding(0, 10, 0, 0),
        };
        layout.Controls.Add(new Label());
        layout.Controls.Add(note);
        layout.SetColumnSpan(note, 2);
        var version = new Label
        {
            Text = $"hsbg-kifu {Watch.KifuVersion.Display}",
            AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 10, 0, 0),
        };
        layout.Controls.Add(new Label());
        layout.Controls.Add(version);
        layout.SetColumnSpan(version, 2);

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

    private void AddRetention(int? days, string label)
    {
        _retentionValues.Add(days);
        _retention.Items.Add(label);
    }

    /// <summary>期間に関係なく一時ログを整理する: 件数と容量を見せて、確認してから消す。</summary>
    private async Task PruneNow(Button button)
    {
        button.Enabled = false;
        try
        {
            var plan = await _prune(true);
            if (plan.Count == 0)
            {
                MessageBox.Show("整理できる一時ログはありません。\n記録の横に power.log.gz があるゲームと、記録にならなかったゲームの一時ログだけを消します（始まってから 3 時間以内のゲームは残します）。",
                    "一時ログの整理", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var answer = MessageBox.Show(
                $"一時ログ {plan.Count} 件（{KifuWriterService.FormatSize(plan.Bytes)}）を、期間に関係なく消します。\n" +
                "記録の JSON と、記録の横の power.log.gz は残ります。消した一時ログからは記録を作り直せません。\n\n消してよいですか？",
                "一時ログの整理", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;
            var done = await _prune(false);
            MessageBox.Show($"一時ログ {done.Count} 件を整理しました（{KifuWriterService.FormatSize(done.Bytes)}）。", "一時ログの整理", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception e)
        {
            MessageBox.Show("一時ログを整理できませんでした: " + e.Message, "一時ログの整理", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            button.Enabled = true;
        }
    }

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
        _config.SlicesRetentionDays = _retentionValues[_retention.SelectedIndex];
        DialogResult = DialogResult.OK;
        Close();
    }
}
