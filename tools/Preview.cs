#:project ../src/RhythmHint.Reference/RhythmHint.Reference.csproj
#:property TargetFramework=net10.0
#:property TreatWarningsAsErrors=false
#:property PublishAot=false

// 离线预览：不开游戏，用文字画出某一关的排键结果（和 Mod 用同一套规则，只看关卡自己的砖块）。
// 可选再给一张自己手排的参考谱：只用来对照「自动排键像不像自己的手法」，不会改变排键结果。
// 用法：dotnet run tools/Preview.cs -- <关卡.adofai> [参考谱.pez/.json] [--from 秒] [--seconds 秒]
//       [--reset 毫秒] [--focus 毫秒] [--jack 毫秒] [--chord 毫秒] [--no-roll] [--inner-roll] [--left-handed] [--chord-other-hand]   临时试排键参数
//       [--keys 4|6|8]   键数（默认 4）
//       [--export 文件.json]   导出砖块时间和节拍线，给 tools/layout-lab 网页用
//       [--editor-session 目录] [--alternate]   给手序编辑器写 session.js（和游戏里「编辑本关手序」一样，离线近似：没有游戏音频采样）；
//                                               --alternate 用交替优先排法，默认标准。比如写进 src/RhythmHint.Editor 后直接打开 index.html 看界面
// 支持常见 angleData / pathData（绝对方向、中旋与现代相对方向）+ SetSpeed + Twirl + Pause；离线仅作近似，游戏时序以游戏内导出为准。

using System.Globalization;
using Newtonsoft.Json.Linq;
using RhythmHint.Core;
using RhythmHint.Reference;

var valueOptions = new HashSet<string> { "--from", "--seconds", "--reset", "--focus", "--jack", "--chord", "--keys", "--export", "--editor-session" };
var files = args.Where((a, i) => !a.StartsWith("--", StringComparison.Ordinal)
    && (i == 0 || !valueOptions.Contains(args[i - 1]))).ToList();
double from = ReadOption("--from", 0), span = ReadOption("--seconds", 6);
if (files.Count == 0)
{
    Console.Error.WriteLine("用法：dotnet run tools/Preview.cs -- <关卡.adofai> [参考谱] [--from 秒] [--seconds 秒] [--reset|--focus|--jack|--chord 毫秒] [--no-roll] [--inner-roll] [--left-handed] [--chord-other-hand] [--alternate] [--keys 4|6|8] [--editor-session 目录]");
    return 2;
}

