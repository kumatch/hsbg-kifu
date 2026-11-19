using System.Text;

namespace HsbgKifu.Watch;

/// <summary>1 行と、その行の先頭のバイト位置（gzip なら展開後の位置）。</summary>
public readonly record struct LogLine(string Text, long Offset);

/// <summary>
/// バイト列を受け取り、改行（LF）までそろった行だけを返す。書きかけの最後の行は次のバイト列を待つ。
/// UTF-8 の文字が読み込みの境目で分かれても、行ごとに復号するので化けない。行末の CR は除く。
/// </summary>
public sealed class LineSplitter
{
    private byte[] _pending = new byte[4096];
    private int _pendingLength;
    private long _pendingOffset;

    /// <summary>次に受け取るバイトの位置（ここまで受け取った）。</summary>
    public long Position { get; private set; }

    /// <summary>まだ改行が来ていない書きかけの行のバイト数。</summary>
    public int PendingLength => _pendingLength;

    public LineSplitter(long startOffset = 0)
    {
        Position = startOffset;
        _pendingOffset = startOffset;
    }

    public void Append(ReadOnlySpan<byte> data, List<LogLine> output)
    {
        var start = 0;
        while (start < data.Length)
        {
            var nl = data[start..].IndexOf((byte)'\n');
            if (nl < 0)
            {
                Keep(data[start..]);
                break;
            }
            var piece = data.Slice(start, nl);
            if (_pendingLength > 0)
            {
                Keep(piece);
                output.Add(new LogLine(Decode(_pending.AsSpan(0, _pendingLength)), _pendingOffset));
                _pendingLength = 0;
            }
            else
            {
                output.Add(new LogLine(Decode(piece), Position + start));
            }
            start += nl + 1;
            _pendingOffset = Position + start;
        }
        Position += data.Length;
    }

    /// <summary>書きかけの行を捨てずに 1 行として返す（ファイルが終わった、もう追記されないと分かったとき）。</summary>
    public LogLine? FlushPending()
    {
        if (_pendingLength == 0) return null;
        var line = new LogLine(Decode(_pending.AsSpan(0, _pendingLength)), _pendingOffset);
        _pendingLength = 0;
        _pendingOffset = Position;
        return line;
    }

    private void Keep(ReadOnlySpan<byte> piece)
    {
        if (_pendingLength + piece.Length > _pending.Length)
            Array.Resize(ref _pending, Math.Max(_pending.Length * 2, _pendingLength + piece.Length));
        piece.CopyTo(_pending.AsSpan(_pendingLength));
        _pendingLength += piece.Length;
    }

    private static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > 0 && bytes[^1] == (byte)'\r') bytes = bytes[..^1];
        return Encoding.UTF8.GetString(bytes);
    }
}
