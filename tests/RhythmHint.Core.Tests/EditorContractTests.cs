using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

/// <summary>
/// Mod（C#）和手序编辑器（JS）之间的文件约定：两份样例文件提交在 src/RhythmHint.Editor/tests/fixtures/。
/// session.json 由这里生成（Mod 交给编辑器的数据）；saved.json 由编辑器的 node 测试生成（编辑器改完存的手序），Mod 要能读回。
/// 格式改了时设环境变量 RHYTHMHINT_UPDATE_FIXTURES=1 重跑一次 C# 测试和 node 测试，再看 diff 提交。
/// </summary>
public class EditorContractTests
{
    internal static string Fixtures
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
            {
                dir = dir.Parent;
            }

            return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("找不到仓库根目录"), "src", "RhythmHint.Editor", "tests", "fixtures");
        }
    }

    /// <summary>固定的一小段谱：单押、双押、三押、长按，转角齐全。</summary>
    internal static EditorSession Sample()
    {
        var beats = new[] { 1.0, 0.5, 0.5, 0.0, 1.0, 0.25, 0.25, 0.25, 0.25, 1.0, 0.0, 0.0, 1.0, 0.5, 0.5, 1.0 };
        var hits = new List<TileHit>();
        var t = 2.0;
        for (var i = 0; i < beats.Length; i++)
        {
            t += beats[i] * 0.5;
            hits.Add(new TileHit(i + 1, t, i == 12 ? 0.75 : 0, beats[i] * 180));
        }

        var options = new FingeringOptions();
        var notes = NotePlanner.Plan(hits, options);
        var fingering = CustomFingering.From(hits, notes, leftHanded: false);
        fingering.Song = "Sample Song";
        fingering.Artist = "Sample Artist";
        fingering.Charter = "Sample Charter";
        fingering.UpdatedAt = new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);
        var fromAuto = notes.ConvertAll(_ => false);
        fromAuto[notes.Count - 1] = true;
        // 编辑器里能切换的另一种键数（6K，自动排键）
        var six = new FingeringOptions { Keys = 6 };
        var sixFingering = CustomFingering.From(hits, NotePlanner.Plan(hits, six), leftHanded: false, keys: 6);
        sixFingering.Song = fingering.Song;
        sixFingering.Artist = fingering.Artist;
        sixFingering.Charter = fingering.Charter;
        sixFingering.UpdatedAt = fingering.UpdatedAt;
        var variant = new SessionVariant
        {
            Id = SessionLibrary.IdOf(sixFingering),
            Fingering = sixFingering,
            FileName = sixFingering.SuggestedFileName(),
            KeyLabels = new[] { "S", "D", "F", "J", "K", "L" },
            Standard = NotePlanner.Plan(hits, six),
            Alternate = NotePlanner.Plan(hits, new FingeringOptions { Keys = 6, Style = FingeringStyle.ReviewPreferenceV1 }),
        };
        return new EditorSession
        {
            Variants = new[] { variant },
            Id = SessionLibrary.IdOf(fingering),
            Fingering = fingering,
            FromAuto = fromAuto,
            UsedFile = "Sample Artist - Sample Song.json",
            FileName = "Sample Artist - Sample Song.json",
            SaveFolder = @"C:\Game\Mods\RhythmHint\Fingerings",
            Language = "en",
            Standard = notes,
            Alternate = NotePlanner.Plan(hits, new FingeringOptions { Style = FingeringStyle.ReviewPreferenceV1 }),
            Beats = new[] { new BeatLine(2, true), new BeatLine(2.5, false), new BeatLine(3, false), new BeatLine(3.5, false), new BeatLine(4, true) },
            Tempo = new[] { (2.0, 120.0) },
            Path = hits.ConvertAll(h => new PathPoint(h.TileIndex, h.TileIndex, 0, h.Time, true)),
            Audio = null,
        };
    }

    [Fact]
    public void SessionFixtureIsCurrent()
    {
        var script = Sample().ToScript();
        var json = script.Substring("window.RHYTHMHINT_SESSION = ".Length).TrimEnd('\n').TrimEnd(';');
        var path = Path.Combine(Fixtures, "session.json");
        if (Environment.GetEnvironmentVariable("RHYTHMHINT_UPDATE_FIXTURES") == "1")
        {
            Directory.CreateDirectory(Fixtures);
            File.WriteAllText(path, json + "\n");
        }

        Assert.True(File.Exists(path), "缺 " + path + "：设 RHYTHMHINT_UPDATE_FIXTURES=1 重跑生成");
        Assert.Equal(File.ReadAllText(path).TrimEnd('\n').Replace("\r\n", "\n"), json);
    }

    /// <summary>编辑器「历史」的列表（recent/index.js）：样例两关，旧的有音乐、新的没有。</summary>
    [Fact]
    public void RecentFixtureIsCurrent()
    {
        var id = SessionLibrary.IdOf(Sample().Fingering);
        var json = SessionLibrary.ToJson(new[]
        {
            new RecentSession
            {
                Id = id, Song = "Sample Song", Artist = "Sample Artist", Charter = "Sample Charter", Notes = Sample().Fingering.Notes.Count,
                Session = id + ".js", Audio = id + "-20261118-090000-000.ogg", FileName = "Sample Artist - Sample Song.json", Exported = 1795050000000,
            },
            new RecentSession
            {
                Id = "8-0123456789abcdef01234567", Song = "<size=40>Second Song</size>", Artist = "Other", Notes = 8,
                Session = "8-0123456789abcdef01234567.js", FileName = "Other - Second Song.json", Exported = 1795053600000,
            },
        }).ToString(Newtonsoft.Json.Formatting.Indented).Replace("\r\n", "\n");
        var path = Path.Combine(Fixtures, "recent.json");
        if (Environment.GetEnvironmentVariable("RHYTHMHINT_UPDATE_FIXTURES") == "1")
        {
            File.WriteAllText(path, json + "\n");
        }

        Assert.True(File.Exists(path), "缺 " + path + "：设 RHYTHMHINT_UPDATE_FIXTURES=1 重跑生成");
        Assert.Equal(File.ReadAllText(path).TrimEnd('\n').Replace("\r\n", "\n"), json);
    }

    /// <summary>歌曲另存的脚本（audio-script.js）：0–255 每个字节一个，编辑器的 node 测试载入后要还原出同样的字节。</summary>
    [Fact]
    public void AudioScriptFixtureIsCurrent()
    {
        var bytes = new byte[256];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)i;
        }

        using var target = new MemoryStream();
        AudioScript.Write(new MemoryStream(bytes), target, "recent/sample.ogg");
        var script = System.Text.Encoding.ASCII.GetString(target.ToArray());
        var path = Path.Combine(Fixtures, "audio-script.js");
        if (Environment.GetEnvironmentVariable("RHYTHMHINT_UPDATE_FIXTURES") == "1")
        {
            File.WriteAllText(path, script);
        }

        Assert.True(File.Exists(path), "缺 " + path + "：设 RHYTHMHINT_UPDATE_FIXTURES=1 重跑生成");
        Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n"), script);
    }

    [Fact]
    public void ModReadsWhatTheEditorSaved()
    {
        var path = Path.Combine(Fixtures, "saved.json");
        Assert.True(File.Exists(path), "缺 " + path + "：先跑编辑器的 node 测试生成");

        var saved = CustomFingering.Parse(File.ReadAllText(path));
        var original = Sample().Fingering;

        Assert.Equal("Sample Song", saved.Song);
        Assert.Equal("Fixture Author", saved.Author); // node 测试里填的
        Assert.Equal(original.Hits.Count, saved.Hits.Count);
        Assert.Equal(original.Notes.Count, saved.Notes.Count);

        // node 测试把第一个音符（单押）改到了 D、把双押镜像了
        Assert.Equal(new[] { 0 }, saved.Notes[0].Lanes.Keys);
        var chord = original.Notes.FindIndex(n => n.Lanes.Count == 2);
        Assert.Equal(original.Notes[chord].Lanes.Keys.Select(l => 3 - l).OrderBy(l => l), saved.Notes[chord].Lanes.Keys);

        // 编辑器存的手序套回这一关：整首都用上
        var match = FingeringMatcher.Apply(saved, original.Hits, NotePlanner.Plan(original.Hits, new FingeringOptions()), leftHanded: false);
        Assert.Equal(1.0, match.Ratio);
    }
}
