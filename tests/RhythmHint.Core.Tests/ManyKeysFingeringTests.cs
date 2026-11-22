using RhythmHint.Core;
using V131 = RhythmHint131.Core;

namespace RhythmHint.Core.Tests;

/// <summary>
/// 6K、8K 的专属手序（2026-11-22 用户）：文件格式不变（keys 早就有），键数和这一关显示的一样就照文件；
/// 键少的同一只手、同一根手指放进来；键多的不套用。自动选键数时听手序文件的键数。
/// </summary>
public class ManyKeysFingeringTests
{
    /// <summary>慢、中、快（R/4 以内，6K、8K 会多指轮）混着的一张谱，带一个双押、一块要按 6 下的双押砖。</summary>
    private static List<TileHit> Chart()
    {
        var hits = new List<TileHit>();
        var t = 1.0;
        void Add(double gap, int count)
        {
            for (var i = 0; i < count; i++)
            {
                hits.Add(new TileHit(hits.Count + 1, t, Angle: 180));
                t += gap;
            }
        }

        Add(0.3, 6);
        Add(0.1, 8);
        Add(0.02, 24);
        t += 0.5;
        hits.Add(new TileHit(hits.Count + 1, t, Angle: 180));
        hits.Add(new TileHit(hits.Count + 1, t + 0.01, Angle: 15));
        t += 0.6;
        hits.Add(new TileHit(hits.Count + 1, t, Angle: 180, Taps: 6, Multitap: true));
        t += 0.6;
        Add(0.25, 6);
        return hits;
    }

    private static List<PlannedNote> Plan(List<TileHit> hits, int keys, bool leftHanded = false) =>
        NotePlanner.Plan(hits, new FingeringOptions { Keys = keys, LeftHanded = leftHanded });

    private static CustomFingering File(List<TileHit> hits, int keys, bool leftHanded = false) =>
        CustomFingering.Parse(CustomFingering.From(hits, Plan(hits, keys, leftHanded), leftHanded, keys).ToJson());

    private static List<int[]> LanesOf(IEnumerable<PlannedNote> notes) => notes.Select(n => n.Lanes.Keys.ToArray()).ToList();

    [Theory]
    [InlineData(6)]
    [InlineData(8)]
    public void SixAndEightKeyFilesRoundTrip(int keys)
    {
        // 复现：From 没设键数，6K 的轨道 4、5 在 4 键文件里不合法，「编辑本关手序」导出时就抛异常
        var hits = Chart();
        var file = File(hits, keys);

        Assert.Equal(keys, file.Keys);
        Assert.Equal(LanesOf(Plan(hits, keys)), LanesOf(file.Notes));
        Assert.Contains(file.Notes, n => n.Lanes.Keys.Max() == keys - 1);
    }

    [Fact]
    public void FileNamesCarryTheKeyCount()
    {
        Assert.Equal("Artist - Song.json", new CustomFingering { Song = "Song", Artist = "Artist" }.SuggestedFileName());
        Assert.Equal("Artist - Song 6K.json", new CustomFingering { Song = "Song", Artist = "Artist", Keys = 6 }.SuggestedFileName());
        Assert.Equal("_CON 8K.json", new CustomFingering { Song = "CON", Keys = 8 }.SuggestedFileName());
        Assert.Equal(new string('x', 80) + " 6K.json", new CustomFingering { Song = new string('x', 90), Keys = 6 }.SuggestedFileName());
    }

    [Theory]
    [InlineData(6, false, false)]
    [InlineData(8, false, false)]
    [InlineData(8, true, false)]
    [InlineData(8, false, true)]
    public void FewerKeyFilesUseTheSameFingers(int keys, bool authorLeft, bool playerLeft)
    {
        // 4K 手序在 6K、8K 里就是中间四个键（D F J K 还是 D F J K）；作者和玩家惯用手不同时先按 4 键镜像再放进来
        var hits = Chart();
        var file = File(hits, 4, authorLeft);
        var match = FingeringMatcher.Apply(file, hits, Plan(hits, keys, playerLeft), playerLeft, keys);
        var four = FingeringMatcher.Apply(file, hits, Plan(hits, 4, playerLeft), playerLeft);
        var layout = KeyLayout.Of(keys);

        Assert.Equal(keys, match.Keys);
        Assert.Equal(hits.Count, match.MatchedHits);
        Assert.Equal(four.Notes.Count, match.Notes.Count);
        for (var i = 0; i < four.Notes.Count; i++)
        {
            var expected = four.Notes[i].Lanes.Keys.Select(l => layout.FromLayout(KeyLayout.Four, l)).ToList();
            if (hits.Single(h => h.TileIndex == four.Notes[i].TileIndex).Taps == 6)
            {
                // 6 下的双押砖：4 键文件只有四个键，按这一关的键数补够（这只手、另一只手的无名指）
                Assert.Equal(6, match.Notes[i].Lanes.Count);
                Assert.Superset(expected.ToHashSet(), match.Notes[i].Lanes.Keys.ToHashSet());
                continue;
            }

            Assert.Equal(expected, match.Notes[i].Lanes.Keys);
        }
    }