var paths = new HashSet<string>(files.Select(Path.GetFullPath),
    OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
string? labOutput;
string? sessionFolder = null;
try
{
    labOutput = OutputPath("--export");
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

var options = new FingeringOptions();
options.ResetWindow = ReadOption("--reset", options.ResetWindow * 1000) / 1000;
options.ChordFocusWindow = ReadOption("--focus", options.ChordFocusWindow * 1000) / 1000;
options.UseRoll = !args.Contains("--no-roll");
options.OuterRoll = !args.Contains("--inner-roll");
options.LeftHanded = args.Contains("--left-handed");
options.ChordsOnOtherHand = args.Contains("--chord-other-hand");
options.Style = args.Contains("--alternate") ? FingeringStyle.ReviewPreferenceV1 : FingeringStyle.TimeReset;
var sessionIndex = Array.IndexOf(args, "--editor-session");
if (sessionIndex >= 0 && sessionIndex + 1 < args.Length)
{
    sessionFolder = Path.GetFullPath(args[sessionIndex + 1]);
}
options.JackWindow = ReadOption("--jack", options.JackWindow * 1000) / 1000;
options.ChordWindow = ReadOption("--chord", options.ChordWindow * 1000) / 1000;
options.Keys = (int)ReadOption("--keys", 4);
if (!KeyLayout.IsMode(options.Keys))
{
    Console.Error.WriteLine($"键数只能是 4、6、8：{options.Keys}");
    return 2;
}

var layout = KeyLayout.Of(options.Keys);

var floorPath = new List<PathPoint>();
List<TileHit> tiles;
List<(double Time, double Speed)> speeds;
double crotchet;
JObject levelDocument;
try
{
    levelDocument = AdofaiTiming.ParseJson(File.ReadAllText(files[0]));
    (tiles, speeds, crotchet) = AdofaiTiming.Read(levelDocument, floorPath);
}
catch (Exception ex) when (ex is Newtonsoft.Json.JsonReaderException or InvalidDataException or IOException)
{
    Console.Error.WriteLine($"读不了关卡文件 {files[0]}：{ex.Message}");
    Console.Error.WriteLine("离线读谱只是近似：要用游戏里的实际砖块，在游戏中按 F8 → 专属手序 → 编辑本关手序。");
    return 1;
}

if (tiles.Count == 0)
{
    Console.Error.WriteLine("关卡没有可供预览的击打；请在游戏中导出实际排键。");
    return 1;
}

Console.WriteLine($"关卡：{Path.GetFileName(files[0])}，需要按键 {tiles.Count} 次，{tiles[0].Time:F2}s – {tiles[^1].Time:F2}s");
// 节拍线：编辑器用推出来的拍子和小节（和游戏里一样，BeatGrid.Infer）；试验台网页有自己的 JS 版旧节拍线（preview-timing.test.cjs 核对两边一致），导出仍给旧的
var grid = BeatGrid.Infer(tiles, speeds, crotchet, 0, tiles[^1].Time + 2);
var labBeats = BeatGrid.Build(BeatGrid.MusicalTempo(speeds, crotchet), 0, tiles[^1].Time + 2);
Console.WriteLine($"节拍线：{grid.Summary()}");
var levelSettings = levelDocument["settings"]!;
if (labOutput != null)
{
    // 给排键试验台网页用：砖块时间（毫秒）、长按、节拍线
    var export = new
    {
        name = Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(files[0]))),
        bpm = Math.Round(60 / crotchet, 3),
        // 歌曲：音频时间 = 砖块时间 + offset（和游戏 scrConductor 的 addoffset 一样）
        song = (string?)levelSettings["songFilename"] ?? "",
        offset = (double?)levelSettings["offset"] ?? 0,
        pitch = (double?)levelSettings["pitch"] ?? 100,
        // 每块：序号、时间（毫秒）、长按（毫秒）、和上一块之间的转角（度，算不出来是 null）
        tiles = tiles.Select(t => new object?[] { t.TileIndex, Math.Round(t.Time * 1000, 1), Math.Round(t.HoldSeconds * 1000, 1),
            double.IsNaN(t.Angle) ? null : Math.Round(t.Angle, 3) }),
        beats = labBeats.Select(b => new[] { Math.Round(b.Time * 1000, 1), b.IsMeasure ? 1 : 0 }),
    };
    File.WriteAllText(labOutput, Newtonsoft.Json.JsonConvert.SerializeObject(export));
    Console.WriteLine($"已导出 {tiles.Count} 块砖、{labBeats.Count} 条节拍线到 {labOutput}");
}
var notes = NotePlanner.Plan(tiles, options);
static FingeringOptions With(FingeringOptions o, int keys) => new()
{
    Style = o.Style, LeftHanded = o.LeftHanded, ChordsOnOtherHand = o.ChordsOnOtherHand, UseRoll = o.UseRoll, OuterRoll = o.OuterRoll,
    ResetWindow = o.ResetWindow, ChordFocusWindow = o.ChordFocusWindow, JackWindow = o.JackWindow, ChordAngle = o.ChordAngle, Keys = keys,
};
if (sessionFolder != null)
{
    // 和游戏里「编辑本关手序」交给编辑器的一样，只是离线近似：轨道形状按关卡方向和 PositionTrack 算、没有波形；歌曲文件在关卡旁边就一起拷过去
    var fingering = CustomFingering.From(tiles, notes, options.LeftHanded, options.Keys);
    var hitTiles = new HashSet<int>(tiles.Select(t => t.TileIndex));
    fingering.Song = (string?)levelSettings["song"] ?? Path.GetFileNameWithoutExtension(files[0]);
    fingering.Artist = (string?)levelSettings["artist"] ?? "";
    fingering.Charter = (string?)levelSettings["author"] ?? "";
    var standard = new FingeringOptions { Style = FingeringStyle.TimeReset, LeftHanded = options.LeftHanded, ChordsOnOtherHand = options.ChordsOnOtherHand, Keys = options.Keys };
    var alternate = new FingeringOptions { Style = FingeringStyle.ReviewPreferenceV1, LeftHanded = options.LeftHanded, ChordsOnOtherHand = options.ChordsOnOtherHand, Keys = options.Keys };
    Directory.CreateDirectory(sessionFolder);
    var id = SessionLibrary.IdOf(fingering);
    var pitch = (double?)levelSettings["pitch"] ?? 100;
    var audio = new SessionAudio
    {
        Pitch = double.IsFinite(pitch) && pitch > 0 ? pitch / 100 : 1,
        Offset = MusicTiming.Offset(((double?)levelSettings["offset"] ?? 0) / 1000, crotchet, countdownBeatsInTileTimes: 0),
    };
    var song = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(files[0]))!, (string?)levelSettings["songFilename"] ?? "");
    if (File.Exists(song))
    {
        // 音乐时间 = 砖块时间 + offset − 起始一拍（MusicTiming；离线读谱的砖块时间不算倒计时，7 张工坊关卡按起音对齐验证过）
        audio.File = SessionLibrary.AudioFile(sessionFolder, id, Path.GetExtension(song));
        File.Copy(song, Path.Combine(sessionFolder, audio.File), overwrite: true);
    }

    var effects = TrackEffects.Extract(levelDocument);
    TrackEffects.AddBeats(effects, speeds, crotchet);
    // 另外两种键数（编辑器里切换），都是自动排键
    var variants = new List<SessionVariant>();
    foreach (var keys in KeyLayout.Modes.Where(k => k != options.Keys))
    {
        var keyed = CustomFingering.From(tiles, NotePlanner.Plan(tiles, With(options, keys)), options.LeftHanded, keys);
        keyed.Song = fingering.Song;
        keyed.Artist = fingering.Artist;
        keyed.Charter = fingering.Charter;
        variants.Add(new SessionVariant
        {
            Id = SessionLibrary.IdOf(keyed),
            Fingering = keyed,
            FileName = keyed.SuggestedFileName(),
            KeyLabels = KeyBindings.Split(new RhythmHint.Settings().KeyLabelsFor(keys), keys),
            Standard = NotePlanner.Plan(tiles, With(standard, keys)),
            Alternate = NotePlanner.Plan(tiles, With(alternate, keys)),
        });
    }

    var session = new EditorSession
    {
        Variants = variants,
        Id = id,
        Fingering = fingering,
        FileName = fingering.SuggestedFileName(),
        SaveFolder = Path.Combine(sessionFolder, "Fingerings"),
        Language = "zh-Hans",
        Standard = NotePlanner.Plan(tiles, standard),
        Alternate = NotePlanner.Plan(tiles, alternate),
        Beats = grid.Lines,
        Tempo = BeatGrid.MusicalTempo(speeds, crotchet).ConvertAll(t => (t.Time, 60 / t.SecondsPerBeat)),
        Audio = audio,
        Effects = effects,
        Path = floorPath.ConvertAll(p => p with { Hit = hitTiles.Contains(p.Tile) }),
    };
    SessionLibrary.Save(sessionFolder, session, DateTimeOffset.Now); // session.js + 编辑器的历史（recent/），和游戏里一样
    Console.WriteLine($"已写好编辑器数据：{Path.Combine(sessionFolder, "session.js")}（{notes.Count} 个音符，音乐：{(audio.File.Length == 0 ? "无" : audio.File)}）");
}
var doubles = notes.Count(n => n.Lanes.Count == 2);
var rightDoubles = notes.Count(n => n.Lanes.Count == 2 && PairHand(n) == 1);
var laneUse = Enumerable.Range(0, layout.Count).Select(l => notes.Count(n => n.Lanes.ContainsKey(l)));
var byAngle = tiles.Exists(t => !double.IsNaN(t.Angle));
Console.WriteLine($"提示 {notes.Count} 个，双押 {doubles} 个（右手 {rightDoubles}，左手 {doubles - rightDoubles}），"
    + $"三押 {notes.Count(n => n.Lanes.Count == 3)} 个，四押 {notes.Count(n => n.Lanes.Count == 4)} 个，"
    + $"五押以上 {notes.Count(n => n.Lanes.Count > 4)} 个（{(byAngle ? "按转角" : "按时间")}判断），"
    + $"{layout.Count} 键，各轨使用次数 [{string.Join(", ", laneUse)}]");
