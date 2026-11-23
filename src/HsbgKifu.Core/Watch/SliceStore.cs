using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HsbgKifu.Watch;

/// <summary>
/// 中継ログ 1 つ分の付帯情報（<c>&lt;base&gt;.slice.json</c>）。BattleTag・接続先アドレス・spectateKey は入れない。
/// </summary>
public sealed class SliceMeta
{
    /// <summary>2: 記録との対応（outcome / record）と整理済み（pruned_at）を足した（0.4.0）。</summary>
    public const int CurrentFormat = 2;
    public const string StatusRecording = "recording";
    public const string StatusComplete = "complete";
    public const string StatusIncomplete = "incomplete";

    /// <summary>記録を書いた（record がその記録）。</summary>
    public const string OutcomeRecorded = "recorded";
    /// <summary>同じゲームの別の記録（プラグインの記録など）があったので書かなかった（record がその記録）。</summary>
    public const string OutcomeDuplicate = "duplicate";
    /// <summary>記録にしないゲームだった（ターン 1 の前に終わった、BG 以外）。</summary>
    public const string OutcomeNotRecorded = "not_recorded";

    [JsonPropertyName("format")] public int Format { get; set; } = CurrentFormat;
    /// <summary>ログフォルダ名（<c>Hearthstone_2026_11_15_12_33_42</c>）。</summary>
    [JsonPropertyName("session")] public string Session { get; set; } = "";
    /// <summary>CREATE_GAME の行の時刻（"HH:MM:SS.fffffff"）。session と合わせてゲームを見分ける。</summary>
    [JsonPropertyName("create_game_time")] public string CreateGameTime { get; set; } = "";
    /// <summary>切り出し元のファイル名（Power.log / Power_old.log / その .gz）と、CREATE_GAME の行のバイト位置。</summary>
    [JsonPropertyName("source_file")] public string SourceFile { get; set; } = "";
    [JsonPropertyName("source_offset")] public long SourceOffset { get; set; }
    [JsonPropertyName("start_time")] public DateTimeOffset StartTime { get; set; }
    [JsonPropertyName("end_time")] public DateTimeOffset? EndTime { get; set; }
    [JsonPropertyName("game_type")] public string? GameType { get; set; }
    [JsonPropertyName("build_number")] public int? BuildNumber { get; set; }
    /// <summary>サーバー側のゲーム番号（GameNetLogger.log）。見つからなければ null。</summary>
    [JsonPropertyName("game_handle")] public long? GameHandle { get; set; }
    /// <summary>Hearthstone が落ちた後の再接続で始まったか（GameNetLogger.log の reconnecting）。</summary>
    [JsonPropertyName("reconnecting")] public bool? Reconnecting { get; set; }
    /// <summary>recording（書いている途中）/ complete（STATE=COMPLETE を見た）/ incomplete（見ないまま終わった）。</summary>
    [JsonPropertyName("status")] public string Status { get; set; } = StatusRecording;
    [JsonPropertyName("server_complete")] public bool ServerComplete { get; set; }
    [JsonPropertyName("client_complete")] public bool ClientComplete { get; set; }
    [JsonPropertyName("line_count")] public long LineCount { get; set; }
    [JsonPropertyName("kifu_version")] public string WrittenBy { get; set; } = global::HsbgKifu.Watch.KifuVersion.Current;
    /// <summary>この中継ログから記録を作ったときの結果（<see cref="OutcomeRecorded"/> など）。まだ作っていない・作るのに失敗したなら null。</summary>
    [JsonPropertyName("outcome")] public string? Outcome { get; set; }
    /// <summary>このゲームの記録（出力先からの相対パス、拡張子なし、区切りは /）。recorded と duplicate のとき。</summary>
    [JsonPropertyName("record")] public string? Record { get; set; }
    /// <summary>整理した（.power.log.gz を消した）時刻。整理済みの中継ログは作り直しに使えないが、切り出し直さない。</summary>
    [JsonPropertyName("pruned_at")] public DateTimeOffset? PrunedAt { get; set; }

