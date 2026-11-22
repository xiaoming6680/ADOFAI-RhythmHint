using System;
using System.Collections.Generic;
using System.Linq;

namespace RhythmHint.Core;

public static partial class NotePlanner
{
    /// <summary>手指歇不到 R/2 又按的代价：基本人按不出来（2026-11-22 用户截图：右手刚按完 J+K 双押，隔一下又按 J）。</summary>
    private const double TiredCost = 30;

    /// <summary>歇了 R/2 到 R 又按的代价（最多）：能按，但累。</summary>
    private const double SoreCost = 6;

    /// <summary>换到同一只手的别的手指、另一只手的代价；手指每差一根再加 FingerStepCost。</summary>
    private const double SameHandCost = 4;

    private const double OtherHandCost = 10;

    private const double FingerStepCost = 3;

    /// <summary>换手指后同一只手 R 以内从内往外（21 式）的代价。</summary>
    private const double InnerToOuterCost = 15;

    /// <summary>换手指后和前一下或后一下隔 2 条以上（跳轨）的代价；两边都隔 2 条以上就是孤立音符，代价大得多。</summary>
    private const double JumpCost = 4;

    private const double LoneCost = 80;

    /// <summary>换到按着长按的那只手（长按要换手接）、多押换到紧挨着的单押那只手（让按多押的手专心按多押）的代价。</summary>
    private const double HeldHandCost = 50;

    private const double BusyHandCost = 8;

    /// <summary>多押每往外挪一根手指的代价（多押尽量从食指起）。</summary>
    private const double ChordShiftCost = 5;

    /// <summary>
    /// 6K、8K 排完后再顺一遍，让每根手指歇够（2026-11-22 用户：还有很多人打不出来的段）：
    /// 规则只管和上一下不连按，隔一两下又用同一根手指的地方会有（长串滚完紧接多押、多押后紧跟两下单押）。
    /// 每一下按「这根手指上次松开到现在歇了多久」算代价：歇不到 R/2 很大、R/2–R 一点点、超过 R 没有；
    /// 再加上离规则排的那根手指有多远（同一只手近、另一只手远）。规则排的手指歇够了就不动，所以平常的轮指和交替不变。
    /// 单押在所有轨道里挑代价最小的（轮指关掉时只在两根食指里挑），一只手的多押在两只手的相邻几根手指里挑。
    /// 和上一下连按、还按着的键不能用。
    /// </summary>
    private static void RestFingers(List<PlannedNote> notes, KeyLayout layout, FingeringOptions o)
    {
        var free = new double[layout.Count]; // 每条轨道上次松开的时间
        for (var c = 0; c < free.Length; c++)
        {
            free[c] = double.NegativeInfinity;
        }

        var lastSingle = new[] { (Time: double.NegativeInfinity, Finger: 0), (Time: double.NegativeInfinity, Finger: 0) };
        PlannedNote? prev = null;
        for (var i = 0; i < notes.Count; i++)
        {
            var note = notes[i];
            var next = i + 1 < notes.Count ? notes[i + 1] : null;
            if (!note.IsChord)
            {
                note = RestSingle(note, prev, next, PrevFar(notes, i), free, lastSingle, layout, o);
            }
            else if (note.Lanes.Count <= layout.FingersPerHand && note.Lanes.Keys.Select(layout.HandOf).Distinct().Count() == 1)
            {
                note = RestChord(note, prev, next, PrevFar(notes, i), free, layout, o);
            }

            notes[i] = note;
            foreach (var kv in note.Lanes)
            {
                free[kv.Key] = note.Time + kv.Value;
            }

            if (!note.IsChord)
            {
                var lane = note.Lanes.Keys.First();
                lastSingle[layout.HandOf(lane)] = (note.Time, layout.Finger(lane));
            }

            prev = note;
        }
    }