var tooFast = notes.Zip(notes.Skip(1), (a, b) => (a, b)).Count(p => p.a.IsChord && p.b.IsChord && p.b.Time - p.a.Time < options.JackWindow);
Console.WriteLine($"两个多押挨得太近、同一只手来不及（只能左右手轮流）：{tooFast} 处");

// 散点程度：连在一起（间隔不超过 300 ms）的相邻两下，第一条轨道相差 2 条及以上算「跳轨」
int linked = 0, jumps = 0;
for (var i = 1; i < notes.Count; i++)
{
    if (notes[i].Time - notes[i - 1].Time > LayoutStats.DefaultLinkGap)
    {
        continue;
    }

    linked++;
    var step = notes[i].Lanes.Keys.First() - notes[i - 1].Lanes.Keys.First();
    jumps += Math.Abs(step) >= 2 ? 1 : 0;
}

Console.WriteLine($"跳轨：{jumps}/{linked}（{(linked == 0 ? 0 : jumps * 100.0 / linked):F1}%）");
var lone = LayoutStats.LoneNotes(notes, LayoutStats.DefaultLinkGap);
Console.WriteLine($"孤立音符（前后都隔 2 条以上轨道）：{lone.Count} 个{(lone.Count == 0 ? "" : "，例如 " + string.Join("、", lone.Take(5).Select(n => $"{n.Time:F2}s")))}");

