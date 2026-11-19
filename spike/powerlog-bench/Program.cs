using System; using System.Collections.Generic; using System.Diagnostics; using System.IO; using System.IO.Compression; using System.Text; using System.Text.RegularExpressions;

var path = args[0]; var outDir = args[1];
var sw = Stopwatch.StartNew(); var proc = Process.GetCurrentProcess();
var idRe = new Regex(@"id=(\d+)", RegexOptions.Compiled);
var createRe = new Regex(@"(?:ID|EntityID)=(\d+)", RegexOptions.Compiled);
var tagRe = new Regex(@"tag=(\S+) value=(\S*)", RegexOptions.Compiled);
var names = new Dictionary<string,int>();
Dictionary<int, Dictionary<string,string>> ents = null;
Dictionary<string,string> curEnt = null;
StreamWriter slice = null; int game = 0; long lines = 0, gsLines = 0, tagChanges = 0;
var intern = new Dictionary<string,string>();
string I(string s) { if (!intern.TryGetValue(s, out var v)) { intern[s] = s; v = s; } return v; }
using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
using var sr = new StreamReader(fs, new UTF8Encoding(false), false, 1 << 16);
string line;
void Close(string why) {
  if (slice == null) return; slice.Dispose(); slice = null;
  proc.Refresh();
  Console.WriteLine($"game {game} closed ({why}): entities={ents.Count} tags={Sum()} managed={GC.GetTotalMemory(false)/1048576}MB ws={proc.WorkingSet64/1048576}MB t={sw.Elapsed.TotalSeconds:F1}s");
}
int Sum() { int n = 0; foreach (var e in ents.Values) n += e.Count; return n; }
while ((line = sr.ReadLine()) != null) {
  lines++;
  int dash = line.IndexOf("() - ", StringComparison.Ordinal);
  if (dash < 0) { slice?.WriteLine(line); continue; }
  bool gs = line.AsSpan(19).StartsWith("GameState.DebugPrintPower(");
  if (gs && line.AsSpan(dash + 5).TrimStart().StartsWith("CREATE_GAME")) {
    Close("new CREATE_GAME"); game++;
    slice = new StreamWriter(new GZipStream(File.Create(Path.Combine(outDir, $"game{game}.power.log.gz")), CompressionLevel.Fastest), new UTF8Encoding(false));
    ents = new Dictionary<int, Dictionary<string,string>>(); names.Clear(); curEnt = null;
  }
  slice?.WriteLine(line);
  if (!gs || ents == null) continue;
  gsLines++;
  var body = line.AsSpan(dash + 5);
  var t = body.TrimStart();
  bool indented = body.Length != t.Length && t.StartsWith("tag=");
  if (indented) { var m = tagRe.Match(t.ToString()); if (m.Success && curEnt != null) curEnt[I(m.Groups[1].Value)] = I(m.Groups[2].Value); continue; }
  var s = t.ToString();
  if (s.StartsWith("FULL_ENTITY") || s.StartsWith("SHOW_ENTITY") || s.StartsWith("CHANGE_ENTITY") || s.StartsWith("GameEntity") || s.StartsWith("Player")) {
    var m = s.StartsWith("FULL_ENTITY - Creating") || s.StartsWith("GameEntity") || s.StartsWith("Player") ? createRe.Match(s) : idRe.Match(s);
    if (m.Success) { int id = int.Parse(m.Groups[1].Value); if (!ents.TryGetValue(id, out curEnt)) ents[id] = curEnt = new Dictionary<string,string>(); }
    continue;
  }
  if (s.StartsWith("TAG_CHANGE")) {
    tagChanges++;
    var m = tagRe.Match(s); if (!m.Success) continue;
    var im = idRe.Match(s); int id = -1;
    if (im.Success) id = int.Parse(im.Groups[1].Value);
    if (id >= 0 && ents.TryGetValue(id, out var e)) e[I(m.Groups[1].Value)] = I(m.Groups[2].Value);
    if (m.Groups[1].Value == "STATE" && m.Groups[2].Value == "COMPLETE") Close("STATE=COMPLETE");
  }
}
Close("EOF");
proc.Refresh();
Console.WriteLine($"lines={lines} gsLines={gsLines} tagChanges={tagChanges} elapsed={sw.Elapsed.TotalSeconds:F1}s cpu={proc.TotalProcessorTime.TotalSeconds:F1}s peakWS={proc.PeakWorkingSet64/1048576}MB");