    /// <summary>
    /// 6K、8K 的外轮（同 4K 的 OuterFirst）：同一只手、R 以内连着两下从内往外（21 式，歇够手指那一遍换出来的多），两下换位置，变成从外往内。
    /// 换了会连按、换过去的手指没歇够 R/2、和前后两下又成了 21 式、附近多出孤立音符的不换。外轮关掉、轮指关掉时不做。
    /// </summary>
    private static void OuterFirstMany(List<PlannedNote> notes, KeyLayout layout, FingeringOptions o)
    {
        var free = new double[layout.Count]; // notes[0..i-2] 松开的时间
        for (var c = 0; c < free.Length; c++)
        {
            free[c] = double.NegativeInfinity;
        }

        bool Single(int k) => k >= 0 && k < notes.Count && !notes[k].IsChord;
        int LaneOf(int k) => notes[k].Lanes.Keys.First();
        bool Lone(int k) =>
            k >= 1 && k + 1 < notes.Count && notes[k].Time - notes[k - 1].Time <= LayoutStats.DefaultLinkGap
            && notes[k + 1].Time - notes[k].Time <= LayoutStats.DefaultLinkGap
            && LayoutStats.Distance(notes[k], notes[k - 1]) >= 2 && LayoutStats.Distance(notes[k], notes[k + 1]) >= 2;
        int LoneAround(int a) => Enumerable.Range(a - 1, 4).Count(Lone);
        bool InnerToOuter(int from, int to) =>
            Single(from) && Single(to) && Math.Abs(notes[to].Time - notes[from].Time) <= o.ResetWindow + Epsilon
            && layout.HandOf(LaneOf(from)) == layout.HandOf(LaneOf(to)) && layout.Finger(LaneOf(to)) > layout.Finger(LaneOf(from));

        for (var i = 1; i < notes.Count; i++)
        {
            var (a, b) = (i - 1, i);
            if (InnerToOuter(a, b))
            {
                var (inner, outer) = (LaneOf(a), LaneOf(b));
                var (ta, tb) = (notes[a].Time, notes[b].Time);
                var rested = ta - free[outer] >= (o.ResetWindow / 2) - Epsilon && tb - free[inner] >= (o.ResetWindow / 2) - Epsilon;
                var noJack = (a == 0 || !Jacks(notes[a - 1], outer, ta, o)) && (b + 1 >= notes.Count || !Jacks(notes[b + 1], inner, tb, o));
                var swappedA = WithLane(notes[a], outer);
                var swappedB = WithLane(notes[b], inner);
                var (oldA, oldB) = (notes[a], notes[b]);
                var lone = LoneAround(a);
                (notes[a], notes[b]) = (swappedA, swappedB);
                var clean = !InnerToOuter(a - 1, a) && !InnerToOuter(b, b + 1) && LoneAround(a) <= lone;
                if (!(rested && noJack && clean))
                {
                    (notes[a], notes[b]) = (oldA, oldB);
                }
            }

            foreach (var kv in notes[i - 1].Lanes)
            {
                free[kv.Key] = notes[i - 1].Time + kv.Value;
            }
        }
    }

    /// <summary>上一下已经和它前面那下隔得远（0.3 秒内隔 2 条以上）：这一下再离它远，它就成了孤立音符。</summary>
    private static bool PrevFar(List<PlannedNote> notes, int i) =>
        i >= 2 && notes[i - 1].Time - notes[i - 2].Time <= LayoutStats.DefaultLinkGap && LayoutStats.Distance(notes[i - 2], notes[i - 1]) >= 2;

    /// <summary>
    /// 换了位置以后和前后两下的距离代价：一边远是跳轨；两边都远是孤立音符；上一下本来一边远、这一下又远，上一下成了孤立音符。
    /// 孤立音符的代价比手指最累还大，换位置时不许造成孤立音符（用户要求减少孤立音符）。
    /// </summary>
    private static double SpreadCost(IEnumerable<int> lanes, double time, PlannedNote? prev, PlannedNote? next, bool prevFar)
    {
        bool Far(PlannedNote? other) =>
            other is not null && Math.Abs(other.Time - time) <= LayoutStats.DefaultLinkGap && other.Lanes.Keys.Min(l => lanes.Min(c => Math.Abs(l - c))) >= 2;
        var before = Far(prev);
        var after = Far(next);
        return (before && after ? LoneCost : before || after ? JumpCost : 0) + (before && prevFar ? LoneCost : 0);
    }

    /// <summary>这根手指歇了 rest 秒又按的代价。</summary>
    private static double RestCost(double rest, FingeringOptions o)
    {
        var half = o.ResetWindow / 2;
        return rest < half - Epsilon ? TiredCost + (TiredCost * (half - rest) / half)
            : rest < o.ResetWindow - Epsilon ? SoreCost * (o.ResetWindow - rest) / half
            : 0;
    }

    /// <summary>这条轨道在 time 能不能按：和上一下连按、长按还没松开的不能。</summary>
    private static bool Usable(int lane, double time, PlannedNote? prev, double[] free, FingeringOptions o) =>
        !Blocked(lane, time, prev, o) && time >= free[lane] - Epsilon;