// 手序：快的地方同一只手连着按两个单押（轮指以外不该有）；停顿之后从哪只手起手（应该总是主手：默认右手，--left-handed 时左手）
// 多押看按两个键的那只手（和 PlannedNote.PairHand 一样，按这个键数分手）
int PairHand(PlannedNote n) => n.Lanes.Keys.Count(l => layout.HandOf(l) == 1) * 2 >= n.Lanes.Count ? 1 : 0;
int HandOf(PlannedNote n) => n.IsChord ? PairHand(n) : layout.HandOf(n.Lanes.Keys.First());
var mainHand = options.LeftHanded ? 0 : 1;
int fastPairs = 0, sameHand = 0, restarts = 0, mainStarts = 0;
for (var i = 1; i < notes.Count; i++)
{
    var gap = notes[i].Time - notes[i - 1].Time;
    if (gap > options.ResetWindow)
    {
        restarts++;
        mainStarts += HandOf(notes[i]) == mainHand ? 1 : 0;
    }
    else if (!notes[i].IsChord && !notes[i - 1].IsChord)
    {
        fastPairs++;
        sameHand += HandOf(notes[i]) == HandOf(notes[i - 1]) ? 1 : 0;
    }
}

Console.WriteLine($"手序：主手重置间隔以内的相邻单押 {fastPairs} 对，同一只手 {sameHand} 对（轮指）；停顿之后{(options.LeftHanded ? "左" : "右")}手（主手）起手 {mainStarts}/{restarts}");

// 同一根手指隔不到主手重置间隔又按（不管中间按了别的）：6K、8K 多出来的手指就是为了减少它（越快用的手指越多）
var lastPress = new Dictionary<int, double>();
int quickRepeats = 0, presses = 0;
foreach (var n in notes)
{
    foreach (var lane in n.Lanes.Keys)
    {
        presses++;
        if (lastPress.TryGetValue(lane, out var before) && n.Time - before <= options.ResetWindow)
        {
            quickRepeats++;
        }

        lastPress[lane] = n.Time;
    }
}

Console.WriteLine($"同一根手指隔不到 {options.ResetWindow * 1000:0} ms 又按：{quickRepeats}/{presses}（{(presses == 0 ? 0 : quickRepeats * 100.0 / presses):F1}%）");

List<ChartGroup>? reference = null;
AlignResult? align = null;
if (files.Count > 1)
{
    var chart = RpeChart.Load(files[1]);
    reference = chart.ToGroups().ToList();
    var c = ReferenceComparison.Compare(tiles, notes, reference);
    align = c.Align;
    Console.WriteLine($"参考谱「{chart.Name}」：对上 {c.Compared} 个提示（平移 {c.Align.Shift:+0.000;-0.000}s）");
    Console.WriteLine($"  同手 {c.SameHands * 100.0 / c.Compared:F1}%，同轨 {c.SameLanes * 100.0 / c.Compared:F1}%，单押/双押一致 {c.ChordAgree * 100.0 / c.Compared:F1}%");
}

Console.WriteLine();
var laneDigits = string.Concat(Enumerable.Range(0, layout.Count));
Console.WriteLine($"  时间      | 自动 {laneDigits} |{(reference is null ? "" : " 参考 0123 |")}  ({from:F1}s 起 {span:F1}s；O 单押，# 多押，= 带长按)");
var groupOfTile = new Dictionary<int, int>();
for (var i = 0; i < tiles.Count && align is not null; i++)
{
    groupOfTile[tiles[i].TileIndex] = align.GroupOfTile[i];
}

