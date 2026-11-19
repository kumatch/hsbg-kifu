using System.Globalization;

namespace HsbgKifu.Watch;

/// <summary>
/// Hearthstone のログの時刻は時:分:秒だけなので、フォルダ名（起動日時）から日付を補う。
/// 前の行より 12 時間以上戻ったら日付をまたいだとみなす。ファイルごとに 1 つ作る。
/// </summary>
public sealed class SessionClock
{
    private DateTime _date;
    private TimeSpan _last;

    public SessionClock(DateTime sessionStart)
    {
        _date = sessionStart.Date;
        _last = sessionStart.TimeOfDay;
    }

    public DateTime ToDateTime(TimeSpan timeOfDay)
    {
        if (timeOfDay < _last - TimeSpan.FromHours(12)) _date = _date.AddDays(1);
        _last = timeOfDay;
        return _date + timeOfDay;
    }

    /// <summary><c>Hearthstone_2026_11_15_12_33_42</c> → 2026-11-15 12:33:42（ローカル時刻）。</summary>
    public static DateTime? ParseSessionName(string folderName)
    {
        const string prefix = "Hearthstone_";
        if (!folderName.StartsWith(prefix, StringComparison.Ordinal)) return null;
        return DateTime.TryParseExact(folderName[prefix.Length..], "yyyy_MM_dd_HH_mm_ss", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal, out var t) ? t : null;
    }
}
