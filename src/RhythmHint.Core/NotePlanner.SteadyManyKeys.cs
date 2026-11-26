using System;
using System.Collections.Generic;
using System.Linq;

namespace RhythmHint.Core;

public static partial class NotePlanner
{
    /// <summary>往前看多少个音符找还按着的长按（长按可以跨很多下）。</summary>
    private const int HoldLookBack = 64;


    /// <summary>
    /// 双手稳拍、时间稳拍的 6K、8K（2026-11-26 用户：以 Linxi-1105 的风格做 6K、8K）。他的排法的核心是每一下交给哪只手，
    /// 所以先按他的规则排 4K，再换到 6K、8K：
    /// <list type="bullet">
    /// <item>每一下的手、提示的个数和分组都和他的 4K 一样（多押同手、长按换手接这些他已经排好）</item>
    /// <item>每个键先换成同一只手、同一根手指（D F J K 落在中间四个键上）</item>
    /// <item>同一只手上相隔不超过 R/2 的一串单押（4K 里是这只手两根手指来回轮）改用这只手的全部手指从外往内滚
    /// （6K L K J、8K ; L K J，外轮关掉时从内往外），手指歇得更久；起手的手指避开前后刚按过、马上要按的键。轮指关掉时不改</item>
    /// <item>一块双押砖要按的下数超过 4（4K 最多放 4 个键）时，补到 min(下数, 键数)</item>
    /// </list>
    /// 左撇子同样按右手排完再整体镜像。
    /// </summary>
    private static List<PlannedNote> PlanSteadyManyKeys(IReadOnlyList<TileHit> tiles, FingeringOptions o, FingeringRhythm? rhythm, KeyLayout layout)
    {
        var four = o.Copy();
        four.Keys = Lanes.Count;
        four.LeftHanded = false;
        var tileOf = new Dictionary<int, TileHit>();
        foreach (var tile in tiles)
        {
            tileOf[tile.TileIndex] = tile;
        }

        var notes = Plan(tiles, four, rhythm).ConvertAll(note => Widen(note, tileOf, layout));
        if (o.UseRoll)
        {
            // 按拍分手不读换手间隔、同键最短间隔（设置页选它时这两项收起来了），这里也用默认值，收起来的设置不暗中起作用
            var roll = o;
            if (o.Style == FingeringStyle.SteadyHands)
            {
                var defaults = new FingeringOptions();
                roll = o.Copy();
                roll.ResetWindow = defaults.ResetWindow;
                roll.JackWindow = defaults.JackWindow;
            }

            for (var hand = 0; hand < 2; hand++)
            {
                RollOneHand(notes, hand, layout, roll);
            }
        }

        return o.LeftHanded ? notes.ConvertAll(note => Mirror(note, layout)) : notes;
    }

    /// <summary>4K 的音符换到多键：同一只手、同一根手指；双押砖要按的下数超过 4 时从两只手的食指往外补。</summary>
    private static PlannedNote Widen(PlannedNote note, Dictionary<int, TileHit> tileOf, KeyLayout layout)
    {
        var wide = new PlannedNote(note.TileIndex, note.Time) { LastTileIndex = note.LastTileIndex };
        foreach (var kv in note.Lanes)
        {
            wide.Lanes[layout.FromLayout(KeyLayout.Four, kv.Key)] = kv.Value;
        }

        if (note.Lanes.Count == Lanes.Count && note.TileIndex == note.LastTileIndex
            && tileOf.TryGetValue(note.TileIndex, out var tile) && Presses(tile) > Lanes.Count)
        {
            var want = Math.Min(Presses(tile), layout.Count);
            for (var finger = 0; wide.Lanes.Count < want && finger < layout.FingersPerHand; finger++)
            {
                for (var hand = 1; hand >= 0 && wide.Lanes.Count < want; hand--)
                {
                    var lane = layout.Lane(hand, finger);
                    if (!wide.Lanes.ContainsKey(lane))
                    {
                        wide.Lanes[lane] = tile.HoldSeconds;
                    }
                }
            }
        }

        return wide;
    }