foreach (var n in notes.Where(n => n.Time >= from && n.Time < from + span))
{
    var row = string.Concat(Enumerable.Range(0, layout.Count).Select(l =>
        !n.Lanes.TryGetValue(l, out var hold) ? "." : hold > 0 ? "=" : n.IsChord ? "#" : "O"));
    var refRow = "";
    if (reference is not null)
    {
        var g = groupOfTile.GetValueOrDefault(n.TileIndex, -1);
        refRow = g < 0 ? " ----      |" : " " + string.Concat(Enumerable.Range(0, Lanes.Count).Select(l =>
            reference[g].Holds[l] is not double h ? "." : h > 0 ? "=" : reference[g].LaneCount > 1 ? "#" : "O")) + "      |";
    }

    Console.WriteLine($"  {n.Time,8:F3}s | {row}      |{refRow}");
}

return 0;

double ReadOption(string name, double fallback)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length && double.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
}

string? OutputPath(string option)
{
    var index = Array.IndexOf(args, option);
    if (index < 0) return null;
    if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
    {
        throw new ArgumentException($"{option} 后需要提供 JSON 输出路径。");
    }

    var path = Path.GetFullPath(args[index + 1]);
    if (!paths.Add(path))
    {
        throw new ArgumentException($"{option} 的输出路径不能与输入文件或另一份输出相同：{path}");
    }

    return path;
}

/// <summary>
/// 按 ADOFAI 的规则从 angleData / pathData 算每块砖的到达时间和转过的角度（只处理 SetSpeed、Twirl、Pause 和中旋），
/// 再交给 Core 的 FloorHits：哪些砖要按、转角怎么累加、转角信不信，和游戏里的 Mod 一样。
/// </summary>
internal static class AdofaiTiming
{
    // These events change presentation, not the outgoing floor interval used by this preview.
    private static readonly HashSet<string> PresentationEvents = new(StringComparer.Ordinal)
    {
        "AddDecoration", "AddText", "AddObject", "AddParticle", "AnimateTrack", "Bloom", "Bookmark",
        "Checkpoint", "ColorTrack", "CustomBackground", "EditorComment", "EmitParticle", "Flash",
        "HallOfMirrors", "Hide", "MoveCamera", "MoveDecorations", "MoveTrack", "PlaySound", "PositionTrack",
        "RecolorTrack", "ScalePlanets", "ScreenScroll", "ScreenTile", "SetDefaultText", "SetFilter",
        "SetFilterAdvanced", "SetFrameRate", "SetHitsound", "SetObject", "SetParticle", "SetPlanetRotation",
        "SetText", "ShakeScreen", "RepeatEvents", "SetConditionalEvents", "ScaleRadius",
    };

    /// <summary>pathData 的绝对方向字母（度）；'!' 是中旋。</summary>
    private static readonly Dictionary<char, double> PathAngles = new()
    {
        ['R'] = 0, ['p'] = 15, ['J'] = 30, ['E'] = 45, ['T'] = 60, ['o'] = 75, ['U'] = 90, ['q'] = 105, ['G'] = 120, ['Q'] = 135,
        ['H'] = 150, ['W'] = 165, ['L'] = 180, ['x'] = 195, ['N'] = 210, ['Z'] = 225, ['F'] = 240, ['V'] = 255, ['D'] = 270,
        ['Y'] = 285, ['B'] = 300, ['C'] = 315, ['M'] = 330, ['A'] = 345, ['!'] = 999,
    };