    [Fact]
    public void MoreKeyFilesAreNotUsed()
    {
        var hits = Chart();
        var file = File(hits, 8);
        var auto = Plan(hits, 6);

        Assert.False(FingeringMatcher.Fits(8, 6));
        Assert.False(FingeringMatcher.Fits(5, 8));
        Assert.True(FingeringMatcher.Fits(2, 4));
        var match = FingeringMatcher.Apply(file, hits, auto, false, 6);
        Assert.Equal(0, match.MatchedHits);
        Assert.Equal(LanesOf(auto), LanesOf(match.Notes));

        var choice = FingeringMatcher.Choose(new[] { new FingeringCandidate("Song 8K.json", file) }, hits, k => Plan(hits, k), false, null, 6, followFile: false);
        Assert.Null(choice.Used);
        Assert.Single(choice.OtherKeys);
    }

    [Fact]
    public void ManualKeysPreferAFileWithTheSameKeys()
    {
        // 同一张谱有 4K、6K 两份：手动选 6K 时先看 6K 那份；手动选 8K 时两份都放得进来、都不是 8 键，照旧让玩家选
        var hits = Chart();
        var candidates = new[] { new FingeringCandidate("Song.json", File(hits, 4)), new FingeringCandidate("Song 6K.json", File(hits, 6)) };

        var six = FingeringMatcher.Choose(candidates, hits, k => Plan(hits, k), false, null, 6, followFile: false);
        Assert.Equal("Song 6K.json", six.Used?.Name);
        Assert.Equal(6, six.Match!.Keys);

        var four = FingeringMatcher.Choose(candidates, hits, k => Plan(hits, k), false, null, 4, followFile: false);
        Assert.Equal("Song.json", four.Used?.Name);
        Assert.Equal("Song 6K.json", Assert.Single(four.OtherKeys).Name);

        var eight = FingeringMatcher.Choose(candidates, hits, k => Plan(hits, k), false, null, 8, followFile: false);
        Assert.Equal(FingeringReason.Unsure, eight.Reason);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void AutomaticKeysFollowTheFile(int byDifficulty)
    {
        // 自动模式：手序是几键就按几键显示（作者的本意），不管难度选的是几键；对不上的地方按文件的键数自动排
        var hits = Chart();
        var file = File(hits, 6);
        var asked = new List<int>();
        var choice = FingeringMatcher.Choose(new[] { new FingeringCandidate("Song 6K.json", file) }, hits,
            k => { asked.Add(k); return Plan(hits, k); }, false, null, byDifficulty, followFile: true);

        Assert.Equal(FingeringReason.Exact, choice.Reason);
        Assert.Equal(6, choice.Match!.Keys);
        Assert.Equal(LanesOf(file.Notes), LanesOf(choice.Match.Notes));
        Assert.All(asked, k => Assert.Equal(6, k));
    }

    [Fact]
    public void SavingANewKeyCountDoesNotOverwriteTheOtherFile()
    {
        // 同一关的 6K 手序存进文件夹，同名的是 4K 那份：另存「名字 (2).json」，不盖掉 4K 的（FingeringInbox.SaveName，编辑器 M.saveName 同一规则）
        var hits = Chart();
        var folder = Directory.CreateTempSubdirectory("rhythmhint-keys-").FullName;
        try
        {
            System.IO.File.WriteAllText(Path.Combine(folder, "Song.json"), File(hits, 4).ToJson());
            Assert.Equal("Song (2).json", FingeringInbox.SaveName(folder, "Song.json", File(hits, 6).ToJson()));
            Assert.Equal("Song.json", FingeringInbox.SaveName(folder, "Song.json", File(hits, 4).ToJson()));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData(6, false)]
    [InlineData(8, true)]
    public void ManyKeyFilesAreReadBy131(int keys, bool leftHanded)
    {
        // 1.3.1 收 1–16 键：读得进来、读出来的一样（它的匹配只套用 4 键的，所以不会用错）
        var hits = Chart();
        var json = CustomFingering.From(hits, Plan(hits, keys, leftHanded), leftHanded, keys).ToJson();
        var old = V131.CustomFingering.Parse(json);

        Assert.Equal(keys, old.Keys);
        Assert.Equal(leftHanded, old.LeftHanded);
        Assert.Equal(LanesOf(CustomFingering.Parse(json).Notes), old.Notes.Select(n => n.Lanes.Keys.ToArray()).ToList());
        Assert.Contains(old.Notes, n => n.Lanes.Count > 4);
    }

    [Fact]
    public void FilesWithoutKeysAreFourKeys()
    {
        var json = "{\"format\":\"rhythmhint-fingering\",\"schemaVersion\":1,\"hits\":[[1,1,0,180]],\"notes\":[[1,1,[[3,0]]]]}";

        Assert.Equal(4, CustomFingering.Parse(json).Keys);
        Assert.Equal(4, V131.CustomFingering.Parse(json).Keys);
    }
}
