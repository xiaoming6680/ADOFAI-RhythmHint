using RhythmHint.Core;
using RhythmHint.Reference;

namespace RhythmHint.Core.Tests;

/// <summary>
/// 6K（S D F J K L）、8K（A S D F J K L ;）的自动排键（2026-11-22 用户：不照搬 4K，参考网上多键的手法另写一套）：
/// 慢的右手食指、中速左右食指轮流；快连打整串分给两只手轮流、每只手一组从外往内滚；多押手指够就放在一只手上。
/// 提示怎么合和 4K 一样。4K 不走这套（4K 的结果由改动前后的转储逐字节比较守着）。
/// </summary>
public class ManyKeysTests
{
    /// <summary>均匀连打。转角写成正常的 180°：读不到转角时 35 ms 以内会按时间合成多押。</summary>
    private static List<TileHit> Run(int count, double gap, double start = 1, int firstTile = 1) =>
        Enumerable.Range(0, count).Select(i => new TileHit(firstTile + i, start + (i * gap), Angle: 180)).ToList();

    /// <summary>几块转角很小、挨得很近的砖：合成一个多押。</summary>
    private static List<TileHit> Chord(int count, double time, int firstTile) =>
        Enumerable.Range(0, count).Select(i => new TileHit(firstTile + i, time + (i * 0.005), Angle: i == 0 ? 180 : 15)).ToList();

    private static int[] LanesOf(IEnumerable<PlannedNote> notes) => notes.Select(n => n.Lanes.Keys.First()).ToArray();

    private static FingeringOptions Options(int keys, int mask = 0, FingeringStyle style = FingeringStyle.TimeReset) => new()
    {
        Keys = keys,
        Style = style,
        LeftHanded = (mask & 1) != 0,
        ChordsOnOtherHand = (mask & 2) != 0,
        UseRoll = (mask & 4) == 0,
        OuterRoll = (mask & 8) == 0,
    };

