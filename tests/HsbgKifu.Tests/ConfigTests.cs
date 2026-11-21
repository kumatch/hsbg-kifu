using HsbgKifu.Config;
using Xunit;

namespace HsbgKifu.Tests;

public sealed class ConfigTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kifu-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("HSBG-Logs")]
    [InlineData(@"\HSBG-Logs")]
    [InlineData(@"%KIFU_TEST_NO_SUCH_VAR%\logs")]
    public void OutputDirectory_RejectsUnusablePaths(string? raw) => Assert.NotNull(OutputDirectory.Check(raw, out _));

    [Fact]
    public void OutputDirectory_CreatesFolderAndLeavesNoProbeFile()
    {
        var dir = Path.Combine(_root, "new", "logs");
        Assert.Null(OutputDirectory.Check(dir, out var resolved));
        Assert.Equal(dir, resolved);
        Assert.Empty(Directory.GetFileSystemEntries(dir));
    }

    [Theory]
    [InlineData(@"C:\Users\me\Documents\HSBG-Logs", @"%USERPROFILE%\Documents\HSBG-Logs")]
    [InlineData(@"C:\Users\me", "%USERPROFILE%")]
    [InlineData(@"C:\Users\me2\Logs", @"C:\Users\me2\Logs")] // check_private: ok (another user)
    [InlineData(@"D:\HSBG-Logs", @"D:\HSBG-Logs")]
    public void OutputDirectory_ToPortable(string path, string expected) => Assert.Equal(expected, OutputDirectory.ToPortable(path, @"C:\Users\me"));

    [Fact]
    public void KifuConfig_WritesDefaultsWhenMissingAndRoundTrips()
    {
        var path = Path.Combine(_root, "config.json");
        var first = KifuConfig.Load(path, out var problem);
        Assert.Null(problem);
        Assert.True(File.Exists(path));
        Assert.Equal(OutputDirectory.Default, first.OutputDir);
        Assert.Null(first.HearthstoneDir);
        Assert.Equal(60, first.MmrAfterWaitSec);

        first.OutputDir = @"D:\Kifu";
        first.WritePowerLog = false;
        first.MmrAfterWaitSec = 10;
        first.Save(path);
        var again = KifuConfig.Load(path, out _);
        Assert.Equal(@"D:\Kifu", again.OutputDir);
        Assert.False(again.WritePowerLog);
        Assert.Equal(10, again.MmrAfterWaitSec);
    }

    [Fact]
    public void KifuConfig_BrokenFileFallsBackToDefaultsAndKeepsTheFile()
    {
        var path = Path.Combine(_root, "config.json");
        Directory.CreateDirectory(_root);
        File.WriteAllText(path, "{ broken");
        var config = KifuConfig.Load(path, out var problem);
        Assert.NotNull(problem);
        Assert.Equal(OutputDirectory.Default, config.OutputDir);
        Assert.Equal("{ broken", File.ReadAllText(path));
    }

    [Fact]
    public void LogConfig_HdtSettingsHaveNoProblems()
    {
        const string text = "[Zone]\nLogLevel=1\n[Power]\nLogLevel=1\nFilePrinting=True\nConsolePrinting=False\nScreenPrinting=False\nVerbose=True\n";
        Assert.Empty(LogConfigCheck.ProblemsIn(text));
    }

    [Fact]
    public void LogConfig_ReportsMissingSectionAndWrongValues()
    {
        Assert.Single(LogConfigCheck.ProblemsIn("[Zone]\nLogLevel=1\n"));
        var problems = LogConfigCheck.ProblemsIn("[Power]\r\nLogLevel=1\r\nFilePrinting=false\r\n");
        Assert.Equal(2, problems.Count); // FilePrinting と Verbose
        Assert.Contains(problems, p => p.Contains("FilePrinting"));
        Assert.Contains(problems, p => p.Contains("Verbose"));
    }

    [Fact]
    public void LogConfig_MissingFileIsAProblem() =>
        Assert.Single(LogConfigCheck.Problems(Path.Combine(_root, "none", "log.config")));

    [Fact]
    public void TextLog_MovesTheFileAsideWhenItGetsLarge()
    {
        var path = Path.Combine(_root, "kifu.log");
        var log = new HsbgKifu.Diagnostics.TextKifuLog(TextWriter.Null, path, maxBytes: 100);
        for (var i = 0; i < 5; i++) log.Info(new string('x', 40));
        Assert.True(File.Exists(path + ".old"));
        Assert.True(new FileInfo(path).Length <= 200);
    }
}
