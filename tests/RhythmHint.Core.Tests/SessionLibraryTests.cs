using System.Text;
using Newtonsoft.Json.Linq;
using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

/// <summary>
/// 编辑器的「历史」（2026-11-18 用户：改到一半关了网页，不开游戏很难再找到网页和之前的东西）：
/// 每次「编辑本关手序」另存一份到 recent/，同一份谱面只留最新的，最多留 10 关，旧文件删掉，被占用的留到下次。
/// </summary>
public class SessionLibraryTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("rhythmhint-editor-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Recent => Path.Combine(_folder, SessionLibrary.FolderName);

    /// <summary>一关：beat 不同谱面指纹就不同。</summary>
    private static EditorSession Level(string song, double beat = 0.5)
    {
        var hits = new List<TileHit>();
        for (var i = 0; i < 8; i++)
        {
            hits.Add(new TileHit(i + 1, 1 + (i * beat), 0, 180));
        }

        var fingering = CustomFingering.From(hits, NotePlanner.Plan(hits, new FingeringOptions()), leftHanded: false);
        fingering.Song = song;
        fingering.Artist = "Artist";
        return new EditorSession { Fingering = fingering, FileName = fingering.SuggestedFileName() };
    }

    /// <summary>像 Mod 一样：先拷音乐，再存会话。</summary>
    private EditorSession Export(EditorSession session, DateTimeOffset when)
    {
        session.Id = SessionLibrary.IdOf(session.Fingering);
        var audio = SessionLibrary.AudioFile(_folder, session.Id, ".OGG");
        File.WriteAllText(Path.Combine(_folder, audio), "music");
        session.Audio = new SessionAudio { File = audio };
        SessionLibrary.Save(_folder, session, when);
        return session;
    }

    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 9, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void ExportWritesTheCurrentLevelAndTheHistory()
    {
        var session = Export(Level("First"), T0);

        var current = File.ReadAllText(Path.Combine(_folder, "session.js"));
        Assert.Equal(current, File.ReadAllText(Path.Combine(Recent, session.Id + ".js")));
        Assert.Equal(session.Id, (string?)JObject.Parse(current.Substring("window.RHYTHMHINT_SESSION = ".Length).TrimEnd('\n', ';'))["id"]);

        var index = File.ReadAllText(Path.Combine(Recent, "index.js"));
        Assert.StartsWith("window.RHYTHMHINT_RECENT = ", index);
        var item = Assert.Single(SessionLibrary.Read(_folder));
        Assert.Equal(session.Id, item.Id);
        Assert.Equal("First", item.Song);
        Assert.Equal(session.Id + ".js", item.Session);
        Assert.Equal(session.Audio!.File, SessionLibrary.FolderName + "/" + item.Audio);
        Assert.EndsWith(".ogg", item.Audio);
        Assert.Equal(T0.ToUnixTimeMilliseconds(), item.Exported);
        Assert.Equal(session.Fingering.Notes.Count, item.Notes);
    }

    [Fact]
    public void TheSameChartKeepsOnlyItsNewestExport()
    {
        var first = Export(Level("First"), T0);
        Export(Level("Second", beat: 0.25), T0.AddMinutes(1));
        var again = Export(Level("First"), T0.AddMinutes(2));

        var items = SessionLibrary.Read(_folder);
        Assert.Equal(new[] { first.Id, SessionLibrary.IdOf(Level("Second", 0.25).Fingering) }, items.ConvertAll(i => i.Id));
        Assert.Equal(again.Audio!.File, SessionLibrary.FolderName + "/" + items[0].Audio);
        Assert.Equal(5, Directory.GetFiles(Recent).Length); // index.js + 两关各一个会话、一首音乐，第一关的旧音乐删掉了
    }

    /// <summary>歌曲另存的脚本（<see cref="AudioScript"/>）跟着它的音乐：留着时一起留，换新的时一起删。</summary>
    [Fact]
    public void TheSongScriptStaysAndGoesWithItsSong()
    {
        EditorSession ExportWithScript(DateTimeOffset when)
        {
            var session = Level("First");
            session.Id = SessionLibrary.IdOf(session.Fingering);
            var audio = SessionLibrary.AudioFile(_folder, session.Id, ".ogg");
            File.WriteAllText(Path.Combine(_folder, audio), "music");
            File.WriteAllText(Path.Combine(_folder, AudioScript.PathFor(audio)), "script");
            session.Audio = new SessionAudio { File = audio, Script = AudioScript.PathFor(audio) };
            SessionLibrary.Save(_folder, session, when);
            return session;
        }

        var first = ExportWithScript(T0);
        Assert.True(File.Exists(Path.Combine(_folder, first.Audio!.Script)));

        var again = ExportWithScript(T0.AddMinutes(1));
        Assert.NotEqual(first.Audio.File, again.Audio!.File);
        Assert.False(File.Exists(Path.Combine(_folder, first.Audio.File)));
        Assert.False(File.Exists(Path.Combine(_folder, first.Audio.Script)));
        Assert.True(File.Exists(Path.Combine(_folder, again.Audio.Script)));
        Assert.Equal(4, Directory.GetFiles(Recent).Length); // index.js、会话、音乐、音乐的脚本
    }

    [Fact]
    public void KeepsTenLevelsAndDeletesTheRest()
    {
        for (var i = 0; i < SessionLibrary.Keep + 2; i++)
        {
            Export(Level("Song " + i, beat: 0.1 + (i * 0.05)), T0.AddMinutes(i));
        }

        var items = SessionLibrary.Read(_folder);
        Assert.Equal(SessionLibrary.Keep, items.Count);
        Assert.Equal("Song " + (SessionLibrary.Keep + 1), items[0].Song);
        Assert.DoesNotContain(items, i => i.Song is "Song 0" or "Song 1");
        Assert.Equal(1 + (2 * SessionLibrary.Keep), Directory.GetFiles(Recent).Length);
    }

    [Fact]
    public void MusicStillPlayingInTheBrowserIsLeftForNextTime()
    {
        // 复现：旧的编辑器页面还开着、浏览器占着上一次的音乐文件时，删除失败不能让整个「编辑本关手序」打不开
        var first = Export(Level("First"), T0);
        var playing = Path.Combine(_folder, first.Audio!.File);
        using (new FileStream(playing, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var again = Export(Level("First"), T0.AddMinutes(1));
            Assert.NotEqual(first.Audio.File, again.Audio!.File);
        }

        Assert.True(File.Exists(playing));
        Export(Level("Second", beat: 0.25), T0.AddMinutes(2));
        Assert.False(File.Exists(playing)); // 不再被占用：下一次整理时删掉
    }

    [Fact]
    public void OldMusicNextToThePageIsCleanedUp()
    {
        // 以前的版本把音乐放在编辑器目录根上（session-audio-*）
        var old = Path.Combine(_folder, "session-audio-20261118-085612-180.ogg");
        File.WriteAllText(old, "old");

        var name = SessionLibrary.AudioFile(_folder, SessionLibrary.IdOf(Level("First").Fingering), ".Mp3");

        Assert.False(File.Exists(old));
        Assert.StartsWith(SessionLibrary.FolderName + "/", name);
        Assert.EndsWith(".mp3", name);
    }

    [Fact]
    public void ABrokenListDeletesNothing()
    {
        var first = Export(Level("First"), T0);
        File.WriteAllText(Path.Combine(Recent, "index.js"), "window.RHYTHMHINT_RECENT = {broken");

        Export(Level("Second", beat: 0.25), T0.AddMinutes(1));

        Assert.True(File.Exists(Path.Combine(Recent, first.Id + ".js"))); // 认不出哪些还有用：宁可留着
        Assert.Single(SessionLibrary.Read(_folder));
    }

    private const string IndexPrefix = "window.RHYTHMHINT_RECENT = ";

    /// <summary>
    /// 列表里某一项的字段类型不对（数组、对象）。复现（2026-11-19 评审）：Newtonsoft 强转时抛 ArgumentException，没接住，
    /// 每次「编辑本关手序」都抛、设置窗口被关掉、编辑器一直打不开。现在坏的那项跳过，别的照常；
    /// 这一次不删文件（认不出坏的那项用着哪些文件，宁可留着），下一次列表读得懂了再整理。
    /// </summary>
    [Fact]
    public void ABrokenEntryIsSkippedAndTheRestStillWork()
    {
        var a = Export(Level("A"), T0);
        var b = Export(Level("B", beat: 0.25), T0.AddMinutes(1));
        var c = Export(Level("C", beat: 0.3), T0.AddMinutes(2));
        var index = Path.Combine(Recent, "index.js");
        var json = JObject.Parse(File.ReadAllText(index).Substring(IndexPrefix.Length).TrimEnd('\n', ';'));
        var items = (JArray)json["items"]!;
        items.Single(i => (string?)i["id"] == b.Id)["song"] = new JArray("B");
        items.Add(new JObject { ["id"] = a.Id, ["session"] = a.Id + ".js", ["notes"] = new JObject { ["x"] = 1 }, ["exported"] = 5 });
        File.WriteAllText(index, IndexPrefix + json.ToString(Newtonsoft.Json.Formatting.None) + ";\n");

        Assert.Equal(new[] { c.Id, a.Id }, SessionLibrary.Read(_folder).Select(i => i.Id));

        var d = Export(Level("D", beat: 0.35), T0.AddMinutes(3));
        Assert.Equal(new[] { d.Id, c.Id, a.Id }, SessionLibrary.Read(_folder).Select(i => i.Id));
        Assert.True(File.Exists(Path.Combine(Recent, b.Id + ".js")));

        Export(Level("E", beat: 0.4), T0.AddMinutes(4));
        Assert.False(File.Exists(Path.Combine(Recent, b.Id + ".js"))); // 列表又读得懂了：不在列表里的整理掉
        Assert.Equal(4, SessionLibrary.Read(_folder).Count);
    }

    /// <summary>曲名恰好写成日期时间的样子，读回来还是原样（原来被 Newtonsoft 自动转成日期，再写成本机格式的日期）。</summary>
    [Fact]
    public void TextThatLooksLikeADateStaysAsWritten()
    {
        Export(Level("2026-11-18T09:00:00Z"), T0);

        Assert.Equal("2026-11-18T09:00:00Z", Assert.Single(SessionLibrary.Read(_folder)).Song);
    }

    [Fact]
    public void EntriesPointingOutsideTheFolderAreIgnored()
    {
        var first = Export(Level("First"), T0);
        var json = SessionLibrary.ToJson(new[]
        {
            new RecentSession { Id = first.Id, Session = "../../index.html", Exported = 1 },
            new RecentSession { Id = "../x", Session = "../x.js", Exported = 2 },
            new RecentSession { Id = first.Id, Session = first.Id + ".js", Audio = "../../game.exe", Exported = 3 },
        });
        File.WriteAllText(Path.Combine(Recent, "index.js"), "window.RHYTHMHINT_RECENT = " + json.ToString(Newtonsoft.Json.Formatting.None) + ";\n");

        Assert.Empty(SessionLibrary.Read(_folder));
        Assert.Throws<ArgumentException>(() => SessionLibrary.AudioFile(_folder, "../x", ".ogg"));
    }
}

/// <summary>
/// 桌面快捷方式（2026-11-18 用户要图标）：.lnk 按 MS-SHLLINK 写。路径里有中文时 .url 实测打不开、.html 启动页没有自己的图标；
/// 这样写出来的 .lnk 在本机用 WScript.Shell 读回目标、双击打开、外壳画出图标都实测过（见 docs/专属手序.md）。
/// </summary>
public class ShellLinkTests
{
    private const string Page = @"D:\!测试\游戏\A Dance of Fire and Ice\Mods\RhythmHint\Editor\index.html";
    private const string Icon = @"D:\!测试\游戏\A Dance of Fire and Ice\Mods\RhythmHint\Editor\icon.ico";

    [Fact]
    public void HeaderSaysItIsAShortcutWithTargetNameFolderAndIcon()
    {
        var lnk = ShellLink.Build(Page, Path.GetDirectoryName(Page)!, Icon, "RhythmHint 手序编辑器");

        Assert.Equal(0x4C, BitConverter.ToInt32(lnk, 0));
        Assert.Equal(new byte[] { 0x01, 0x14, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46 }, lnk.Skip(4).Take(16));
        Assert.Equal(0x01u | 0x02 | 0x04 | 0x10 | 0x40 | 0x80, BitConverter.ToUInt32(lnk, 20));
        Assert.Equal(0u, BitConverter.ToUInt32(lnk, lnk.Length - 4)); // 结束块
    }

    [Fact]
    public void EveryFolderIsInTheTargetChainWithItsUnicodeName()
    {
        var lnk = ShellLink.Build(Page, Path.GetDirectoryName(Page)!, Icon, "RhythmHint");

        // 盘符项目 25 字节，后面的名字不一定在偶数位置上，按字节找
        foreach (var part in new[] { "!测试", "游戏", "A Dance of Fire and Ice", "Mods", "RhythmHint", "Editor", "index.html" })
        {
            Assert.True(Has(lnk, part + "\0"), part); // 外壳项目里的名字（带结尾的 0）
        }

        Assert.True(Has(lnk, Page + "\0")); // LinkInfo 里的 Unicode 路径
        Assert.True(Has(lnk, Icon));
    }

    private static bool Has(byte[] bytes, string text)
    {
        var needle = Encoding.Unicode.GetBytes(text);
        for (var i = 0; i + needle.Length <= bytes.Length; i++)
        {
            if (bytes.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public void WithoutAnIconTheBrowserIconIsUsed()
    {
        var lnk = ShellLink.Build(Page, Path.GetDirectoryName(Page)!, null, "RhythmHint");
        Assert.Equal(0u, BitConverter.ToUInt32(lnk, 20) & 0x40);
    }

    [Fact]
    public void OnlyFullPathsWithADrive()
    {
        Assert.Throws<ArgumentException>(() => ShellLink.Build(@"Mods\RhythmHint\Editor\index.html", "Mods", null, "x"));
        Assert.Throws<ArgumentException>(() => ShellLink.Build(@"\\server\share\index.html", "x", null, "x"));
    }

    [Fact]
    public void SixAndEightKeySessionsAreKeptApart()
    {
        // 2026-11-22：同一张谱的 4K、6K 会话在历史里各留一份（编辑器的草稿也按这个 Id 分开）；4K 的 Id 和以前一样
        var hits = Enumerable.Range(0, 6).Select(i => new TileHit(i + 1, 1 + (i * 0.3), Angle: 180)).ToList();
        var four = CustomFingering.From(hits, NotePlanner.Plan(hits, new FingeringOptions()), false);
        var six = CustomFingering.From(hits, NotePlanner.Plan(hits, new FingeringOptions { Keys = 6 }), false, 6);

        Assert.Equal(CustomFingering.ChartPrint(hits), SessionLibrary.IdOf(four));
        Assert.Equal(CustomFingering.ChartPrint(hits) + "-6k", SessionLibrary.IdOf(six));
        var folder = Directory.CreateTempSubdirectory("rhythmhint-recent-").FullName;
        try
        {
            SessionLibrary.Save(folder, new EditorSession { Fingering = four }, DateTimeOffset.FromUnixTimeMilliseconds(1));
            SessionLibrary.Save(folder, new EditorSession { Fingering = six }, DateTimeOffset.FromUnixTimeMilliseconds(2));
            var index = File.ReadAllText(Path.Combine(folder, SessionLibrary.FolderName, "index.js"));
            Assert.Contains("\"id\":\"" + SessionLibrary.IdOf(six) + "\"", index, StringComparison.Ordinal);
            Assert.Contains("\"keys\":6", index, StringComparison.Ordinal);
            Assert.Contains("\"id\":\"" + SessionLibrary.IdOf(four) + "\"", index, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