    [Fact]
    public void LayoutPutsFourKeysInTheMiddle()
    {
        // 每只手从中间往外：食指、中指、无名指、小指。4K 的 D F J K 在 6K、8K 里还是 D F J K
        Assert.Equal(new[] { 1, 0, 0, 1 }, Enumerable.Range(0, 4).Select(KeyLayout.Four.Finger));
        Assert.Equal(new[] { 2, 1, 0, 0, 1, 2 }, Enumerable.Range(0, 6).Select(KeyLayout.Six.Finger));
        Assert.Equal(new[] { 3, 2, 1, 0, 0, 1, 2, 3 }, Enumerable.Range(0, 8).Select(KeyLayout.Eight.Finger));
        Assert.Equal(new[] { 1, 2, 3, 4 }, Enumerable.Range(0, 4).Select(l => KeyLayout.Six.FromLayout(KeyLayout.Four, l)));
        Assert.Equal(new[] { 2, 3, 4, 5 }, Enumerable.Range(0, 4).Select(l => KeyLayout.Eight.FromLayout(KeyLayout.Four, l)));
        Assert.Equal(new[] { 0, 0, 0, 1, 1, 1 }, Enumerable.Range(0, 6).Select(KeyLayout.Six.HandOf));
        Assert.Equal(7, KeyLayout.Eight.Lane(1, 3));
        Assert.Equal(0, KeyLayout.Eight.Lane(0, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => KeyLayout.Of(5));
        Assert.Throws<ArgumentOutOfRangeException>(() => NotePlanner.Plan(Run(4, 0.1), new FingeringOptions { Keys = 5 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => NotePlanner.Plan(Run(4, 0.1), new FingeringOptions { Keys = 2 }));
    }

    /// <summary>
    /// 慢（隔得超过 R）右手食指；中速（R/2–R）左右食指轮流；R/2（65 ms）以内的长连打两只手轮流、每只手从外往内滚满：
    /// 6K L K J S D F，8K ; L K J A S D F。
    /// </summary>
    [Theory]
    [InlineData(6, 0.2, new[] { 3 })]
    [InlineData(8, 0.2, new[] { 4 })]
    [InlineData(6, 0.1, new[] { 3, 2 })]
    [InlineData(8, 0.1, new[] { 4, 3 })]
    [InlineData(6, 0.06, new[] { 5, 4, 3, 0, 1, 2 })]
    [InlineData(6, 0.02, new[] { 5, 4, 3, 0, 1, 2 })]
    [InlineData(8, 0.06, new[] { 7, 6, 5, 4, 0, 1, 2, 3 })]
    [InlineData(8, 0.02, new[] { 7, 6, 5, 4, 0, 1, 2, 3 })]
    public void LongRunsRollWholeHands(int keys, double gap, int[] cycle)
    {
        var lanes = LanesOf(NotePlanner.Plan(Run(24, gap), Options(keys)));

        Assert.Equal(Enumerable.Range(0, 24).Select(i => cycle[i % cycle.Length]), lanes);
    }

    /// <summary>短的快连打每只手用的手指少：两下左右手各一下，三四下每组两根手指，再长用满一只手；不够一组的从食指往外数。</summary>
    [Theory]
    [InlineData(6, 2, new[] { 3, 2 })]
    [InlineData(6, 3, new[] { 4, 3, 2 })]
    [InlineData(6, 4, new[] { 4, 3, 1, 2 })]
    [InlineData(6, 5, new[] { 5, 4, 3, 1, 2 })]
    [InlineData(6, 7, new[] { 5, 4, 3, 0, 1, 2, 3 })]
    [InlineData(8, 4, new[] { 5, 4, 2, 3 })]
    [InlineData(8, 6, new[] { 6, 5, 4, 1, 2, 3 })]
    [InlineData(8, 7, new[] { 7, 6, 5, 4, 1, 2, 3 })]
    public void ShortRunsUseFewerFingers(int keys, int count, int[] expected)
    {
        Assert.Equal(expected, LanesOf(NotePlanner.Plan(Run(count, 0.04), Options(keys))));
    }

    [Fact]
    public void RunStartsOnTheOtherHandAfterARecentNote()
    {
        // 右手食指刚按过（R 以内）：快连打从左手起
        var tiles = Run(1, 0.1).Concat(Run(4, 0.04, start: 1.1, firstTile: 2)).ToList();

        Assert.Equal(new[] { 3, 1, 2, 4, 3 }, LanesOf(NotePlanner.Plan(tiles, Options(6))));
    }

    [Fact]
    public void OuterRollOffRollsInnerFingerFirst()
    {
        var lanes = LanesOf(NotePlanner.Plan(Run(12, 0.03), Options(6, mask: 8)));

        Assert.Equal(Enumerable.Range(0, 12).Select(i => new[] { 3, 4, 5, 2, 1, 0 }[i % 6]), lanes);
    }

    [Fact]
    public void WithoutRollingOnlyIndexFingersAlternate()
    {
        // 轮指关掉：只用两根食指，6K、8K 也一样
        var lanes = LanesOf(NotePlanner.Plan(Run(12, 0.02), Options(8, mask: 4)));

        Assert.Equal(Enumerable.Range(0, 12).Select(i => i % 2 == 0 ? 4 : 3), lanes);
    }

    [Fact]
    public void AlternateStyleKeepsShortRunsOnIndexFingers()
    {
        // 交替优先：长度、间隔落在它的短串参数里（默认 4–5 下、60–65 ms）的快连打只用两根食指
        var tiles = Run(4, 0.062);

        Assert.Equal(new[] { 3, 2, 3, 2 }, LanesOf(NotePlanner.Plan(tiles, Options(6, style: FingeringStyle.ReviewPreferenceV1))));
        Assert.Equal(new[] { 4, 3, 1, 2 }, LanesOf(NotePlanner.Plan(tiles, Options(6))));
    }

    /// <summary>多押手指够就放在一只手上，从食指往外；不够的从另一只手的食指补。多押用手选另一只手时放左手。</summary>
    [Theory]
    [InlineData(6, 2, 0, new[] { 3, 4 })]
    [InlineData(6, 3, 0, new[] { 3, 4, 5 })]
    [InlineData(6, 4, 0, new[] { 2, 3, 4, 5 })]
    [InlineData(8, 3, 0, new[] { 4, 5, 6 })]
    [InlineData(8, 4, 0, new[] { 4, 5, 6, 7 })]
    [InlineData(6, 2, 2, new[] { 1, 2 })]
    [InlineData(6, 3, 2, new[] { 0, 1, 2 })]
    [InlineData(8, 4, 2, new[] { 0, 1, 2, 3 })]
    public void ChordsStayOnOneHandWhenFingersAllow(int keys, int count, int mask, int[] expected)
    {
        var notes = NotePlanner.Plan(Chord(count, 1, 1), Options(keys, mask));

        Assert.Equal(expected, Assert.Single(notes).Lanes.Keys);
    }

    [Fact]
    public void NotesNextToAChordUseTheOtherHand()
    {
        // 紧挨着多押的单押放另一只手的食指，让按多押的手专心按多押
        var tiles = Run(1, 0, start: 0.95).Concat(Chord(3, 1, 2)).Concat(Run(1, 0, start: 1.08, firstTile: 5)).ToList();
        var notes = NotePlanner.Plan(tiles, Options(6));

        Assert.Equal(new[] { 2 }, notes[0].Lanes.Keys);
        Assert.Equal(new[] { 3, 4, 5 }, notes[1].Lanes.Keys);
        Assert.Equal(new[] { 2 }, notes[2].Lanes.Keys);
    }

    /// <summary>
    /// 复现（2026-11-22 用户截图，Hello (BPM) 2023 第 34–53 秒）：双押后 15、15、30、30、30 ms 各一下再接双押，
    /// 原来右手刚按完 J+K，隔一下（30 ms）又要按 J。每根手指都要歇得比 R/2 久，也不出现孤立音符。
    /// </summary>
    [Theory]
    [InlineData(6, 0.045)]
    [InlineData(8, 0.065)]
    public void FingersRestBetweenChordsAndShortRuns(int keys, double minRest)
    {
        var tiles = new List<TileHit>();
        for (var cycle = 0; cycle < 12; cycle++)
        {
            var t = 1 + (cycle * 0.12);
            tiles.AddRange(Chord(2, t, tiles.Count + 1));
            foreach (var dt in new[] { 0.015, 0.03, 0.06, 0.09 })
            {
                tiles.Add(new TileHit(tiles.Count + 1, t + dt, Angle: 180));
            }
        }

        tiles.AddRange(Chord(2, 1 + (12 * 0.12), tiles.Count + 1)); // 和谱里一样以双押收尾
        var notes = NotePlanner.Plan(tiles, Options(keys));
        var free = new Dictionary<int, double>();
        foreach (var note in notes)
        {
            foreach (var lane in note.Lanes.Keys)
            {
                Assert.True(!free.TryGetValue(lane, out var t0) || note.Time - t0 >= minRest - 1e-6,
                    $"{keys}K 轨道 {lane} 在 {note.Time:F3}s 只歇了 {(note.Time - t0) * 1000:F0} ms");
                free[lane] = note.Time;
            }
        }

        Assert.Empty(LayoutStats.LoneNotes(notes, LayoutStats.DefaultLinkGap));
        Assert.All(notes.Where(n => n.Lanes.Count == 2), n => Assert.Single(n.Lanes.Keys.Select(KeyLayout.Of(keys).HandOf).Distinct()));
    }

    [Fact]
    public void MultitapTileGetsAllItsKeys()
    {
        // 一块要按 6 下的双押砖：4K 只有四个键按不过；6K 六个键全按，8K 右手四指加左手食指、中指
        var tiles = new List<TileHit> { new(1, 1, Angle: 180), new(2, 2, Angle: 180, Taps: 6, Multitap: true), new(3, 3, Angle: 180, Taps: 8, Multitap: true) };

        Assert.Equal(new[] { 0, 1, 2, 3 }, NotePlanner.Plan(tiles, Options(4))[1].Lanes.Keys);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, NotePlanner.Plan(tiles, Options(6))[1].Lanes.Keys);
        Assert.Equal(new[] { 2, 3, 4, 5, 6, 7 }, NotePlanner.Plan(tiles, Options(8))[1].Lanes.Keys);
        Assert.Equal(Enumerable.Range(0, 8), NotePlanner.Plan(tiles, Options(8))[2].Lanes.Keys);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, NotePlanner.Plan(tiles, Options(6))[2].Lanes.Keys);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(8)]
    public void LeftHandedIsTheMirror(int keys)
    {
        foreach (var hits in RandomCharts(12))
        {
            foreach (var mask in new[] { 0, 2, 8 })
            {
                var right = NotePlanner.Plan(hits, Options(keys, mask));
                var left = NotePlanner.Plan(hits, Options(keys, mask | 1));
                Assert.Equal(right.Count, left.Count);
                for (var i = 0; i < right.Count; i++)
                {
                    Assert.Equal(right[i].Lanes.Select(kv => (keys - 1 - kv.Key, kv.Value)).OrderBy(x => x.Item1), left[i].Lanes.Select(kv => (kv.Key, kv.Value)));
                }
            }
        }
    }

    /// <summary>
    /// 硬约束：提示个数、每个提示盖的砖和 4K 一样；单押一个键、长按跟着砖；双押同手。
    /// 连按、孤立音符加起来不比 4K 多（排法不同，个别谱上会多几个，整体要少）。
    /// </summary>
    [Theory]
    [InlineData(6)]
    [InlineData(8)]
    public void KeepsTheHardConstraints(int keys)
    {
        var total = new Tally();
        foreach (var hits in RandomCharts(40))
        {
            foreach (var style in Enum.GetValues<FingeringStyle>())
            {
                for (var mask = 0; mask < 16; mask++)
                {
                    total.Add(AssertConstraints(hits, Options(4, mask, style), Options(keys, mask, style)));
                }
            }
        }

        total.AssertNotWorse(keys);
    }

    /// <summary>本机的真实谱面（不进仓库）：工坊全部、TUF 校准谱 U10 以上，6K、8K 都守住硬约束。</summary>
    [Fact]
    public void RealChartsKeepTheHardConstraints()
    {
        var paths = new List<string>();
        var workshop = Environment.GetEnvironmentVariable("RHYTHMHINT_WORKSHOP");
        if (!string.IsNullOrEmpty(workshop) && Directory.Exists(workshop))
        {
            paths.AddRange(Directory.EnumerateFiles(workshop, "*.adofai", SearchOption.AllDirectories));
        }

        var tuf = Environment.GetEnvironmentVariable("RHYTHMHINT_TUF");
        if (!string.IsNullOrEmpty(tuf) && File.Exists(Path.Combine(tuf, "index.json")))
        {
            var index = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(tuf, "index.json")));
            paths.AddRange(index.Properties()
                .Where(p => DifficultyTiers.Parse((string?)p.Value["tier"]) >= DifficultyTiers.Parse("U10"))
                .Select(p => Path.Combine(tuf, "levels", p.Name + ".adofai"))
                .Where(File.Exists));
        }

