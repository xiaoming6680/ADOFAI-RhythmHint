using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

/// <summary>
/// 双手稳拍、时间稳拍的 6K、8K（2026-11-26 用户：以 Linxi-1105 的风格做 6K、8K）：每一下的手照他的 4K，
/// 只有同一只手连着按的快单押改用这只手更多的手指，而且附近的孤立音符、连按不比他的 4K 多。
/// </summary>
public class SteadyManyKeysTests
{
    private static readonly FingeringStyle[] Styles = { FingeringStyle.SteadyHands, FingeringStyle.ReviewedTime };

    private static FingeringOptions Options(FingeringStyle style, int keys, int mask) => new()
    {
        Style = style,
        Keys = keys,
        LeftHanded = (mask & 1) != 0,
        ChordsOnOtherHand = (mask & 2) != 0,
        UseRoll = (mask & 4) == 0,
        OuterRoll = (mask & 8) == 0,
    };

    /// <summary>快慢混着的随机谱：快连打、伪多押、长按、双押砖。</summary>
    private static IEnumerable<List<TileHit>> Charts()
    {
        var gaps = new[] { 0.015, 0.022, 0.03, 0.047, 0.06, 0.075, 0.094, 0.15, 0.3 };
        for (var round = 0; round < 12; round++)
        {
            var rng = new Random(1126 + round);
            var t = 0.0;
            var gap = gaps[rng.Next(gaps.Length)];
            var tiles = new List<TileHit>();
            for (var i = 0; i < 240; i++)
            {
                if (rng.Next(6) == 0)
                {
                    gap = gaps[rng.Next(gaps.Length)];
                }

                t += gap;
                var taps = rng.Next(40) == 0 ? rng.Next(2, 9) : 1;
                tiles.Add(new TileHit(tiles.Count + 1, t, rng.Next(25) == 0 ? gap : 0, 180, taps, taps > 1));
                if (rng.Next(14) == 0)
                {
                    tiles.Add(new TileHit(tiles.Count + 1, t + 0.01, 0, 15));
                }
            }

            yield return tiles;
        }
    }

    [Fact]
    public void EveryNoteStaysOnTheHandHis4KChose()
    {
        foreach (var hits in Charts())
        {
            foreach (var style in Styles)
            {
                for (var mask = 0; mask < 16; mask++)
                {
                    var four = NotePlanner.Plan(hits, Options(style, 4, mask));
                    foreach (var keys in new[] { 6, 8 })
                    {
                        var layout = KeyLayout.Of(keys);
                        var many = NotePlanner.Plan(hits, Options(style, keys, mask));
                        Assert.Equal(four.Count, many.Count);
                        for (var i = 0; i < many.Count; i++)
                        {
                            Assert.Equal(four[i].TileIndex, many[i].TileIndex);
                            var fourHands = four[i].Lanes.Keys.Select(KeyLayout.Four.HandOf).Distinct().OrderBy(h => h);
                            var manyHands = many[i].Lanes.Keys.Select(layout.HandOf).Distinct().OrderBy(h => h);
                            if (many[i].Lanes.Count == four[i].Lanes.Count)
                            {
                                Assert.Equal(fourHands, manyHands);
                            }

                            if (!four[i].IsChord)
                            {
                                // 单押：要么是他的 4K 那根手指，要么是同一只手滚开的另一根
                                Assert.Equal(four[i].Lanes.Values.First(), Assert.Single(many[i].Lanes).Value);
                            }
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void ExtraFingersNeverAddLoneNotesOrJacks()
    {
        foreach (var style in Styles)
        {
            foreach (var keys in new[] { 6, 8 })
            {
                var (lone, fourLone, jacks, fourJacks) = (0, 0, 0, 0);
                foreach (var hits in Charts())
                {
                    for (var mask = 0; mask < 16; mask++)
                    {
                        var four = NotePlanner.Plan(hits, Options(style, 4, mask));
                        var many = NotePlanner.Plan(hits, Options(style, keys, mask));
                        lone += LayoutStats.LoneNotes(many, LayoutStats.DefaultLinkGap).Count;
                        fourLone += LayoutStats.LoneNotes(four, LayoutStats.DefaultLinkGap).Count;
                        jacks += Jacks(many, 0.15);
                        fourJacks += Jacks(four, 0.15);
                    }
                }

                Assert.True(lone <= fourLone, $"{style} {keys}K 孤立音符 {lone}，他的 4K {fourLone}");
                Assert.True(jacks <= fourJacks, $"{style} {keys}K 连按 {jacks}，他的 4K {fourJacks}");
            }
        }
    }

    [Fact]
    public void LeftHandedIsTheMirror()
    {
        foreach (var hits in Charts().Take(4))
        {
            foreach (var style in Styles)
            {
                foreach (var keys in new[] { 6, 8 })
                {
                    var layout = KeyLayout.Of(keys);
                    var right = NotePlanner.Plan(hits, Options(style, keys, 0));
                    var left = NotePlanner.Plan(hits, Options(style, keys, 1));
                    Assert.Equal(right.Select(n => n.Lanes.Keys.Select(layout.Mirror).OrderBy(l => l)), left.Select(n => n.Lanes.Keys.AsEnumerable()));
                }
            }
        }
    }

    [Fact]
    public void RollingOffKeepsHisFingers()
    {
        foreach (var hits in Charts().Take(4))
        {
            foreach (var style in Styles)
            {
                var four = NotePlanner.Plan(hits, Options(style, 4, 4));
                var six = NotePlanner.Plan(hits, Options(style, 6, 4));
                Assert.Equal(four.Select(n => n.Lanes.Keys.Select(l => KeyLayout.Six.FromLayout(KeyLayout.Four, l)).OrderBy(l => l)).Where((_, i) => four[i].Lanes.Count == six[i].Lanes.Count),
                    six.Select(n => n.Lanes.Keys.AsEnumerable()).Where((_, i) => four[i].Lanes.Count == six[i].Lanes.Count));
            }
        }
    }

    /// <summary>按拍分手在设置页收起了换手间隔、多押前后让手、同键最短间隔（2026-11-26 用户）：这几项怎么调，4K、6K、8K 都不变。</summary>
    [Fact]
    public void HiddenGapSettingsDoNotChangeHandsByBeat()
    {
        foreach (var hits in Charts().Take(4))
        {
            foreach (var keys in new[] { 4, 6, 8 })
            {
                var plain = NotePlanner.Plan(hits, Options(FingeringStyle.SteadyHands, keys, 0));
                var tuned = Options(FingeringStyle.SteadyHands, keys, 0);
                tuned.ResetWindow = 0.2;
                tuned.ChordFocusWindow = 0.1;
                tuned.JackWindow = 0.06;
                var changed = NotePlanner.Plan(hits, tuned);
                Assert.Equal(plain.Select(n => n.Lanes.Keys.AsEnumerable()), changed.Select(n => n.Lanes.Keys.AsEnumerable()));
            }
        }
    }

    private static int Jacks(List<PlannedNote> notes, double window) =>
        Enumerable.Range(1, Math.Max(0, notes.Count - 1))
            .Count(i => notes[i].Time - notes[i - 1].Time < window - 1e-6 && notes[i].Lanes.Keys.Any(notes[i - 1].Lanes.ContainsKey));
}
