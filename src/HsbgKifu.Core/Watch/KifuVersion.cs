using System.Reflection;

namespace HsbgKifu.Watch;

/// <summary>
/// hsbg-kifu 自身の版。正本は Directory.Build.props の <c>&lt;Version&gt;</c>（タグ <c>v&lt;版&gt;</c> はそれに合わせて打つ。docs/release.md）。
/// </summary>
public static class KifuVersion
{
    /// <summary>x.y.z。記録の <c>generator.plugin_version</c> と <c>.slice.json</c> の <c>kifu_version</c> に書く。</summary>
    public static readonly string Current =
        typeof(KifuVersion).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    /// <summary>
    /// 画面と動作記録に出す版（例 <c>0.3.0（8a7ce60）</c>）。括弧の中はビルドしたコミット。
    /// .NET SDK が AssemblyInformationalVersion の <c>+</c> の後ろに入れるコミットのハッシュを使う。リリース後の開発ビルドと区別するため。
    /// </summary>
    public static readonly string Display = MakeDisplay(Current,
        typeof(KifuVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    internal static string MakeDisplay(string version, string? informationalVersion)
    {
        var plus = informationalVersion?.IndexOf('+') ?? -1;
        if (plus < 0) return version;
        var commit = informationalVersion![(plus + 1)..].Trim();
        if (commit.Length > 7) commit = commit[..7];
        return commit.Length == 0 ? version : $"{version}（{commit}）";
    }
}