        Assert.SkipWhen(paths.Count == 0, "需要本机谱面：未设置 RHYTHMHINT_WORKSHOP / RHYTHMHINT_TUF");
        foreach (var keys in new[] { 6, 8 })
        {
            var total = new Tally();
            foreach (var path in paths)
            {
                var hits = AdofaiLevelReader.Parse(File.ReadAllText(path)).Hits();
                foreach (var mask in new[] { 0, 1, 2, 8 })
                {
                    total.Add(AssertConstraints(hits, Options(4, mask), Options(keys, mask)));
                }
            }

            total.AssertNotWorse(keys);
        }
    }

    /// <summary>连按、孤立音符的个数：多键和 4K 各一份。</summary>
    private sealed class Tally
    {
        private int _jacks;
        private int _fourJacks;
        private int _lone;
        private int _fourLone;

        public void Add(Tally t)
        {
            _jacks += t._jacks;
            _fourJacks += t._fourJacks;
            _lone += t._lone;
            _fourLone += t._fourLone;
        }

        public void Count(List<PlannedNote> many, List<PlannedNote> four, FingeringOptions o)
        {
            _jacks += Jacks(many, o);
            _fourJacks += Jacks(four, o);
            _lone += LayoutStats.LoneNotes(many, LayoutStats.DefaultLinkGap).Count;
            _fourLone += LayoutStats.LoneNotes(four, LayoutStats.DefaultLinkGap).Count;
        }

        public void AssertNotWorse(int keys)
        {
            Assert.True(_jacks <= _fourJacks, $"{keys}K 的连按 {_jacks} 个，比 4K 的 {_fourJacks} 个多");
            Assert.True(_lone <= _fourLone, $"{keys}K 的孤立音符 {_lone} 个，比 4K 的 {_fourLone} 个多");
        }
    }

    private static Tally AssertConstraints(List<TileHit> hits, FingeringOptions fourOptions, FingeringOptions options)
    {
        var layout = KeyLayout.Of(options.Keys);
        var four = NotePlanner.Plan(hits, fourOptions);
        var many = NotePlanner.Plan(hits, options);
        Assert.Equal(four.Count, many.Count);
        for (var i = 0; i < many.Count; i++)
        {
            Assert.Equal(four[i].TileIndex, many[i].TileIndex);
            Assert.Equal(four[i].LastTileIndex, many[i].LastTileIndex);
            Assert.Equal(four[i].Time, many[i].Time);
            Assert.All(many[i].Lanes.Keys, lane => Assert.InRange(lane, 0, options.Keys - 1));
            if (!four[i].IsChord)
            {
                Assert.Equal(four[i].Lanes.Values.First(), Assert.Single(many[i].Lanes).Value);
                continue;
            }

            // 多押：键数和 4K 一样，超过 4 下的双押砖补到下数；长按一样；双押同手
            Assert.True(many[i].Lanes.Count >= four[i].Lanes.Count);
            Assert.Equal(four[i].Lanes.Values.OrderBy(v => v), many[i].Lanes.Values.OrderBy(v => v).Take(four[i].Lanes.Count));
            if (many[i].Lanes.Count == 2)
            {
                Assert.Single(many[i].Lanes.Keys.Select(layout.HandOf).Distinct());
            }
        }

        var tally = new Tally();
        tally.Count(many, four, options);
        return tally;
    }

    private static int Jacks(List<PlannedNote> notes, FingeringOptions o) =>
        Enumerable.Range(1, Math.Max(0, notes.Count - 1))
            .Count(i => notes[i].Time - notes[i - 1].Time < o.JackWindow - 1e-6 && notes[i].Lanes.Keys.Any(notes[i - 1].Lanes.ContainsKey));

    /// <summary>随机谱：各种间隔（含 R/4、R/2 两侧的快连打）、伪多押、长按、双押砖、读不到转角的砖。</summary>
    private static IEnumerable<List<TileHit>> RandomCharts(int count)
    {
        var gaps = new[] { 0.012, 0.018, 0.0216, 0.022, 0.025, 0.03, 0.0325, 0.033, 0.047, 0.06, 0.065, 0.066, 0.094, 0.12, 0.188, 0.3, 0.5 };
        for (var round = 0; round < count; round++)
        {
            var rng = new Random(2211 + round);
            var t = 0.0;
            var tiles = new List<TileHit>();
            var gap = gaps[rng.Next(gaps.Length)];
            for (var i = 0; i < 300; i++)
            {
                if (rng.Next(8) == 0)
                {
                    gap = gaps[rng.Next(gaps.Length)];
                }

                t += gap;
                var angle = rng.Next(4) == 0 ? double.NaN : 180.0;
                var hold = rng.Next(25) == 0 ? gap : 0;
                var taps = rng.Next(30) == 0 ? rng.Next(2, 9) : 1;
                tiles.Add(new TileHit(tiles.Count + 1, t, hold, angle, taps, taps > 1));
                if (rng.Next(12) == 0)
                {
                    tiles.Add(new TileHit(tiles.Count + 1, t + 0.01, 0, 15));
                }
            }

            yield return tiles;
        }
    }
}
