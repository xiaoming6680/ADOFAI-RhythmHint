using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml.Serialization;
using RhythmHint.Core.Tests.Frozen;

namespace RhythmHint.Core.Tests;

/// <summary>
/// 设置文件 Settings.xml 的兼容：已经在玩家手里的老文件原样能读、值不丢；现在写出的文件降级回 1.3.x 也能读；
/// 读失败时不整份重置（UMM 的 ModSettings.Load 会），先另存原文件再逐项读回。
/// <para>
/// 样例 fixtures/settings/v*/ 是按 v1.2.1、v1.3.0、v1.3.1 标签的 Settings.cs 写出来的（v1.3.2 只比 v1.3.1 多一项 ShowChordCount、默认值都没变，
/// 是在 v1.3.1 的样例里 ShowDifficulty 后面插这一项得到的）（default = 新装第一次启动后存的，
/// custom = 每一项都改成界面上调得出来的非默认值）：把那一版的 Settings.cs、FingeringOptions.cs 和一个空的 UMM 基类编在一起，
/// 用 XmlSerializer 和 UMM 一样经 StreamWriter 写出，根元素的命名空间声明换成游戏里 Mono 的顺序（对照过游戏目录里真实的文件）。
/// 只能加新版本的样例，不能改已有的：它们代表玩家手里已有的文件。
/// </para>
/// </summary>
public sealed class SettingsFileTests : IDisposable
{
    private static readonly string Root = FindRoot();
    private static readonly string Fixtures = Path.Combine(Root, "tests", "RhythmHint.Core.Tests", "fixtures", "settings");
    private static readonly DateTime Now = new(2026, 11, 19, 15, 30, 12);

    private readonly string _dir = Directory.CreateTempSubdirectory("rhythmhint-settings-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("找不到仓库根目录（Directory.Build.props）");
    }

    public static TheoryData<string> Samples => new(Directory.GetFiles(Fixtures, "*.xml", SearchOption.AllDirectories)
        .Select(f => Path.GetRelativePath(Fixtures, f).Replace('\\', '/')).OrderBy(f => f, StringComparer.Ordinal));

    private string Copy(string sample, Func<string, string>? change = null)
    {
        var text = File.ReadAllText(Path.Combine(Fixtures, sample));
        var path = Path.Combine(_dir, "Settings.xml");
        File.WriteAllText(path, change != null ? change(text) : text, new UTF8Encoding(false));
        return path;
    }

    /// <summary>根元素下每个子元素的文本（按出现顺序）。</summary>
    private static List<(string Name, string Value)> Elements(string xml) =>
        XDocument.Parse(xml).Root!.Elements().Select(e => (e.Name.LocalName, e.Value)).ToList();

    private static string Serialize<T>(T value)
    {
        var writer = new StringWriter();
        new XmlSerializer(typeof(T)).Serialize(writer, value);
        return writer.ToString();
    }

