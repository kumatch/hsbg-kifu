namespace HsbgKifu.Watch;

/// <summary>
/// 伸び続ける Power.log を後ろから読む。Hearthstone の邪魔をしない共有モード（ReadWrite | Delete）で開いたまま、
/// 呼ばれるたびに増えた分だけ読み、改行までそろった行を返す。
/// 開いている間に名前が変わっても（終了時の Power.log → Power_old.log）同じファイルを読み続けられる。
/// </summary>
public sealed class LogTailer : IDisposable
{
    private readonly FileStream _stream;
    private readonly LineSplitter _splitter = new();
    private readonly byte[] _buffer = new byte[1 << 20];

    public string Path { get; }

    /// <summary>ファイルが読んだ位置より短くなった（作り直された）ことがある。</summary>
    public bool Truncated { get; private set; }

    public long Position => _splitter.Position;

    public LogTailer(string path)
    {
        Path = path;
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, LogFiles.ShareWithHearthstone, 1);
    }

    /// <summary>
    /// 増えた分を読む。1 回に読むのは最大 <paramref name="maxBytes"/> バイトで、まだ残っていれば <see cref="HasMore"/> が true。
    /// 行の文字列をまとめて作りすぎないよう、呼び出し側は少しずつ読んではすぐ処理する（起動直後の読み直しで数百 MB ある）。
    /// </summary>
    public List<LogLine> ReadAvailable(long maxBytes = 4L << 20)
    {
        var lines = new List<LogLine>();
        HasMore = false;
        var length = _stream.Length;
        if (length < _splitter.Position)
        {
            Truncated = true;
            return lines;
        }
        _stream.Position = _splitter.Position; // 書きかけの行のバイトも受け取り済み（LineSplitter が持っている）
        long read = 0;
        while (read < maxBytes)
        {
            var n = _stream.Read(_buffer, 0, (int)Math.Min(_buffer.Length, maxBytes - read));
            if (n <= 0) return lines;
            _splitter.Append(_buffer.AsSpan(0, n), lines);
            read += n;
        }
        HasMore = _stream.Length > _splitter.Position;
        return lines;
    }

    /// <summary>直前の <see cref="ReadAvailable"/> が上限で止まり、まだ読んでいない分がある。</summary>
    public bool HasMore { get; private set; }

    /// <summary>もう伸びないと分かったとき、改行で終わっていない最後の行も返す。</summary>
    public LogLine? FlushPending() => _splitter.FlushPending();

    public void Dispose() => _stream.Dispose();
}