    [JsonIgnore] public string Key => MakeKey(Session, CreateGameTime);

    public static string MakeKey(string session, string createGameTime) => session + "|" + createGameTime;
}

/// <summary>
/// 中継ログの置き場: <c>&lt;root&gt;\YYYY\MM\&lt;yyyyMMdd-HHmmss&gt;_&lt;ゲーム番号&gt;.power.log(.gz)</c> と <c>.slice.json</c>。
/// 書いている間は平文で追記し（落ちても書いたところまで残る）、終わったら gzip にして平文を消す。
/// 整理（<see cref="Prune"/>）では .power.log.gz だけを消し、.slice.json は残す（Hearthstone の Logs に残っているフォルダから切り出し直さないため）。
/// 監視のスレッドと設定画面（整理のボタン）から呼ばれるので、置き場の一覧は鍵をかけて触る。
/// </summary>
public sealed class SliceStore
{
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly object _gate = new();
    private readonly Dictionary<string, (string MetaPath, SliceMeta Meta)> _index = new();

    public string Root { get; }

    public SliceStore(string root)
    {
        Root = root;
        Directory.CreateDirectory(root);
        foreach (var path in Directory.EnumerateFiles(root, "*.slice.json", SearchOption.AllDirectories))
        {
            try
            {
                var meta = JsonSerializer.Deserialize<SliceMeta>(File.ReadAllText(path), Json);
                if (meta != null) _index[meta.Key] = (path, meta);
            }
            catch (Exception e) when (e is IOException or JsonException)
            {
                // 壊れた付帯情報は無いものとして扱う（そのゲームは切り出し直す）
            }
        }
    }

    public IReadOnlyList<SliceMeta> All
    {
        get { lock (_gate) return _index.Values.Select(v => v.Meta).ToList(); }
    }

    /// <summary>1 ゲーム分の中継ログを書き終えた（gzip にした）。引数は付帯情報と .power.log.gz のパス。記録の JSON を作るのに使う。</summary>
    public event Action<SliceMeta, string>? SliceFinished;

    /// <summary>書き終えた中継ログ（付帯情報と .power.log.gz のパス）。作り直しのコマンドが使う。整理済みのものは入らない。</summary>
    public IReadOnlyList<(SliceMeta Meta, string Gz)> Finished
    {
        get
        {
            lock (_gate)
                return _index.Values.Where(v => v.Meta.Status != SliceMeta.StatusRecording && v.Meta.PrunedAt == null)
                    .Select(v => (v.Meta, GzPath(v.MetaPath)))
                    .Where(x => File.Exists(x.Item2))
                    .OrderBy(x => x.Meta.StartTime)
                    .ToList();
        }
    }

    /// <summary>
    /// 書き終えた（gzip がある）か、整理済みのゲームなら true（切り出さない）。書いている途中で止まったものは false（切り出し直す）。
    /// </summary>
    public bool IsDone(string key)
    {
        lock (_gate)
            return _index.TryGetValue(key, out var e) && e.Meta.Status != SliceMeta.StatusRecording
                   && (e.Meta.PrunedAt != null || File.Exists(GzPath(e.MetaPath)));
    }

    /// <summary>付帯情報を書き直す（記録との対応を書いたとき）。この置き場に無い中継ログなら何もしない。</summary>
    public void Update(SliceMeta meta)
    {
        lock (_gate)
        {
            if (!_index.TryGetValue(meta.Key, out var e)) return;
            _index[meta.Key] = (e.MetaPath, meta);
            WriteMeta(e.MetaPath, meta);
        }
    }

    /// <summary>中継ログの .power.log.gz の大きさ（無ければ 0）。</summary>
    public long GzLength(SliceMeta meta)
    {
        lock (_gate)
        {
            if (!_index.TryGetValue(meta.Key, out var e)) return 0;
            var info = new FileInfo(GzPath(e.MetaPath));
            return info.Exists ? info.Length : 0;
        }
    }

