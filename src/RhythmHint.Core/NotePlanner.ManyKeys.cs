using System;
using System.Collections.Generic;
using System.Linq;

namespace RhythmHint.Core;

public static partial class NotePlanner
{
    /// <summary>
    /// 6K、8K 的排法（2026-11-22 用户：不照搬 4K，参考网上多键的手法另写一套；4K 不走这里，结果逐音不变）。
    /// 按右手为主写，左撇子最后整体镜像：
    /// <list type="bullet">
    /// <item>提示怎么合（多押、伪多押、双押砖）和 4K 一样（Moments），提示的个数、每个提示盖哪几块砖不随键数变</item>
    /// <item>慢（和上一下隔得超过 R）：右手食指；中速（R/2–R）：换另一只手的食指，左右手食指轮流</item>
    /// <item>快连打（相邻都在 R/2 以内的一串单押）：先在一只手上从外往内滚到食指（外轮关掉时从内往外），这只手的手指都用过一遍还没完才换另一只手
    /// （2026-11-25 用户：轮指少换手、尽量在一边轮完、不要跨越中间）。6K 两下 K J、三下 L K J、四下 L K J F；
    /// 再长两只手整手轮流（6K L K J S D F，8K ; L K J A S D F）。前一下在 R 以内就从另一只手起，否则右手起；轮指关掉时只用两根食指</item>
    /// <item>多押：手指够就整组放在一只手上，从食指往外（6K 三押 J K L，8K 四押 J K L ;），不够的从另一只手的食指往外补。
    /// 优先右手（多押用手选另一只手时左手），那只手刚按过、来不及才换手</item>
    /// <item>和 4K 一样：紧挨着多押的单押放另一只手的食指，前一下先用中指让开（不出现食指→中指）；
    /// 上一下的长按还按着时换另一只手接；同一条轨道不在 JackWindow 内连按</item>
    /// </list>
    /// </summary>
    private static List<PlannedNote> PlanManyKeys(IReadOnlyList<TileHit> tiles, FingeringOptions o, KeyLayout layout)
    {
        var reviewPreference = o.Style == FingeringStyle.ReviewPreferenceV1 && !tiles.Any(hit => hit.HoldSeconds > 0);
        var moments = Moments(tiles, o);
        var focus = FocusChords(moments, o, reviewPreference);
        var runEnd = FastRuns(moments, focus, o);
        var preferred = o.ChordsOnOtherHand ? 0 : 1;
        var yieldIndex = layout.Lane(1 - preferred, 0);
        var yieldMiddle = layout.Lane(1 - preferred, 1);
        var notes = new List<PlannedNote>(moments.Count);
        var hands = new List<int>(moments.Count); // 每个音符主要在哪只手：单押是它那只手，多押是放得多的那只手
        for (var i = 0; i < moments.Count; i++)
        {
            var moment = moments[i];
            var time = moment[0].Time;
            var last = notes.Count > 0 ? notes[notes.Count - 1] : null;
            var lastHand = hands.Count > 0 ? hands[hands.Count - 1] : 1;
            var recent = last is not null && time - last.Time <= o.ResetWindow + Epsilon;
            if (moment.Count > 1)
            {
                var presses = ChordPresses(moment, layout);
                var hand = ManyChordHand(preferred, presses.Count, time, last, layout, o);
                notes.Add(BuildManyChord(moment, presses, hand, last, layout, o));
                hands.Add(hand);
                continue;
            }

            if (runEnd[i] > i)
            {
                var run = RunLanes(moments, i, runEnd[i], recent ? 1 - lastHand : 1, notes, focus, preferred, layout, o, reviewPreference);
                for (var k = 0; k < run.Length; k++)
                {
                    var tile = moments[i + k][0];
                    var note = new PlannedNote(tile.TileIndex, tile.Time);
                    note.Lanes[run[k]] = tile.HoldSeconds;
                    notes.Add(note);
                    hands.Add(layout.HandOf(run[k]));
                }

                i = runEnd[i];
                continue;
            }

            int lane;
            if (focus[i] >= 0)
            {
                // 前面的多押已经排好；后面的还没排，按偏好的手算。按多押的手专心按多押，这一下放另一只手
                var chordHand = focus[i] < i ? hands[focus[i]] : preferred;
                lane = IndexOrMiddle(1 - chordHand, time, last, layout, o);
                if (focus[i] > i && Blocked(lane, time, last, o) && !Blocked(layout.Lane(chordHand, 0), time, last, o)
                    && moments[i + 1][0].Time - time < o.JackWindow - Epsilon)
                {
                    // 另一只手刚按过多押、来不及，后面的多押又紧挨着（复现 TUF 298：左手双押后紧跟一下、再接多押）：
                    // 这一下放偏好的手的食指，后面的多押来不及用这只手、换到另一只手（同 4K）
                    lane = layout.Lane(chordHand, 0);
                }
            }
            else
            {
                lane = layout.Lane(recent ? 1 - lastHand : 1, 0);
            }

            if (lane == yieldIndex && i + 1 < moments.Count && focus[i + 1] == i + 2
                && moments[i + 1][0].Time - time < o.JackWindow - Epsilon && !Blocked(yieldMiddle, time, last, o))
            {
                // 下一下要让给这根食指、又挨得太近：这一下先用同一只手的中指（从外往内），同 4K
                lane = yieldMiddle;
            }

            var holding = HoldingHand(last, time, layout);
            if (holding >= 0 && layout.HandOf(lane) == holding)
            {
                lane = IndexOrMiddle(1 - holding, time, last, layout, o);
            }

            if (Blocked(lane, time, last, o))
            {
                lane = NearestFreeLane(lane, time, o, last!, layout);
            }

            var single = new PlannedNote(moment[0].TileIndex, time);
            single.Lanes[lane] = moment[0].HoldSeconds;
            notes.Add(single);
            hands.Add(layout.HandOf(lane));
        }

        RestFingers(notes, layout, o);
        if (o.UseRoll && o.OuterRoll)
        {
            OuterFirstMany(notes, layout, o);
        }

        return o.LeftHanded ? notes.ConvertAll(note => Mirror(note, layout)) : notes;
    }