    /// <returns>需要按键的砖块、变速点（时间, 相对起始 BPM 的倍率）、起始一拍的秒数。</returns>
    /// <param name="path">给了就按每块砖的方向（angleData 的绝对角度，中旋原路折返）填上位置和时间，编辑器画轨道形状用；
    /// 按游戏默认的 1.5 单位砖距构造轨道，叠加 PositionTrack 的静态位置。</param>
    public static (List<TileHit> Hits, List<(double Time, double Speed)> Speeds, double Crotchet) Read(JObject root, List<PathPoint>? path = null)
    {
        if (root["angleData"] is JToken angleToken && angleToken is not JArray)
        {
            throw new InvalidDataException("angleData 必须是数组。");
        }

        var angles = root["angleData"]?.Select((x, i) => Number(x, $"angleData[{i}]")).ToArray()
            ?? ReadPathAngles(root);
        if (angles.Length == 0)
        {
            throw new InvalidDataException("关卡需要非空 angleData 或 pathData。");
        }

        var bpm = PositiveNumber(root["settings"]?["bpm"], "settings.bpm");
        if (root["actions"] is JToken actionToken && actionToken is not JArray)
        {
            throw new InvalidDataException("actions 必须是数组。");
        }

        var events = new Dictionary<int, List<JObject>>();
        foreach (var token in root["actions"] as JArray ?? new JArray())
        {
            if (token is not JObject e)
            {
                throw new InvalidDataException("actions 中每个事件必须是对象。");
            }

            var kind = (string?)e["eventType"] ?? "";
            if (PresentationEvents.Contains(kind))
            {
                continue;
            }

            if (kind is not ("SetSpeed" or "Twirl" or "Pause"))
            {
                throw new InvalidDataException($"离线预览不支持事件 {kind}；请从游戏内导出真实排键。");
            }

            var floor = Number(e["floor"], "事件 floor");
            if (floor < 0 || floor > angles.Length || floor != Math.Floor(floor))
            {
                throw new InvalidDataException("时序事件 floor 必须是有效的整数砖块序号。");
            }

            if (e["angleOffset"] is JToken offset && Number(offset, "angleOffset") != 0)
            {
                throw new InvalidDataException($"离线预览不支持 {kind} 的非零 angleOffset；请从游戏内导出。");
            }

            if ((kind != "Pause" && e["duration"] is JToken duration && Number(duration, "duration") != 0)
                || !string.IsNullOrEmpty((string?)e["eventTag"]))
            {
                throw new InvalidDataException($"离线预览不支持带 duration/eventTag 的 {kind}；请从游戏内导出。");
            }

            if (kind == "SetSpeed")
            {
                var speedType = (string?)e["speedType"] ?? "Bpm";
                if (speedType is not ("Multiplier" or "Bpm"))
                {
                    throw new InvalidDataException($"不支持的 SetSpeed.speedType：{speedType}。");
                }

                PositiveNumber(e[speedType == "Multiplier" ? "bpmMultiplier" : "beatsPerMinute"], "SetSpeed 速度");
            }
            else if (kind == "Pause" && e["duration"] is JToken pause)
            {
                Number(pause, "Pause.duration");
            }

            if (!events.TryGetValue((int)floor, out var list))
            {
                events[(int)floor] = list = new List<JObject>();
            }

            list.Add(e);
        }

        var version = (double?)root["settings"]?["version"] ?? 0;
        var pauseCorrections = LegacyPauseCorrections(angles, events, version);
        var floors = new List<FloorInfo>(angles.Length + 1);
        var speeds = new List<(double Time, double Speed)>();
        var time = 0.0;
        var clockwise = true;
        var current = bpm;
        var lastDir = 0.0;
        for (var k = 0; k < angles.Length; k++)
        {
            var pauseBeats = 0.0;
            foreach (var e in events.GetValueOrDefault(k) ?? new List<JObject>())
            {
                switch ((string?)e["eventType"])
                {
                    case "Twirl":
                        clockwise = !clockwise;
                        break;
                    case "SetSpeed" when (string?)e["speedType"] == "Multiplier":
                        current *= (double)e["bpmMultiplier"]!;
                        break;
                    case "SetSpeed":
                        current = (double)e["beatsPerMinute"]!;
                        break;
                    case "Pause":
                        var duration = (double?)e["duration"] ?? 0;
                        var correction = pauseCorrections[k];
                        if (correction != 0)
                        {
                            duration = version == 9 ? duration + correction : Math.Max(0, duration + correction);
                        }

                        pauseBeats += duration;
                        break;
                }
            }

            if (!double.IsFinite(current) || current <= 0 || !double.IsFinite(pauseBeats))
            {
                throw new InvalidDataException("变速或暂停计算结果无效；请从游戏内导出。");
            }

            if (speeds.Count == 0 || Math.Abs(speeds[^1].Speed - (current / bpm)) > 1e-9)
            {
                speeds.Add((time, current / bpm));
            }

            if (angles[k] == 999)
            {
                // 中旋：原地转 180°，不花时间；这块砖不用按（游戏里 midSpin 当自动砖）
                floors.Add(new FloorInfo(k, time, false, true, 0, current / bpm));
                lastDir = (lastDir + 180) % 360;
                continue;
            }

            var entry = (k > 0 ? lastDir : 0) + 180;
            var swept = clockwise ? Mod(entry - angles[k]) : Mod(angles[k] - entry);
            if (swept < 1e-6)
            {
                swept = 360;
            }

            floors.Add(new FloorInfo(k, time, false, false, swept, current / bpm));
            var interval = (swept / 180 + pauseBeats) * 60 / current;
            if (!double.IsFinite(interval) || interval < 0 || !double.IsFinite(time + interval))
            {
                throw new InvalidDataException("暂停导致时间倒序或时间计算溢出；请从游戏内导出。");
            }

            time += interval;
            lastDir = angles[k];
        }

        // angleData 有 N 项就有 N+1 块砖，最后一块（第 N 块）的到达也要按一下
        floors.Add(new FloorInfo(angles.Length, time, false, false));
        if (path != null)
        {
            const double tileSize = 1.5;
            double x = 0, y = 0, entryAngle = Math.PI;
            foreach (var floor in floors)
            {
                var k = floor.SeqId;
                var midspin = k < angles.Length && angles[k] == 999;
                var exitAngle = k == angles.Length ? entryAngle + Math.PI : midspin ? entryAngle : angles[k] * Math.PI / 180;
                path.Add(new PathPoint(k, x, y, floor.Time, false, entryAngle, exitAngle));
                // 中旋的下一块回到上一块中心，时间间隔仍为零。
                x += tileSize * Math.Cos(exitAngle);
                y += tileSize * Math.Sin(exitAngle);
                entryAngle = exitAngle + Math.PI;
            }

            ApplyTrackPositions(path, root["actions"] as JArray, tileSize);
        }

        return (FloorHits.From(floors, 60 / bpm), speeds, 60 / bpm);
    }