    /// <summary>整理する: .power.log.gz を消し、付帯情報に整理済みの印を付ける。消せたら true。</summary>
    public bool Prune(SliceMeta meta, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!_index.TryGetValue(meta.Key, out var e) || e.Meta.Status == SliceMeta.StatusRecording) return false;
            try
            {
                var gz = GzPath(e.MetaPath);
                if (File.Exists(gz)) File.Delete(gz);
                meta.PrunedAt = now;
                _index[meta.Key] = (e.MetaPath, meta);
                WriteMeta(e.MetaPath, meta);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    public SliceWriter Begin(SliceMeta meta)
    {
        lock (_gate) return BeginLocked(meta);
    }

    private SliceWriter BeginLocked(SliceMeta meta)
    {
        string basePath;
        if (_index.TryGetValue(meta.Key, out var old))
        {
            basePath = BasePath(old.MetaPath);
            TryDelete(basePath + ".power.log.gz");
        }
        else
        {
            var local = meta.StartTime.LocalDateTime;
            var dir = Path.Combine(Root, local.ToString("yyyy"), local.ToString("MM"));
            Directory.CreateDirectory(dir);
            var suffix = meta.GameHandle is { } h ? h.ToString() : "o" + meta.SourceOffset;
            if (meta.Reconnecting == true) suffix += "r";
            basePath = Path.Combine(dir, $"{local:yyyyMMdd-HHmmss}_{suffix}");
        }
        var metaPath = basePath + ".slice.json";
        _index[meta.Key] = (metaPath, meta);
        WriteMeta(metaPath, meta);
        return new SliceWriter(this, basePath, meta);
    }

    internal void Finish(string basePath, SliceMeta meta)
    {
        var plain = basePath + ".power.log";
        var gz = basePath + ".power.log.gz";
        var tmp = gz + ".tmp";
        using (var input = new FileStream(plain, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
        using (var output = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
        using (var zip = new GZipStream(output, CompressionLevel.Optimal))
            input.CopyTo(zip);
        File.Move(tmp, gz, overwrite: true);
        lock (_gate) WriteMeta(basePath + ".slice.json", meta);
        File.Delete(plain);
        SliceFinished?.Invoke(meta, gz);
    }

    internal void Abandon(string basePath, SliceMeta meta)
    {
        TryDelete(basePath + ".power.log");
        TryDelete(basePath + ".slice.json");
        lock (_gate) _index.Remove(meta.Key);
    }

    internal static void WriteMeta(string path, SliceMeta meta)
    {
        meta.Format = SliceMeta.CurrentFormat;
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(meta, Json), new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    private static string BasePath(string metaPath) => metaPath[..^".slice.json".Length];
    private static string GzPath(string metaPath) => BasePath(metaPath) + ".power.log.gz";

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
    }
}

/// <summary>1 ゲーム分の中継ログを書く。行は平文で追記し、<see cref="Flush"/> でディスクへ。</summary>
public sealed class SliceWriter : IDisposable
{
    private readonly SliceStore _store;
    private readonly string _basePath;
    private StreamWriter? _writer;

    public SliceMeta Meta { get; }
    public string BasePath => _basePath;

    internal SliceWriter(SliceStore store, string basePath, SliceMeta meta)
    {
        _store = store;
        _basePath = basePath;
        Meta = meta;
        var fs = new FileStream(basePath + ".power.log", FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
        _writer = new StreamWriter(fs, new UTF8Encoding(false)) { NewLine = "\n" };
    }

    public void Write(string line)
    {
        _writer!.WriteLine(line);
        Meta.LineCount++;
    }

    public void Flush() => _writer?.Flush();

    /// <summary>書き終える: gzip にして付帯情報を確定する。</summary>
    public void Finish(string status, DateTimeOffset? endTime)
    {
        Close();
        Meta.Status = status;
        Meta.EndTime = endTime;
        _store.Finish(_basePath, Meta);
    }

    /// <summary>記録しないゲーム（BG 以外）だったので消す。</summary>
    public void Abandon()
    {
        Close();
        _store.Abandon(_basePath, Meta);
    }

    public void Dispose() => Close();

    private void Close()
    {
        _writer?.Dispose();
        _writer = null;
    }
}