    /// <summary>
    /// 快连打：每个能进连打的单押记下它所在那一串的最后一个（不是一串的开头、或者一串只有一下的记它自己）。
    /// 一串是相邻间隔都不超过 R/2 的单押；让手的单押、上一下的长按到这一下还没松开的不进（它们有自己的规则）。
    /// </summary>
    private static int[] FastRuns(List<List<TileHit>> moments, int[] focus, FingeringOptions o)
    {
        var n = moments.Count;
        var end = new int[n];
        bool Eligible(int i) => moments[i].Count == 1 && focus[i] < 0
            && (i == 0 || !moments[i - 1].Any(t => t.HoldSeconds > 0 && moments[i - 1][0].Time + t.HoldSeconds > moments[i][0].Time - Epsilon));
        for (var start = 0; start < n;)
        {
            end[start] = start;
            if (!Eligible(start))
            {
                start++;
                continue;
            }

            var last = start;
            while (last + 1 < n && Eligible(last + 1) && moments[last + 1][0].Time - moments[last][0].Time <= (o.ResetWindow / 2) + Epsilon)
            {
                last++;
                end[last] = last;
            }

            end[start] = last;
            start = last + 1;
        }

        return end;
    }

    /// <summary>
    /// 一串快连打每一下的轨道。每组是一只手的全部手指，从外往内滚到食指（外轮关掉时从内往外）；这只手用满了才换另一只手，
    /// 最后不够一只手的从食指往外数。原来每组是一串的一半（两下 J F、四下 K J D F），短串也跨中间；2026-11-25 用户要少换手、尽量在一边轮完。
    /// 「尽量」：一只手上怎么排都有手指歇不到 R/2 又要按时（多押、密集单押刚按过那只手），再拿原来每组一半的分法比一比，不比一只手差就用它。
    /// 一般从 startHand 起手；和前后接不上（连按、21 式、孤立音符、占了紧接着那个让手单押要用的食指）时换另一只手起手。
    /// </summary>
    private static int[] RunLanes(List<List<TileHit>> moments, int start, int end, int startHand, List<PlannedNote> notes, int[] focus, int preferred,
        KeyLayout layout, FingeringOptions o, bool reviewPreference)
    {
        var n = end - start + 1;
        var indexOnly = !o.UseRoll || (reviewPreference && ReviewShortRun(moments, start, end, o));
        int[] Assign(int hand, int group)
        {
            var lanes = new int[n];
            for (var k = 0; k < n; k++)
            {
                var g = k / group;
                var size = Math.Min(group, n - (g * group));
                var pos = k % group;
                lanes[k] = layout.Lane(g % 2 == 0 ? hand : 1 - hand, o.OuterRoll ? size - 1 - pos : pos);
            }

            return lanes;
        }

        var time = moments[start][0].Time;
        var endTime = moments[end][0].Time;
        var last = notes.Count > 0 ? notes[notes.Count - 1] : null;
        var beforeLast = notes.Count > 1 ? notes[notes.Count - 2] : null;

        // 紧接着的单押要让给下一个多押、只能按 1 − preferred 那只手的食指：这串最后一下不能也是它（不然那一下只能食指→中指）
        bool EndFits(int[] lanes) =>
            end + 1 >= moments.Count || focus[end + 1] != end + 2
            || lanes[n - 1] != layout.Lane(1 - preferred, 0)
            || moments[end + 1][0].Time - endTime >= o.JackWindow - Epsilon;

        // 起手和上一下同一只手、R 以内、手指从内到外（外轮时的 21 式）
        bool InnerToOuter(int lane) =>
            o.OuterRoll && last is { IsChord: false } && time - last.Time <= o.ResetWindow + Epsilon
            && layout.HandOf(last.Lanes.Keys.First()) == layout.HandOf(lane) && layout.Finger(lane) > layout.Finger(last.Lanes.Keys.First());

        // 上一下夹在中间、两边都跳 2 条以上（孤立音符）
        bool IsolatesLast(int lane) =>
            last is not null && beforeLast is not null
            && time - last.Time <= LayoutStats.DefaultLinkGap && last.Time - beforeLast.Time <= LayoutStats.DefaultLinkGap
            && LayoutStats.Distance(beforeLast, last) >= 2 && last.Lanes.Keys.Min(l => Math.Abs(l - lane)) >= 2;

        // 这根手指上次松开的时间（往前看 R 以内的音符，再看这串里排在前面的几下）
        double Released(int[] lanes, int k)
        {
            for (var j = k - 1; j >= 0; j--)
            {
                if (lanes[j] == lanes[k])
                {
                    return moments[start + j][0].Time;
                }
            }

            for (var j = notes.Count - 1; j >= 0 && time - notes[j].Time <= o.ResetWindow + Epsilon; j--)
            {
                if (notes[j].Lanes.TryGetValue(lanes[k], out var hold))
                {
                    return notes[j].Time + hold;
                }
            }

            return double.NegativeInfinity;
        }

        // 歇不到 R/2 又要按的下数（2026-11-25：一只手用满再换以后，紧跟在多押后面的快连打从多押那只手起手时，刚按过的手指隔 30 ms 又要按）
        int Tired(int[] lanes) => Enumerable.Range(0, n).Count(k => moments[start + k][0].Time - Released(lanes, k) < (o.ResetWindow / 2) - Epsilon);

        // 每组 group 根手指时挑起手：两只手起手各一种；最后一组只有一下（落在食指）时还可以改用同一只手的中指，紧接着让手的单押按食指，
        // 从外往内（同 4K 的让手）。起手和上一下连按的不要；剩下的按「每有一下手指歇不够 20（只用食指时不算：两根食指轮流本来就这样）、
        // 最后一下占了让手要用的食指 12、起手 21 式 10、上一下成了孤立音符 5、改用中指 3、不是顺着起手 1」挑最小的
        (int[]? Lanes, int Penalty, int Tired) Best(int group)
        {
            (int[]? Lanes, int Penalty, int Tired) best = (null, int.MaxValue, 0);
            foreach (var hand in new[] { startHand, 1 - startHand })
            {
                var plain = Assign(hand, group);
                var lastAlone = n > 1 && (n - 1) % group == 0;
                foreach (var middle in lastAlone ? new[] { false, true } : new[] { false })
                {
                    var lanes = (int[])plain.Clone();
                    if (middle)
                    {
                        lanes[n - 1] = layout.Lane(layout.HandOf(lanes[n - 1]), 1);
                    }

                    if (Blocked(lanes[0], time, last, o))
                    {
                        continue;
                    }

                    var tired = indexOnly ? 0 : Tired(lanes);
                    var penalty = (20 * tired) + (EndFits(lanes) ? 0 : 12) + (InnerToOuter(lanes[0]) ? 10 : 0) + (IsolatesLast(lanes[0]) ? 5 : 0)
                        + (middle ? 3 : 0) + (hand == startHand ? 0 : 1);
                    if (penalty < best.Penalty)
                    {
                        best = (lanes, penalty, tired);
                    }
                }
            }

            return best;
        }

        var full = indexOnly ? 1 : layout.FingersPerHand;
        var chosen = Best(full);
        var split = Math.Min(Math.Max((n + 1) / 2, 1), layout.FingersPerHand);
        if (!indexOnly && split != full && (chosen.Lanes is null || chosen.Tired > 0))
        {
            // 一只手上有手指歇不够：原来每组一半的分法不比它差就用（两只手各分一半，「排完再顺一遍」的余地大）
            var halves = Best(split);
            if (halves.Lanes is not null && halves.Penalty <= chosen.Penalty)
            {
                chosen = halves;
            }
        }

        var best = chosen.Lanes;
        if (best is null)
        {
            // 两只手起手都会连按（上一下是多押）：第一下换到最近的空轨道
            best = Assign(startHand, full);
            best[0] = NearestFreeLane(best[0], time, o, last!, layout);
        }

        return best;
    }