    private static PlannedNote RestSingle(PlannedNote note, PlannedNote? prev, PlannedNote? next, bool prevFar, double[] free,
        (double Time, int Finger)[] lastSingle, KeyLayout layout, FingeringOptions o)
    {
        var time = note.Time;
        var proposal = note.Lanes.Keys.First();
        var (hand, finger) = (layout.HandOf(proposal), layout.Finger(proposal));
        var holding = HoldingHand(prev, time, layout);
        var stuck = !Usable(proposal, time, prev, free, o); // 前一下换过手指后，规则排的位置连按了：这时宁可孤立也要换开
        double Cost(int c)
        {
            if (!Usable(c, time, prev, free, o))
            {
                return double.PositiveInfinity;
            }

            // 和前后两下（0.3 秒以内）离得远：看着是跳来跳去，两边都远就成了孤立音符（用户要求减少）。规则排的那根手指也算：
            // 前一下换过手指后，它可能成了孤立音符
            var spread = SpreadCost(new[] { c }, time, prev, next, prevFar);
            if (c == proposal)
            {
                return RestCost(time - free[c], o) + spread;
            }

            if (!stuck && (spread >= LoneCost || (next is not null && Jacks(next, c, time, o))))
            {
                return double.PositiveInfinity; // 换过去的位置不能造成孤立音符，也不能占掉紧接着那一下要按的键（它就得连按）
            }

            var cost = RestCost(time - free[c], o) + spread;

            var h = layout.HandOf(c);
            var f = layout.Finger(c);
            cost += (h == hand ? SameHandCost : OtherHandCost) + (FingerStepCost * Math.Abs(f - finger));
            if (h == holding)
            {
                cost += HeldHandCost;
            }

            var (lastTime, lastFinger) = lastSingle[h];
            if (o.OuterRoll && time - lastTime <= o.ResetWindow + Epsilon && f > lastFinger)
            {
                cost += InnerToOuterCost;
            }

            // 下一下排在同一只手、更外侧的手指：换过去后下一下成了 21 式
            if (o.OuterRoll && next is { IsChord: false } && next.Time - time <= o.ResetWindow + Epsilon
                && layout.HandOf(next.Lanes.Keys.First()) == h && layout.Finger(next.Lanes.Keys.First()) > f)
            {
                cost += InnerToOuterCost;
            }

            return cost;
        }

        var best = proposal;
        var bestCost = Cost(proposal);
        if (bestCost <= 0)
        {
            return note;
        }

        for (var c = 0; c < layout.Count; c++)
        {
            if (!o.UseRoll && layout.Finger(c) != 0)
            {
                continue; // 轮指关掉：只用两根食指
            }

            var cost = Cost(c);
            if (cost < bestCost - Epsilon)
            {
                (best, bestCost) = (c, cost);
            }
        }

        return best == proposal ? note : WithLane(note, best);
    }

    /// <summary>
    /// 一只手的多押：在两只手里挑相邻的几根手指（从食指起每往外挪一根加一点代价，换手加一点），按歇得够不够挑；
    /// 紧挨着（JackWindow 以内）的单押在哪只手，多押尽量不放那只手。长按按手指从内到外的顺序跟着挪。
    /// </summary>
    private static PlannedNote RestChord(PlannedNote note, PlannedNote? prev, PlannedNote? next, bool prevFar, double[] free, KeyLayout layout,
        FingeringOptions o)
    {
        var time = note.Time;
        var size = note.Lanes.Count;
        var hand = layout.HandOf(note.Lanes.Keys.First());
        var start = note.Lanes.Keys.Min(layout.Finger);
        var holding = HoldingHand(prev, time, layout);
        var stuck = note.Lanes.Keys.Any(lane => !Usable(lane, time, prev, free, o));
        bool Near(PlannedNote? other, int h) =>
            other is { IsChord: false } && Math.Abs(other.Time - time) < o.JackWindow - Epsilon && layout.HandOf(other.Lanes.Keys.First()) == h;

        double Cost(int h, int s)
        {
            var cost = 0.0;
            for (var f = s; f < s + size; f++)
            {
                var lane = layout.Lane(h, f);
                if (!Usable(lane, time, prev, free, o))
                {
                    return double.PositiveInfinity;
                }

                cost += RestCost(time - free[lane], o);
            }

            cost += ChordShiftCost * s;
            if (h != hand)
            {
                cost += OtherHandCost;
            }

            if (h == holding)
            {
                cost += HeldHandCost;
            }

            if (Near(prev, h) || Near(next, h))
            {
                cost += BusyHandCost;
            }

            var spread = SpreadCost(Enumerable.Range(s, size).Select(f => layout.Lane(h, f)), time, prev, next, prevFar);
            var moved = h != hand || s != start;
            var takesNext = next is not null && Enumerable.Range(s, size).Any(f => Jacks(next, layout.Lane(h, f), time, o));
            return moved && !stuck && (spread >= LoneCost || takesNext) ? double.PositiveInfinity : cost + spread;
        }

        var (bestHand, bestStart) = (hand, start);
        var bestCost = Cost(hand, start);
        if (bestCost <= ChordShiftCost * start + Epsilon)
        {
            return note; // 原来的手指都歇够了，离前后也不远
        }

        foreach (var h in new[] { hand, 1 - hand })
        {
            for (var s = 0; s + size <= layout.FingersPerHand; s++)
            {
                var cost = Cost(h, s);
                if (cost < bestCost - Epsilon)
                {
                    (bestHand, bestStart, bestCost) = (h, s, cost);
                }
            }
        }

        if (bestHand == hand && bestStart == start)
        {
            return note;
        }

        var moved = new PlannedNote(note.TileIndex, note.Time) { LastTileIndex = note.LastTileIndex };
        var holds = note.Lanes.OrderBy(kv => layout.Finger(kv.Key)).Select(kv => kv.Value).ToList();
        for (var k = 0; k < size; k++)
        {
            moved.Lanes[layout.Lane(bestHand, bestStart + k)] = holds[k];
        }

        return moved;
    }
}