    private static void ApplyTrackPositions(List<PathPoint> path, JArray? actions, double tileSize)
    {
        // 前面的砖已摆好，后面的砖还在原位；相对位置按游戏逐砖处理的顺序求值。
        var events = (actions ?? new JArray()).OfType<JObject>()
            .Where(e => !Flag(e["editorOnly"]) && (e["active"] == null || Flag(e["active"])))
            .Where(e => (string?)e["eventType"] switch
            {
                "PositionTrack" => e["positionOffset"] is JArray { Count: 2 },
                "ScaleRadius" => e["scale"]?.Type is JTokenType.Float or JTokenType.Integer,
                _ => false,
            })
            .ToLookup(e => (int?)e["floor"]);
        double carryX = 0, carryY = 0;
        double radius = 1, radiusX = 0, radiusY = 0;
        for (var i = 0; i < path.Count; i++)
        {
            var floor = path[i];
            var dx = carryX;
            var dy = carryY;
            foreach (var e in events[i])
            {
                if ((string?)e["eventType"] == "ScaleRadius")
                {
                    radius = (double)e["scale"]! / 100;
                    continue;
                }

                var offset = (JArray)e["positionOffset"]!;
                dx += ((double?)offset[0] ?? 0) * tileSize;
                dy += ((double?)offset[1] ?? 0) * tileSize;
                if (e["relativeTo"] is JArray { Count: 2 } relative)
                {
                    var origin = (string?)relative[1] switch { "Start" => 0, "End" => path.Count - 1, _ => i };
                    var target = (int)Math.Clamp((long)origin + ((int?)relative[0] ?? 0), 0, path.Count - 1);
                    if (target != i)
                    {
                        dx += path[target].X - (floor.X + carryX);
                        dy += path[target].Y - (floor.Y + carryY);
                    }
                }

                if (!Flag(e["justThisTile"]))
                {
                    carryX = dx;
                    carryY = dy;
                }
            }

            // 半径按到达这一块的方向改变砖距。与 PositionTrack 的继承偏移分开累计。
            var extra = tileSize * (1 - radius);
            radiusX += extra * Math.Cos(floor.Entry);
            radiusY += extra * Math.Sin(floor.Entry);
            path[i] = floor with { X = floor.X + dx + radiusX, Y = floor.Y + dy + radiusY };
        }
    }

    private static bool Flag(JToken? value) => value?.Type == JTokenType.Boolean
        ? (bool)value : string.Equals((string?)value, "Enabled", StringComparison.OrdinalIgnoreCase);

    private static double Mod(double x) => ((x % 360) + 360) % 360;

    private static double[] ReadPathAngles(JObject root)
    {
        if (root["pathData"]?.Type != JTokenType.String || root["pathData"]!.ToString().Length == 0)
        {
            throw new InvalidDataException("关卡没有 angleData 或 pathData。");
        }

        var relative = new Dictionary<char, double>
        {
            ['5'] = 72, ['6'] = -72, ['7'] = 52, ['8'] = -52, ['9'] = -30,
            ['h'] = 120, ['j'] = -120, ['t'] = 60, ['y'] = 300,
        };
        var modern = (double?)root["settings"]?["version"] >= 5 && (bool?)root["settings"]?["legacySpriteTiles"] != true;
        var result = new List<double>();
        var previous = 0.0;
        foreach (var c in root["pathData"]!.ToString())
        {
            if (PathAngles.TryGetValue(c, out var absolute)) previous = absolute;
            else if (modern && relative.TryGetValue(c, out var delta)) previous = (float)(previous + delta);
            else throw new InvalidDataException($"pathData 里的「{c}」使用未支持的轨道语义；请从游戏内导出。");
            result.Add(previous);
        }

        return result.ToArray();
    }