    /// <summary>交替优先（ReviewPreferenceV1）的短串：长度、间隔落在它的参数里、节奏均匀的快连打只用两根食指左右手轮流。</summary>
    private static bool ReviewShortRun(List<List<TileHit>> moments, int start, int end, FingeringOptions o)
    {
        var n = end - start + 1;
        if (n < o.ReviewShortMinNotes || n > o.ReviewShortMaxNotes)
        {
            return false;
        }

        var firstGap = moments[start + 1][0].Time - moments[start][0].Time;
        for (var i = start + 1; i <= end; i++)
        {
            var gap = moments[i][0].Time - moments[i - 1][0].Time;
            if (gap < o.ReviewShortMinGap - Epsilon || gap > Math.Min(o.ReviewShortMaxGap, o.ResetWindow / 2) + Epsilon
                || Math.Abs(gap - firstGap) > o.ReviewTimingTolerance + Epsilon)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 多押每个键的长按，按先后排。一块双押砖自己要按超过 4 下时（Moments 按 4K 最多放 4 份），补到 min(下数, 键数)。
    /// </summary>
    private static List<double> ChordPresses(List<TileHit> moment, KeyLayout layout)
    {
        var presses = moment.ConvertAll(t => t.HoldSeconds);
        var tile = moment[0];
        if (moment.Count == Lanes.Count && moment.TrueForAll(t => t.TileIndex == tile.TileIndex))
        {
            while (presses.Count < Math.Min(Presses(tile), layout.Count))
            {
                presses.Add(tile.HoldSeconds);
            }
        }

        return presses;
    }

    /// <summary>多押放哪只手：偏好的手从食指往外那几根手指有刚按过、来不及的，另一只手来得及就换过去。</summary>
    private static int ManyChordHand(int preferred, int presses, double time, PlannedNote? last, KeyLayout layout, FingeringOptions o)
    {
        var fingers = Math.Min(presses, layout.FingersPerHand);
        bool Free(int hand) => Enumerable.Range(0, fingers).All(f => !Blocked(layout.Lane(hand, f), time, last, o));
        return !Free(preferred) && Free(1 - preferred) ? 1 - preferred : preferred;
    }

    /// <summary>
    /// 多押的键：这只手从食指往外放满，不够的从另一只手的食指往外补；每只手先用来得及的手指。
    /// 双押、手指够的三押四押都在同一只手上，绝不跨手。
    /// </summary>
    private static PlannedNote BuildManyChord(List<TileHit> moment, List<double> presses, int hand, PlannedNote? last, KeyLayout layout, FingeringOptions o)
    {
        var first = moment[0];
        var note = new PlannedNote(first.TileIndex, first.Time) { LastTileIndex = moment[moment.Count - 1].TileIndex };
        IEnumerable<int> Fingers(int h) => Enumerable.Range(0, layout.FingersPerHand)
            .Select(f => layout.Lane(h, f))
            .OrderBy(lane => Blocked(lane, first.Time, last, o) ? 1 : 0);
        var main = Math.Min(presses.Count, layout.FingersPerHand);
        var order = Fingers(hand).Take(main).Concat(Fingers(1 - hand).Take(presses.Count - main)).ToList();
        for (var k = 0; k < presses.Count; k++)
        {
            note.Lanes[order[k]] = presses[k];
        }

        return note;
    }

    /// <summary>这只手的食指；食指刚按过（会连按）才用中指。</summary>
    private static int IndexOrMiddle(int hand, double time, PlannedNote? last, KeyLayout layout, FingeringOptions o)
    {
        var index = layout.Lane(hand, 0);
        return Blocked(index, time, last, o) ? layout.Lane(hand, 1) : index;
    }
}
