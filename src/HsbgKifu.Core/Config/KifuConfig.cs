using System.Text;
using Newtonsoft.Json;

namespace HsbgKifu.Config;

/// <summary>
/// 常駐アプリの設定（<c>%APPDATA%\hsbg-kifu\config.json</c>）。無ければ既定値で書き出す。設定画面で変えられる。
/// 項目はプラグインの config.json（PluginConfig）を引き継ぐ。
/// </summary>
public sealed class KifuConfig
{
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "hsbg-kifu");

    public static string DefaultPath => Path.Combine(DefaultDirectory, "config.json");

    /// <summary>記録の出力先（中継ログは &lt;output_dir&gt;\.slices）。環境変数を使える。</summary>
    [JsonProperty("output_dir")]
    public string OutputDir { get; set; } = OutputDirectory.Default;

    /// <summary>Hearthstone のフォルダ。null なら HDT の config.xml の HearthstoneDirectory、無ければ既定の場所。</summary>
    [JsonProperty("hearthstone_dir")]
    public string? HearthstoneDir { get; set; }

    [JsonProperty("write_events_jsonl")]
    public bool WriteEventsJsonl { get; set; } = true;

    /// <summary>記録の横に &lt;name&gt;.power.log.gz（GameState の行）を書く。中継ログ（.slices）は常に残る。</summary>
    [JsonProperty("write_power_log")]
    public bool WritePowerLog { get; set; } = true;

    /// <summary>ショップ終了時の手札を turns[].hand_end_of_shop に記録する。</summary>
    [JsonProperty("snapshot_hand")]
    public bool SnapshotHand { get; set; } = true;

    /// <summary>ゲームの後に、後の MMR が HDT の BgsLastGames.xml に入るまで待つ最長の秒数。</summary>
    [JsonProperty("mmr_after_wait_sec")]
    public int MmrAfterWaitSec { get; set; } = 60;

    public string ResolveOutputDir() => Environment.ExpandEnvironmentVariables(OutputDir);

    public string? ResolveHearthstoneDir() =>
        string.IsNullOrWhiteSpace(HearthstoneDir) ? null : Environment.ExpandEnvironmentVariables(HearthstoneDir.Trim());

    /// <summary>読む。無ければ既定値を書き出して返す。壊れていれば既定値（ファイルはそのまま）と、その理由。</summary>
    public static KifuConfig Load(string path, out string? problem)
    {
        problem = null;
        try
        {
            if (!File.Exists(path))
            {
                var config = new KifuConfig();
                config.Save(path);
                return config;
            }
            var loaded = JsonConvert.DeserializeObject<KifuConfig>(File.ReadAllText(path, Encoding.UTF8));
            if (loaded != null) return loaded;
            problem = "config.json が空です。既定の設定で動きます";
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            problem = $"config.json を読めません（{e.Message}）。既定の設定で動きます";
        }
        return new KifuConfig();
    }

    public KifuConfig Clone() => JsonConvert.DeserializeObject<KifuConfig>(JsonConvert.SerializeObject(this)) ?? new KifuConfig();

    /// <summary>一時ファイルに書いてから置き換える。</summary>
    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonConvert.SerializeObject(this, Formatting.Indented), new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }
}
