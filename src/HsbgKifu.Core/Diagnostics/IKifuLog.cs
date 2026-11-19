namespace HsbgKifu.Diagnostics;

/// <summary>記録アプリ自身の動作記録。BattleTag などの個人情報は書かない。</summary>
public interface IKifuLog
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
}

public sealed class NullKifuLog : IKifuLog
{
    public static readonly NullKifuLog Instance = new();
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message, Exception? exception = null) { }
}

/// <summary>標準出力と（指定すれば）ファイルに書く。複数のスレッドから呼んでよい。</summary>
public sealed class TextKifuLog : IKifuLog
{
    private readonly object _gate = new();
    private readonly TextWriter _console;
    private readonly string? _filePath;

    public TextKifuLog(TextWriter console, string? filePath = null)
    {
        _console = console;
        _filePath = filePath;
        if (filePath != null) Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception == null ? message : $"{message}: {exception}");

    private void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}";
        lock (_gate)
        {
            _console.WriteLine(line);
            if (_filePath == null) return;
            try { File.AppendAllText(_filePath, line + Environment.NewLine); }
            catch (IOException) { /* 動作記録の失敗で止めない */ }
        }
    }
}
