using System.IO.Compression;
using System.Text;
using HsbgKifu.Cards;
using HsbgKifu.Diagnostics;
using HsbgKifu.Model;
using HsbgKifu.Output;
using HsbgKifu.Parse;
using HsbgKifu.Watch;

namespace HsbgKifu.Capture;

/// <summary>中継ログ 1 つから作った記録。</summary>
public sealed record BuildResult(GameLog? Log, IReadOnlyList<string> Events, bool Discarded);

/// <summary>中継ログ（.power.log.gz + .slice.json）から記録（GameLog と events.jsonl の行）を作る。ゲーム中でも作り直しでも同じ処理。</summary>
public static class KifuBuilder
{
    public static BuildResult Build(string sliceGz, SliceMeta meta, CardDb cards, RecorderOptions? options = null, IKifuLog? log = null) =>
        Build(new[] { (sliceGz, meta) }, cards, options, log);

    /// <summary>
    /// 同じゲームの中継ログ（Hearthstone が落ちて再接続すると 2 つ以上に分かれる。開始時刻の順）を続けて読み、1 つの記録にする。
    /// 記録の game_id・開始時刻は最初の中継ログのもの。
    /// </summary>
    public static BuildResult Build(IReadOnlyList<(string Gz, SliceMeta Meta)> slices, CardDb cards, RecorderOptions? options = null, IKifuLog? log = null)
    {
        var recorder = new GameRecorder(slices[0].Meta, cards, options, log);
        foreach (var (gz, meta) in slices)
        {
            recorder.BeginSlice(meta);
            SliceReplay.Run(gz, meta, new PowerGameListener[] { recorder }, log);
        }
        recorder.Finish();
        return new BuildResult(recorder.Record, recorder.EventLines, recorder.Discarded);
    }
}

/// <summary>
/// 記録の書き出し: &lt;root&gt;\YYYY\MM\&lt;yyyyMMdd-HHmmss&gt;_&lt;game_id&gt;.json と、同じ名前の .events.jsonl / .power.log.gz（プラグインと同じ置き方）。
/// .power.log.gz は中継ログのうち <c>GameState.</c> の行だけ（プラグインの写し = HDT の Core.Game.PowerLog と同じ中身。
/// hsbg-kansousen の補完コマンドは行の出どころを区別しないので、PowerTaskList の行を入れると同じ変化を 2 回読む）。
/// どれも一時ファイルに書いてから置き換える。
/// </summary>
public static class KifuOutput
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public sealed record Options(bool WriteEvents = true, bool WritePowerLog = true);

    /// <summary>書いた記録のパス（記録が無ければ null）。</summary>
    public static string? Write(BuildResult result, string root, string sliceGz, Options? options = null) =>
        Write(result, root, new[] { sliceGz }, options);

    /// <summary>再接続でつないだ記録は、全部の中継ログの GameState の行をつなげて .power.log.gz にする。</summary>
    public static string? Write(BuildResult result, string root, IReadOnlyList<string> sliceGzs, Options? options = null)
    {
        options ??= new Options();
        if (result.Log == null) return null;
        var writer = new GameLogWriter(root);
        var basePath = writer.GetBasePath(result.Log);
        var path = basePath + ".json";
        GameLogWriter.WriteAtomic(path, GameLogWriter.Serialize(result.Log));
        if (options.WriteEvents)
            GameLogWriter.WriteAtomic(GameLogWriter.EventsPath(basePath), result.Events.Count == 0 ? "" : string.Join("\n", result.Events) + "\n");
        if (options.WritePowerLog)
            WriteGameStateLines(sliceGzs, basePath + ".power.log.gz");
        return path;
    }

    private static void WriteGameStateLines(IReadOnlyList<string> sliceGzs, string target)
    {
        var tmp = target + ".tmp";
        using (var output = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
        using (var zip = new GZipStream(output, CompressionLevel.Optimal))
        using (var w = new StreamWriter(zip, Utf8NoBom, 1 << 16) { NewLine = "\n" })
        {
            foreach (var sliceGz in sliceGzs)
            {
                using var reader = new StreamReader(LogFiles.OpenRead(sliceGz));
                while (reader.ReadLine() is { } line)
                    if (line.Length > 19 && line.AsSpan(19).StartsWith("GameState.", StringComparison.Ordinal)) w.WriteLine(line);
            }
        }
        File.Move(tmp, target, overwrite: true);
    }
}