    /// <summary>
    /// 一只手上的快单押串（这只手相邻两下都不超过 R/2，中间这只手没有多押、长按）用这只手的全部手指轮着按。
    /// 每串的起手见 <see cref="AssignRoll"/>。
    /// </summary>
    private static void RollOneHand(List<PlannedNote> notes, int hand, KeyLayout layout, FingeringOptions o)
    {
        bool Plain(PlannedNote note) => !note.IsChord && layout.HandOf(note.Lanes.Keys.First()) == hand && note.Lanes.Values.First() <= 0;
        bool Touches(PlannedNote note) => note.Lanes.Keys.Any(lane => layout.HandOf(lane) == hand);

        var mine = new List<int>(); // 这只手按到的音符序号
        for (var i = 0; i < notes.Count; i++)
        {
            if (Touches(notes[i]))
            {
                mine.Add(i);
            }
        }

        for (var a = 0; a < mine.Count;)
        {
            var b = a;
            // 只算这只手连着按的：中间夹着另一只手的（左右交替）保持他的手指，不然交替的两只手都往外滚，跳得很远
            while (b + 1 < mine.Count && mine[b + 1] == mine[b] + 1 && Plain(notes[mine[b]]) && Plain(notes[mine[b + 1]])
                && notes[mine[b + 1]].Time - notes[mine[b]].Time <= (o.ResetWindow / 2) + Epsilon)
            {
                b++;
            }

            if (b > a)
            {
                AssignRoll(notes, mine.GetRange(a, b - a + 1), hand, layout, o);
            }

            a = b + 1;
        }
    }

    /// <summary>
    /// 一串的手指：试每一种起手（外轮时优先最后一下落在食指），只收「附近的孤立音符、连按、撞上的键都不比他的 4K 手指多」的；
    /// 都不行就留着他的 4K 手指（多出来的手指只用来让手指多歇，不让他的排法更跳）。
    /// </summary>
    private static void AssignRoll(List<PlannedNote> notes, List<int> run, int hand, KeyLayout layout, FingeringOptions o)
    {
        var fingers = layout.FingersPerHand;
        var from = Math.Max(0, run[0] - 2);
        var to = Math.Min(notes.Count - 1, run[run.Count - 1] + 2);
        var original = run.ConvertAll(i => notes[i]);

        // 附近一段的代价：撞上还按着的键、孤立音符、连按（前一下、隔一下用同一个键且不到 JackWindow）
        (int Held, int Lone, int Jacks) Cost()
        {
            var held = 0;
            foreach (var i in run)
            {
                var lane = notes[i].Lanes.Keys.First();
                for (var j = i - 1; j >= 0 && i - j <= HoldLookBack; j--)
                {
                    if (notes[j].Lanes.TryGetValue(lane, out var hold) && notes[j].Time + hold > notes[i].Time - Epsilon)
                    {
                        held++;
                    }
                }
            }

            var lone = 0;
            var jacks = 0;
            for (var i = from; i <= to; i++)
            {
                if (i > 0 && i + 1 < notes.Count && notes[i].Time - notes[i - 1].Time <= LayoutStats.DefaultLinkGap
                    && notes[i + 1].Time - notes[i].Time <= LayoutStats.DefaultLinkGap
                    && LayoutStats.Distance(notes[i], notes[i - 1]) >= 2 && LayoutStats.Distance(notes[i], notes[i + 1]) >= 2)
                {
                    lone++;
                }

                for (var j = i - 1; j >= Math.Max(0, i - 2); j--)
                {
                    if (notes[i].Time - notes[j].Time < o.JackWindow - Epsilon && notes[i].Lanes.Keys.Any(notes[j].Lanes.ContainsKey))
                    {
                        jacks++;
                    }
                }
            }

            return (held, lone, jacks);
        }

        void Apply(int offset)
        {
            for (var k = 0; k < run.Count; k++)
            {
                var old = original[k];
                var step = (k + offset) % fingers;
                var note = new PlannedNote(old.TileIndex, old.Time) { LastTileIndex = old.LastTileIndex };
                note.Lanes[layout.Lane(hand, o.OuterRoll ? fingers - 1 - step : step)] = old.Lanes.Values.First();
                notes[run[k]] = note;
            }
        }

        var baseline = Cost();
        var preferred = o.OuterRoll ? (fingers - (run.Count % fingers)) % fingers : 0;
        (int Offset, int Lone, int Jacks)? best = null;
        foreach (var offset in Enumerable.Range(0, fingers).OrderBy(offset => offset == preferred ? 0 : 1).ThenBy(offset => offset))
        {
            Apply(offset);
            var cost = Cost();
            if (cost.Held == 0 && cost.Lone <= baseline.Lone && cost.Jacks <= baseline.Jacks
                && (best is null || (cost.Lone, cost.Jacks).CompareTo((best.Value.Lone, best.Value.Jacks)) < 0))
            {
                best = (offset, cost.Lone, cost.Jacks);
            }
        }

        if (best is null)
        {
            for (var k = 0; k < run.Count; k++)
            {
                notes[run[k]] = original[k];
            }

            return;
        }

        Apply(best.Value.Offset);
    }
}
