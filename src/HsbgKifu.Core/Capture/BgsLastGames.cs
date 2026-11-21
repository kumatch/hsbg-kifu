using System.Globalization;
using System.Xml;

namespace HsbgKifu.Capture;

/// <summary>1 ゲームの MMR（HDT の BgsLastGames.xml の 1 行）。</summary>
public sealed record BgsGameRating(DateTimeOffset StartTime, int? Rating, int? RatingAfter);

/// <summary>
/// HDT の <c>%APPDATA%\HearthstoneDeckTracker\BgsLastGames.xml</c> から MMR を読む（docs/research.md §M4）。
/// HDT はメモリから MMR を読んで、ゲームごとに <c>&lt;Game StartTime= Rating= RatingAfter= ...&gt;</c> を書く。直近の 18 ゲームほどしか残らない。
/// 読むのは StartTime / Rating / RatingAfter だけ（Player 属性はアカウント ID なので読まない）。HDT が書いている途中でも読めるよう共有モードで開く。
/// </summary>
public sealed class BgsLastGames
{
    /// <summary>ゲームの開始時刻（CREATE_GAME。HDT の StartTime より 1 秒ほど遅い）と照らすときの許容。</summary>
    public static readonly TimeSpan MatchWindow = TimeSpan.FromMinutes(2);

    private readonly string _path;

    public BgsLastGames(string? path = null) =>
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HearthstoneDeckTracker", "BgsLastGames.xml");

    /// <summary>開始時刻がいちばん近いゲーム（<see cref="MatchWindow"/> 以内）。ファイルが無い・読めない・見つからなければ null。</summary>
    public BgsGameRating? Find(DateTimeOffset gameStart)
    {
        BgsGameRating? best = null;
        foreach (var g in ReadAll())
        {
            var d = (g.StartTime - gameStart).Duration();
            if (d <= MatchWindow && (best == null || d < (best.StartTime - gameStart).Duration())) best = g;
        }
        return best;
    }

    public List<BgsGameRating> ReadAll()
    {
        var result = new List<BgsGameRating>();
        if (!File.Exists(_path)) return result;
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { IgnoreWhitespace = true, DtdProcessing = DtdProcessing.Ignore });
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.Name != "Game") continue;
                if (!DateTimeOffset.TryParse(reader.GetAttribute("StartTime"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)) continue;
                result.Add(new BgsGameRating(start, Int(reader.GetAttribute("Rating")), Int(reader.GetAttribute("RatingAfter"))));
            }
        }
        catch (Exception e) when (e is IOException or XmlException or UnauthorizedAccessException)
        {
            // HDT が書き換えている途中など。次に読むときにやり直す
        }
        return result;
    }

    private static int? Int(string? s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null;
}