    [Fact]
    public void SamplesForEveryReleasedSettingsVersionArePresent()
    {
        // 每发一版改了 Settings 字段的版本，就在这里加一份样例（只加不改）
        var versions = Directory.GetDirectories(Fixtures).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "v1.2.1", "v1.3.0", "v1.3.1", "v1.3.2" }, versions);
        Assert.All(versions, v =>
        {
            Assert.True(File.Exists(Path.Combine(Fixtures, v!, "default.xml")));
            Assert.True(File.Exists(Path.Combine(Fixtures, v!, "custom.xml")));
        });
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void FilesFromEarlierVersionsReadWithoutLosingValues(string sample)
    {
        foreach (var crlf in new[] { false, true }) // 游戏写出来的是 CRLF，仓库里存的是 LF
        {
            var path = Copy(sample, crlf ? t => t.Replace("\n", "\r\n") : null);
            var load = SettingsFile.Load<Settings>(path, Now);

            Assert.Equal(SettingsReadResult.Read, load.Result);
            Assert.True(load.CanSave);
            Assert.Empty(Directory.GetFiles(_dir, "*.bad-*"));

            // 文件里的每一项读进来再写出去还是原样
            var written = Elements(Serialize(load.Value)).ToDictionary(e => e.Name, e => e.Value);
            foreach (var (name, value) in Elements(File.ReadAllText(Path.Combine(Fixtures, sample))))
            {
                Assert.True(written.TryGetValue(name, out var now), $"{sample}：{name} 没有了");
                Assert.True(value == now, $"{sample}：{name} 原来是「{value}」，读进来变成「{now}」");
            }

            // 界面上调得出来的值，读完修正时一个都不动
            Assert.Empty(load.Value.Normalize());
        }
    }

    [Theory]
    [InlineData("v1.3.0/default.xml", typeof(Settings130))]
    [InlineData("v1.3.0/custom.xml", typeof(Settings130))]
    [InlineData("v1.3.1/default.xml", typeof(Settings131))]
    [InlineData("v1.3.1/custom.xml", typeof(Settings131))]
    [InlineData("v1.3.2/default.xml", typeof(Settings132))]
    [InlineData("v1.3.2/custom.xml", typeof(Settings132))]
    public void SamplesAreExactlyWhatThatVersionWrites(string sample, Type frozen)
    {
        // 样例本身对不对：交给那一版的字段读进来、再写出去，逐项一样，字段也一个不多一个不少
        var text = File.ReadAllText(Path.Combine(Fixtures, sample));
        var serializer = new XmlSerializer(frozen);
        var value = serializer.Deserialize(new StringReader(text));
        var writer = new StringWriter();
        serializer.Serialize(writer, value);

        Assert.Equal(Elements(text), Elements(writer.ToString()));
    }

    public static TheoryData<Type> OldReaders => new() { typeof(Settings130), typeof(Settings131), typeof(Settings132) };

    [Theory]
    [MemberData(nameof(OldReaders))]
    public void WhatThisVersionWritesOldVersionsStillRead(Type frozen)
    {
        // 玩家降级回 1.3.x：1.3.x 用 UMM 的 ModSettings.Load（XmlSerializer）整份读，有一项读不了就全部重置。
        // 这一版能写出的每一种排法都要是 1.3.x 认识的值；新排法要另存新字段
        var samples = new List<Settings> { new(), SettingsFile.Load<Settings>(Copy("v1.3.1/custom.xml"), Now).Value };
        foreach (var style in Enum.GetValues<FingeringStyle>())
        {
            samples.Add(new Settings { Style = style });
        }

        var serializer = new XmlSerializer(frozen);
        foreach (var settings in samples)
        {
            var text = Serialize(settings);
            var old = serializer.Deserialize(new StringReader(text));
            Assert.NotNull(old);

            var read = Elements(Serialize2(serializer, old!)).ToDictionary(e => e.Name, e => e.Value);
            foreach (var (name, value) in Elements(text).Where(e => read.ContainsKey(e.Name)))
            {
                Assert.True(read[name] == value, $"{frozen.Name} 读 {name}：写的是「{value}」，读成「{read[name]}」");
            }
        }

        static string Serialize2(XmlSerializer serializer, object value)
        {
            var writer = new StringWriter();
            serializer.Serialize(writer, value);
            return writer.ToString();
        }
    }

    [Fact]
    public void ChordCountIsOnForFilesWrittenBeforeItExisted()
    {
        // 多押数字开关是 1.3.1 之后加的：老文件里没有这一项，读进来是默认的开着；关掉后写出的文件 1.3.1 照样整份读得进去
        var load = SettingsFile.Load<Settings>(Copy("v1.3.1/custom.xml"), Now);
        Assert.Equal(SettingsReadResult.Read, load.Result);
        Assert.True(load.Value.ShowChordCount);

        var off = Serialize(new Settings { ShowChordCount = false });
        Assert.Contains("<ShowChordCount>false</ShowChordCount>", off, StringComparison.Ordinal);
        Assert.NotNull(new XmlSerializer(typeof(Settings131)).Deserialize(new StringReader(off)));
    }

    [Fact]
    public void ElementNamesAndOrderOnlyGrow()
    {
        // 现在写出的元素 = 已发布各版的元素按原顺序都在，新加的只能接在后面或插在中间，不能少、不能改名
        var current = Elements(Serialize(new Settings())).Select(e => e.Name).ToList();
        foreach (var version in new[] { "v1.3.1", "v1.3.2" })
        {
            var released = Elements(File.ReadAllText(Path.Combine(Fixtures, version, "default.xml"))).Select(e => e.Name).ToList();
            Assert.Equal(released, current.Where(released.Contains).ToList());
        }
    }

    [Fact]
    public void TheModHalfOfSettingsAddsNoStoredMembers()
    {
        // Settings 的另一半在 Mod 里（UMM 基类、保存），测试看不到：存进文件的字段只能写在 Core 的 SettingsFields.cs
        var text = File.ReadAllText(Path.Combine(Root, "src", "RhythmHint.Mod", "Settings.cs"));
        var stored = Regex.Matches(text, @"^\s*public\s+(?!override\b|sealed\b|partial\b|static\b|class\b)[\w<>?\[\],\s]+?\s+\w+\s*(=|;|\{)", RegexOptions.Multiline);

        Assert.Empty(stored.Select(m => m.Value.Trim()));
    }

    [Theory]
    [InlineData("<Style>ReviewPreferenceV1</Style>", "<Style>BeatAlternate</Style>", "Style")] // 降级后读到新版的排法、或删掉的排法
    [InlineData("<ResetWindowMs>180</ResetWindowMs>", "<ResetWindowMs>110.5</ResetWindowMs>", "ResetWindowMs")] // 以后 int 改成小数再降级
    [InlineData("<LeftHanded>true</LeftHanded>", "<LeftHanded>yes</LeftHanded>", "LeftHanded")] // 手改
    [InlineData("<UiScale>1.25</UiScale>", "<UiScale>1,5</UiScale>", "UiScale")] // 逗号小数
    public void OneBadValueOnlyResetsThatValue(string from, string to, string field)
    {
        // 复现：UMM 的 ModSettings.Load 遇到这些整份读失败、返回全默认的设置，接着保存就把玩家的文件覆盖了
        var path = Copy("v1.3.1/custom.xml", t => t.Replace(from, to));
        var original = File.ReadAllBytes(path);
        Assert.ThrowsAny<InvalidOperationException>(() => new XmlSerializer(typeof(Settings)).Deserialize(new MemoryStream(original)));

        var load = SettingsFile.Load<Settings>(path, Now);

        Assert.Equal(SettingsReadResult.Recovered, load.Result);
        Assert.Equal(new[] { field }, load.Unread);
        Assert.False(load.Truncated);
        Assert.True(load.CanSave);
        Assert.Equal(Path.Combine(_dir, "Settings.xml.bad-20261119-153012"), load.BadCopy);
        Assert.Equal(original, File.ReadAllBytes(load.BadCopy));
        Assert.Equal(original, File.ReadAllBytes(path)); // 读的时候不改原文件

        // 读不了的那一项是默认值，其余照样读回来（包括各谱选过的手序）
        var expected = SettingsFile.Load<Settings>(Copy("v1.3.1/custom.xml"), Now).Value;
        var defaults = new Settings();
        foreach (var f in typeof(Settings).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            Assert.Equal(f.GetValue(f.Name == field ? defaults : expected), f.GetValue(load.Value));
        }
    }

    [Fact]
    public void UnknownElementsAreSimplySkipped()
    {
        // 新版加的字段、老版删掉的字段：XmlSerializer 本来就跳过，不算读失败，也不另存
        var path = Copy("v1.3.1/custom.xml", t => t.Replace("<ShowPreview>", "<FutureField><Nested>1</Nested></FutureField>\n  <ShowPreview>"));

        var load = SettingsFile.Load<Settings>(path, Now);

        Assert.Equal(SettingsReadResult.Read, load.Result);
        Assert.Equal("S D K L", load.Value.KeyLabels);
        Assert.Empty(Directory.GetFiles(_dir, "*.bad-*"));
    }

    [Fact]
    public void AFileCutOffHalfwayKeepsWhatCameBefore()
    {
        // 写到一半断电、磁盘满：坏掉之前的设置读回来，之后的用默认值
        var text = File.ReadAllText(Path.Combine(Fixtures, "v1.3.1", "custom.xml"));
        var path = Copy("v1.3.1/custom.xml", t => t.Substring(0, t.IndexOf("<JackWindowMs>", StringComparison.Ordinal) + 6));

        var load = SettingsFile.Load<Settings>(path, Now);

        Assert.Equal(SettingsReadResult.Recovered, load.Result);
        Assert.True(load.Truncated);
        Assert.Empty(load.Unread);
        Assert.True(File.Exists(load.BadCopy));
        Assert.False(load.Value.ShowPreview);
        Assert.Equal("S D K L", load.Value.KeyLabels);
        Assert.Equal(300, load.Value.ChordFocusMs);
        Assert.Equal(150, load.Value.JackWindowMs); // 坏在这一项中间
        Assert.Equal(string.Empty, load.Value.FingeringChoices);
        Assert.Contains("<JackWindowMs>", text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not xml at all")]
    [InlineData("<Settings xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:nil=\"true\" />")]
    public void GarbageGivesDefaultsButKeepsTheFile(string text)
    {
        var path = Path.Combine(_dir, "Settings.xml");
        File.WriteAllText(path, text);

        var load = SettingsFile.Load<Settings>(path, Now);

        Assert.Equal(SettingsReadResult.Recovered, load.Result);
        Assert.True(load.CanSave);
        Assert.Equal(text, File.ReadAllText(load.BadCopy));
        Assert.Equal(new Settings().KeyLabels, load.Value.KeyLabels);
    }

    [Fact]
    public void ABadCopyNeverOverwritesAnEarlierOne()
    {
        var path = Copy("v1.3.1/custom.xml", t => t.Replace("<LeftHanded>true</LeftHanded>", "<LeftHanded>yes</LeftHanded>"));
        File.WriteAllText(path + ".bad-20261119-153012", "earlier");
        File.WriteAllText(path + ".bad-20261119-153012-2", "earlier 2");

        var load = SettingsFile.Load<Settings>(path, Now);

        Assert.Equal(path + ".bad-20261119-153012-3", load.BadCopy);
        Assert.Equal("earlier", File.ReadAllText(path + ".bad-20261119-153012"));
        Assert.Equal("earlier 2", File.ReadAllText(path + ".bad-20261119-153012-2"));
    }

    [Fact]
    public void WithoutABadCopyNothingMaySaveOverTheFile()
    {
        // 另存不了（这里用同名文件夹挡住）：照样逐项读回，但不能保存，否则原文件就没了
        var path = Copy("v1.3.1/custom.xml", t => t.Replace("<LeftHanded>true</LeftHanded>", "<LeftHanded>yes</LeftHanded>"));
        Directory.CreateDirectory(path + ".bad-20261119-153012");

        var load = SettingsFile.Load<Settings>(path, Now);

        Assert.Equal(SettingsReadResult.Recovered, load.Result);
        Assert.Equal(string.Empty, load.BadCopy);
        Assert.NotEqual(string.Empty, load.BackupError);
        Assert.False(load.CanSave);
        Assert.Equal("S D K L", load.Value.KeyLabels);
    }

    [Fact]
    public void ALockedFileIsLeftAloneAndNotSavedOver()
    {
        var path = Copy("v1.3.1/custom.xml");
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var load = SettingsFile.Load<Settings>(path, Now);

            Assert.Equal(SettingsReadResult.Unreadable, load.Result);
            Assert.False(load.CanSave);
            Assert.NotEqual(string.Empty, load.Error);
        }

        Assert.Empty(Directory.GetFiles(_dir, "*.bad-*"));
    }

    [Fact]
    public void NoFileMeansDefaults()
    {
        var load = SettingsFile.Load<Settings>(Path.Combine(_dir, "Settings.xml"), Now);

        Assert.Equal(SettingsReadResult.Missing, load.Result);
        Assert.True(load.CanSave);
        Assert.True(load.Value.MigrateDefaults()); // 新装：第一次启动记下默认值版本
    }

    [Theory]
    [InlineData("v1.2.1/default.xml", 130, true)] // 1.2.x 的老默认 165 换成新默认
    [InlineData("v1.2.1/custom.xml", 180, true)] // 自己调过的不动
    [InlineData("v1.3.0/default.xml", 130, false)]
    [InlineData("v1.3.1/custom.xml", 180, false)]
    public void OldDefaultsMigrateOnce(string sample, int reset, bool migrates)
    {
        var settings = SettingsFile.Load<Settings>(Copy(sample), Now).Value;

        Assert.Equal(migrates, settings.MigrateDefaults());
        Assert.Equal(reset, settings.ResetWindowMs);
        Assert.False(settings.MigrateDefaults());
    }

    // ---------- 读完修正（Normalize） ----------

    [Fact]
    public void RaisingReviewMinimumsPastMaximumsNeverStopsTheMod()
    {
        // 复现（1.3.5 玩家截图）：设置窗口里先把最长间隔拖到最低，再点最短间隔的加号，最长间隔的滑块没动、不会跟着推上去，
        // 排键抛「审核偏好短串最小间隔不能大于最大间隔」，整个 Mod 停用
        var s = new Settings
        {
            Style = FingeringStyle.ReviewPreferenceV1,
            ReviewShortMinGapMs = 61,
            ReviewShortMaxGapMs = 60f,
            ReviewShortMinNotes = 6,
            ReviewShortMaxNotes = 5,
        };
        var hits = Enumerable.Range(0, 40).Select(i => new TileHit(i + 1, 1 + (i * 0.07))).ToList();

        NotePlanner.Plan(hits, s.ToFingering());

        s.KeepReviewRangesOrdered();
        Assert.Equal(61, s.ReviewShortMinGapMs);
        Assert.Equal(61f, s.ReviewShortMaxGapMs);
        Assert.Equal(6, s.ReviewShortMinNotes);
        Assert.Equal(6, s.ReviewShortMaxNotes);
    }

    [Fact]
    public void NormalizeMakesBrokenNumbersSafeForThePlanner()
    {
        // 复现：交替优先的参数不合法（手改 Settings.xml、以后改了滑块范围再降级）时排键抛 ArgumentException，整个 Mod 停用；
        // 标准排法的玩家点「编辑本关手序」也会抛（编辑器两种排法都排一遍）
        var broken = new Settings
        {
            Style = FingeringStyle.ReviewPreferenceV1,
            ReviewShortMinGapMs = 90,
            ReviewShortMaxGapMs = 40.5f,
            ReviewShortMinNotes = 9,
            ReviewShortMaxNotes = 3,
            ReviewTimingToleranceMs = float.NaN,
            ResetWindowMs = -5,
            JackWindowMs = 100000,
            UiScale = float.PositiveInfinity,
            PanelOpacity = -1,
            KeyLabels = null!,
            SettingsHotkey = null!,
            FingeringChoices = null!,
        };
        var hits = Enumerable.Range(0, 40).Select(i => new TileHit(i + 1, 1 + (i * 0.07))).ToList();
        Assert.ThrowsAny<ArgumentException>(() => NotePlanner.Plan(hits, broken.ToFingering()));

        var changed = broken.Normalize();

        Assert.NotEmpty(changed);
        Assert.True(broken.ReviewShortMinGapMs <= broken.ReviewShortMaxGapMs);
        Assert.True(broken.ReviewShortMinNotes <= broken.ReviewShortMaxNotes);
        Assert.Equal(1f, broken.ReviewTimingToleranceMs); // 不是有限数：换回默认值
        Assert.Equal(100, broken.ResetWindowMs);
        Assert.Equal(300, broken.JackWindowMs);
        Assert.Equal(1f, broken.UiScale);
        Assert.Equal(0f, broken.PanelOpacity); // 2026-11-22 下限从 30% 改成 0
        Assert.Equal("D F J K", broken.KeyLabels);
        Assert.Equal("F8", broken.SettingsHotkey);
        Assert.Equal(string.Empty, broken.FingeringChoices);
        foreach (var style in Enum.GetValues<FingeringStyle>())
        {
            broken.Style = style;
            NotePlanner.Plan(hits, broken.ToFingering());
        }

        Assert.Empty(broken.Normalize()); // 修过一次就不用再修
    }

    [Fact]
    public void RandomValuesNeverBreakThePlannerAfterNormalize()
    {
        var random = new Random(20261119);
        var ints = new[] { int.MinValue, -1, 0, 1, 2, 3, 35, 60, 149, 150, 151, 300, 801, int.MaxValue };
        var floats = new[] { float.NaN, float.NegativeInfinity, -1f, 0f, 0.5f, 34.5f, 72.5f, 150f, 151f, float.MaxValue, float.PositiveInfinity };
        var hits = Enumerable.Range(0, 30).Select(i => new TileHit(i + 1, 1 + (i * 0.055))).ToList();
        for (var round = 0; round < 300; round++)
        {
            var s = new Settings { Style = FingeringStyle.ReviewPreferenceV1 };
            foreach (var f in typeof(Settings).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.FieldType == typeof(int))
                {
                    f.SetValue(s, ints[random.Next(ints.Length)]);
                }
                else if (f.FieldType == typeof(float))
                {
                    f.SetValue(s, floats[random.Next(floats.Length)]);
                }
            }

            s.Normalize();
            NotePlanner.Plan(hits, s.ToFingering());
            Assert.Empty(s.Normalize());
        }
    }

    [Fact]
    public void SliderRangesMatchTheSettingsWindow()
    {
        // Normalize 按 Settings.SliderRanges 夹，设置窗口的滑块各写各的数：两边要一致（范围跟着别的设置变的只比写死的那一头）
        var code = string.Concat(Directory.GetFiles(Path.Combine(Root, "src", "RhythmHint.Mod"), "SettingsWindow*.cs").Select(File.ReadAllText));
        var found = new Dictionary<string, (string Min, string Max)>();
        foreach (Match m in Regex.Matches(code, @"\bs\.(\w+),\s*([^,()]+(?:\([^()]*\))?),\s*([^,()]+(?:\([^()]*\))?),\s*[\d.]+f?,"))
        {
            found[m.Groups[1].Value] = (m.Groups[2].Value.Trim(), m.Groups[3].Value.Trim());
        }

        static double? Literal(string text) =>
            double.TryParse(text.TrimEnd('f'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;

        Assert.Equal(Settings.SliderRanges.Keys.Order(StringComparer.Ordinal), found.Keys.Where(Settings.SliderRanges.ContainsKey).Order(StringComparer.Ordinal));
        foreach (var (name, (min, max)) in found.Where(kv => Settings.SliderRanges.ContainsKey(kv.Key)))
        {
            var range = Settings.SliderRanges[name];
            if (Literal(min) is { } lo)
            {
                Assert.True(lo == range.Min, $"{name} 的滑块下限 {lo}，Settings.SliderRanges 是 {range.Min}");
            }

            if (Literal(max) is { } hi)
            {
                Assert.True(hi == range.Max, $"{name} 的滑块上限 {hi}，Settings.SliderRanges 是 {range.Max}");
            }
        }
    }

    // ---------- 排键设置的指纹 ----------

    [Fact]
    public void FingeringKeyChangesWithEverythingThatChangesTheFingering()
    {
        // 每帧都比 FingeringKey：只在设置变了时重新拼。ToFingering 用到的每一项、定这一关按几键的设置（各档位、估不出难度时）
        // 和专属手序开关变了，指纹都要变。手动选的键数在自动模式下用不上，也算进去（切回手动时不用再改别的）
        static string Options(Settings s) => string.Join("|", typeof(FingeringOptions).GetProperties().Select(p => $"{p.Name}={p.GetValue(s.ToFingering())}"))
            + "|" + string.Join(",", Enumerable.Range(0, 62).Select(tier => s.KeyCountFor(tier)));

        foreach (var f in typeof(Settings).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            var s = new Settings();
            var key = s.FingeringKey;
            var options = Options(s);
            object changed = f.FieldType == typeof(bool) ? !(bool)f.GetValue(s)!
                : f.FieldType == typeof(int) ? (int)f.GetValue(s)! + 7
                : f.FieldType == typeof(float) ? (float)f.GetValue(s)! + 7.5f
                : f.FieldType == typeof(string) ? f.GetValue(s) + "x"
                : f.FieldType == typeof(FingeringStyle) ? FingeringStyle.ReviewPreferenceV1
                : throw new InvalidOperationException($"测试不认识 {f.Name} 的类型 {f.FieldType}");
            f.SetValue(s, changed);

            if (Options(s) != options || f.Name is nameof(Settings.UseCustomFingering) or nameof(Settings.KeyCount))
            {
                Assert.True(s.FingeringKey != key, $"{f.Name} 改了排键结果，FingeringKey 却没变");
            }
            else
            {
                Assert.Same(key, s.FingeringKey);
            }
        }
    }

    [Fact]
    public void FingeringKeyDoesNotAllocateWhenNothingChanged()
    {
        var s = new Settings();
        var key = s.FingeringKey;
        var same = true;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            same &= ReferenceEquals(key, s.FingeringKey);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(same);
        Assert.True(allocated < 1000, $"1000 次 FingeringKey 分配了 {allocated} 字节");
    }
}