    // Compatibility with the game's version 9–16 Pause migration. The core
    // outgoing-interval formula and FloorHits filtering remain the preview's own.
    private static int[] LegacyPauseCorrections(double[] angles, Dictionary<int, List<JObject>> events, double version)
    {
        var corrections = new int[angles.Length];
        if (version < 9 || version > 16) return corrections;
        const double pi = 3.1415927410125732, tau = 6.2831854820251465;
        const float radiansPerDegree = (float)pi / 180f;
        var entry = 4.71238899230957;
        var clockwise = true;
        for (var k = 0; k < angles.Length; k++)
        {
            var angle = (float)angles[k];
            var midspin = angle == 999f;
            var exit = midspin ? (double)(float)entry : (double)((0f - angle + 90f) * radiansPerDegree);
            var raw = (((exit - entry) * (clockwise ? 1 : -1)) % tau + tau) % tau;
            var turnaround = !midspin && (Math.Abs(raw) <= 1e-6 || Math.Abs(raw) >= 6.283184482025146);
            if (version == 9) corrections[k] = turnaround ? 1 : 0;
            else if (turnaround ^ (Math.Abs(raw - tau) < .0001)) corrections[k] = turnaround ? -1 : 1;
            entry = (exit + pi) % tau;
            foreach (var e in events.GetValueOrDefault(k + 1) ?? new List<JObject>())
                if ((string?)e["eventType"] == "Twirl") clockwise = !clockwise;
        }

        return corrections;
    }

    // Newtonsoft already accepts comments, trailing commas and raw characters in
    // strings. Some older charts additionally omit a comma after an array-valued
    // object property, or duplicate an object separator. Repair these boundaries outside strings.
    internal static JObject ParseJson(string text)
    {
        text = text.TrimStart('\uFEFF');
        try { return JObject.Parse(text); }
        catch (Newtonsoft.Json.JsonReaderException) { }
        var output = new System.Text.StringBuilder();
        var containers = new Stack<char>();
        var quoted = false;
        var escaped = false;
        var lineComment = false;
        var blockComment = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            output.Append(c);
            if (lineComment) { if (c is '\r' or '\n') lineComment = false; continue; }
            if (blockComment)
            {
                if (c == '*' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    output.Append(text[++i]); blockComment = false;
                }
                continue;
            }
            if (quoted)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') quoted = false;
                continue;
            }
            if (c == '/' && i + 1 < text.Length && text[i + 1] is '/' or '*')
            {
                lineComment = text[i + 1] == '/'; blockComment = !lineComment;
                output.Append(text[++i]); continue;
            }
            if (c == '"') quoted = true;
            else if (c == ',' && containers.TryPeek(out var container) && container == '{')
            {
                // 部分老谱在对象属性之间写了两个逗号（如 "requiredMods": [],,）。
                var next = i + 1;
                while (next < text.Length && char.IsWhiteSpace(text[next])) next++;
                if (next < text.Length && text[next] == ',') i = next;
            }
            else if (c is '[' or '{') containers.Push(c);
            else if (c is ']' or '}')
            {
                var opened = containers.Count > 0 ? containers.Pop() : '\0';
                if (c != ']' || opened != '[' || containers.Count == 0 || containers.Peek() != '{') continue;
                var next = i + 1;
                while (next < text.Length && char.IsWhiteSpace(text[next])) next++;
                if (next >= text.Length || text[next++] != '"') continue;
                var keyEscaped = false;
                while (next < text.Length)
                {
                    var key = text[next++];
                    if (keyEscaped) keyEscaped = false;
                    else if (key == '\\') keyEscaped = true;
                    else if (key == '"') break;
                }
                while (next < text.Length && char.IsWhiteSpace(text[next])) next++;
                if (next < text.Length && text[next] == ':') output.Append(',');
            }
        }

        return JObject.Parse(output.ToString());
    }

    private static double Number(JToken? token, string name)
    {
        if (token is null || (token.Type != JTokenType.Float && token.Type != JTokenType.Integer)
            || !double.IsFinite((double)token))
        {
            throw new InvalidDataException($"{name} 必须是有限数值。");
        }

        return (double)token;
    }

    private static double PositiveNumber(JToken? token, string name)
    {
        var value = Number(token, name);
        if (value <= 0)
        {
            throw new InvalidDataException($"{name} 必须大于零。");
        }

        return value;
    }
}
