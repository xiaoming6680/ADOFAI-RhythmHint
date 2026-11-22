using System;
using System.Collections.Generic;
using System.Linq;

namespace RhythmHint.Core;

/// <summary>
/// 把关卡的砖块变成要画的音符。只看这一关自己的砖块之间的间隔，不读任何外部谱面、不看 BPM 和小节。
/// 思路来自 LaneReader 的「时间重置法」，和原版「全节奏提示」作者的实际按法一致（见 docs/开发说明.md）：
/// <list type="number">
/// <item>同时到的砖合成一个「时刻」：相邻两块转角不超过 ChordAngle、相隔不超过 ChordMaxGap（读不到转角时和第一块相隔不到 ChordWindow），
/// 最多四块。双押只在 0+1 或 2+3；三押是双押那只手两个键加另一只手一个键；四押四个键。
/// 双押砖（关卡事件 Multitap）一块砖要按几下就算几块</item>
/// <item>每个单押只看和前面几下的间隔（R = ResetWindow）：慢（&gt; R）右手食指连点；R/2–R 两只手食指轮流；
/// R/2 以内同一只手两根手指轮指；停下来（&gt; R）总是回到右手食指</item>
/// <item>轮指默认外侧手指先落：K J D F（用户习惯的 4312）</item>
/// <item>双押优先右手（J K），ChordsOnOtherHand 时优先左手（D F）；那只手刚按过来不及才放另一只手</item>
/// <item>紧挨着双押、隔得不超过 ChordFocusWindow 的单押放另一只手，让按双押的手专心按双押</item>
/// <item>同一只手不出现「食指→外侧手指」（F→D、J→K，用户说的 21 式）：外轮换不了顺序时改按另一只手的食指，
/// 下一下要让给某根食指时这一下先用同一只手的外侧手指（D→F 或 K→J）</item>
/// <item>同一条轨道不在 JackWindow 内连续按两下</item>
/// <item>上一下的长按到这一下还按着（连续的长按砖，游戏里要新按一下才进下一块）：这一下换另一只手接，
/// 单押用那只手的食指，多押避开按着的键（用户实测同一个键松开再按、同一只手接几乎按不了）</item>
/// </list>
/// 规则是确定的：同一张图每次排出来都一样，节奏相同的地方形状也相同。
/// 以上按右手为主手写；左撇子（LeftHanded）把结果整体左右镜像。
/// 6K、8K（Keys）另有一套排法（PlanManyKeys，见 NotePlanner.ManyKeys.cs）；多押怎么合、提示有几个和 4K 一样。
/// </summary>
public static partial class NotePlanner
{
    /// <summary>比较时间时的容差，避免浮点误差把正好卡在门槛上的间隔判错。</summary>
    private const double Epsilon = 1e-6;

    /// <summary>
    /// 比较转角时的容差（度）。复现（用户截图）：游戏里方向存成单精度浮点，30° 读出来是 30.000004° 或 29.999996°，
    /// 卡在 30° 的门槛上时有的三押合上、有的合不上。谱师用的转角都是整数度，0.1° 足够吸收误差又不会把 31° 算进来。
    /// </summary>
    private const double AngleTolerance = 0.1;

    // 四个键：D（左手外侧）F（左手食指）J（右手食指）K（右手外侧）
    private const int LeftOuter = 0;
    private const int LeftIndex = 1;
    private const int RightIndex = 2;
    private const int RightOuter = 3;

    /// <param name="tiles">需要按键的砖块，按时间排好。</param>
    public static List<PlannedNote> Plan(IReadOnlyList<TileHit> tiles, FingeringOptions options)
    {
        if (!KeyLayout.IsMode(options.Keys))
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Keys, "键数只能是 4、6、8。");
        }

        if (options.Style == FingeringStyle.ReviewPreferenceV1)
        {
            ValidateReviewOptions(options);
        }

        if (options.Keys > Lanes.Count)
        {
            return PlanManyKeys(tiles, options, KeyLayout.Of(options.Keys));
        }

        var moments = Moments(tiles, options);
        var notes = new List<PlannedNote>(moments.Count);
        var rolled = new List<bool>(moments.Count);
        var hands = new HandState();
        // The feedback contains no holds. Preserve the complete time-reset plan
        // for a chart with holds until that interaction has been reviewed.
        var reviewPreference = options.Style == FingeringStyle.ReviewPreferenceV1 && !tiles.Any(hit => hit.HoldSeconds > 0);
        var focus = FocusChords(moments, options, reviewPreference);
        var preferredChordHand = options.ChordsOnOtherHand ? 0 : 1;
        // 后面的双押还没排，先按偏好的手算：紧挨着它的单押要让给另一只手的食指
        var yieldIndex = Lanes.IndexLane(1 - preferredChordHand);
        var yieldOuter = Lanes.Partner(yieldIndex);
        for (var i = 0; i < moments.Count; i++)
        {
            var moment = moments[i];
            var time = moment[0].Time;
            var last = notes.Count > 0 ? notes[notes.Count - 1] : null;
            if (moment.Count > 1)
            {
                var chordHand = ChordHand(preferredChordHand, time, last, options);
                notes.Add(BuildChord(moment, chordHand, last, options));
                rolled.Add(false);
            }
            else
            {
                var (lane, roll) = hands.Next(time, options);
                if (focus[i] >= 0)
                {
                    // 前面的双押已经排好（一个时刻一个音符，序号相同）；后面的还没排，按偏好的手算
                    var chordHand = focus[i] < i ? notes[focus[i]].PairHand : preferredChordHand;
                    // 用户：右手单押接右手双押，食指、中指配合不过来。双押附近的单押放另一只手，让那只手专心按双押
                    (lane, roll) = (FocusLane(1 - chordHand, time, last, options), false);
                }

                if (lane == yieldIndex && i + 1 < moments.Count && focus[i + 1] == i + 2
                    && moments[i + 1][0].Time - time < options.JackWindow - Epsilon && !Blocked(yieldOuter, time, last, options))
                {
                    // 下一下要让给这根食指、又挨得太近：这一下先用同一只手的外侧手指，下一下再按食指（从外向内）。
                    // 双押在右手时是 D→F，在左手时是 K→J；不然下一下只能 F→D 或 J→K（用户：不要有 21 式）
                    (lane, roll) = (yieldOuter, false);
                }

                var holding = HoldingHand(last, time, KeyLayout.Four);
                if (holding >= 0 && Lanes.HandOf(lane) == holding)
                {
                    // 上一下的长按到这一下还按着：换另一只手接（2026-11-20 用户实测：同一个键要掐着最后一点松开再按，
                    // 同一只手另一根手指接也别扭，几乎按不了）。连着的长按就是 J F J F 左右手接力
                    (lane, roll) = (FocusLane(1 - holding, time, last, options), false);
                }

                if (Blocked(lane, time, last, options))
                {
                    (lane, roll) = (NearestFreeLane(lane, time, options, last!, KeyLayout.Four), false);
                }

                var note = new PlannedNote(moment[0].TileIndex, time);
                note.Lanes[lane] = moment[0].HoldSeconds;
                notes.Add(note);
                rolled.Add(roll);
            }

            hands.Pressed(notes[notes.Count - 1]);
        }

        if (options.UseRoll && options.OuterRoll)
        {
            OuterFirst(notes, rolled, options);
        }

        if (reviewPreference)
        {
            ReviewPhrases(notes, options);
        }

        return options.LeftHanded ? notes.ConvertAll(note => Mirror(note, KeyLayout.Four)) : notes;
    }

    private static void ValidateReviewOptions(FingeringOptions o)
    {
        void Nonnegative(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                throw new ArgumentOutOfRangeException(name, "审核偏好参数必须是有限非负数。");
        }

        Nonnegative(o.ReviewShortMinGap, nameof(o.ReviewShortMinGap));
        Nonnegative(o.ReviewShortMaxGap, nameof(o.ReviewShortMaxGap));
        Nonnegative(o.ReviewTimingTolerance, nameof(o.ReviewTimingTolerance));
        if (o.ReviewShortMinGap > o.ReviewShortMaxGap)
            throw new ArgumentException("审核偏好短串最小间隔不能大于最大间隔。");
        if (o.ReviewShortMinNotes < 2 || o.ReviewShortMinNotes > o.ReviewShortMaxNotes)
            throw new ArgumentException("审核偏好短串至少需要两个音符，最小值不能大于最大值。");
    }

    /// <summary>左右镜像（4K：D↔K、F↔J；6K、8K 同一根手指换到另一只手）：左撇子的排法就是右手为主时的镜像，规则只写一份，两边始终一致。</summary>
    private static PlannedNote Mirror(PlannedNote note, KeyLayout layout)
    {
        var mirrored = new PlannedNote(note.TileIndex, note.Time) { LastTileIndex = note.LastTileIndex };
        foreach (var kv in note.Lanes)
        {
            mirrored.Lanes[layout.Mirror(kv.Key)] = kv.Value;
        }

        return mirrored;
    }

    /// <summary>
    /// 两只手的状态：上一下用的是哪根手指、右手最后一次按是什么时候。
    /// 下一下按哪根手指只看这些和间隔（LaneReader「时间重置法」的状态表，主手是右手）。
    /// </summary>
    private sealed class HandState
    {
        private int last = -1;
        private double lastTime;
        private double lastRight = double.NegativeInfinity;

        /// <returns>这一下的轨道，以及它是不是轮指出来的（外侧手指，外轮时要和前一下换位置）。</returns>
        public (int Lane, bool Roll) Next(double time, FingeringOptions o)
        {
            var r = o.ResetWindow;
            var d = time - lastTime;
            switch (last)
            {
                case RightIndex when d > r + Epsilon:
                    return (RightIndex, false); // 慢：一根手指点得过来
                case RightIndex when d > (r / 2) + Epsilon || !o.UseRoll:
                    return (LeftIndex, false); // 中速：两只手食指轮流
                case RightIndex:
                    return (RightOuter, true); // 太快：右手两根手指轮指
                case RightOuter:
                    return d > r + Epsilon ? (RightIndex, false) : (LeftIndex, false);
                case LeftIndex when o.UseRoll && (d <= (r / 2) + Epsilon || time - lastRight <= r + Epsilon):
                    return (LeftOuter, true); // 右手刚轮过、左手也得轮
                default:
                    return (RightIndex, false); // 起手、左手外侧之后、停顿之后：回到右手食指
            }
        }

        /// <summary>
        /// 记下实际按的（可能被双押、连按检查改过）。多押当成按两个键那只手的外侧手指按的：右手双押之后接左手，左手双押之后回右手；
        /// 三押、四押里右手也按了，就算右手刚按过。
        /// </summary>
        public void Pressed(PlannedNote note)
        {
            last = note.IsChord ? (note.PairHand == 1 ? RightOuter : LeftOuter) : note.Lanes.Keys.First();
            lastTime = note.Time;
            if (note.Lanes.Keys.Any(lane => Lanes.HandOf(lane) == 1))
            {
                lastRight = note.Time;
            }
        }
    }

    /// <summary>
    /// 每个单押要给哪个双押让手（双押的时刻序号），-1 表示不用让。只有紧挨着双押（前一下或后一下就是双押）、
    /// 而且隔得不超过 ChordFocusWindow 的单押才让；前后都是双押时看离得近的，一样近看后面的。
    /// 中间的单押照常排，不整串压在一只手上 F D 轮流（用户：不要有 21 式、从内向外的楼梯）。
    /// </summary>
    private static int[] FocusChords(List<List<TileHit>> moments, FingeringOptions o, bool reviewPreference)
    {
        var n = moments.Count;
        var focus = new int[n];
        // Reuse the configured recovery interval, not a hidden learned threshold.
        var focusWindow = reviewPreference ? Math.Min(o.ChordFocusWindow, o.ResetWindow) : o.ChordFocusWindow;
        for (var i = 0; i < n; i++)
        {
            focus[i] = -1;
            if (moments[i].Count > 1)
            {
                continue;
            }

            var time = moments[i][0].Time;
            var before = i > 0 && moments[i - 1].Count > 1 ? time - moments[i - 1][0].Time : double.PositiveInfinity;
            var after = i + 1 < n && moments[i + 1].Count > 1 ? moments[i + 1][0].Time - time : double.PositiveInfinity;
            if (after <= focusWindow + Epsilon && after <= before + Epsilon)
            {
                focus[i] = i + 1;
            }
            else if (before <= focusWindow + Epsilon)
            {
                focus[i] = i - 1;
            }
        }

        return focus;
    }

    /// <summary>
    /// 审核偏好：按完整单押片段选择交互相位。中速用两食指，符合可配置边界的短轮指段才尝试改成交互。
    /// 多押保持上游的分组和用手；片段两端以所有实际轨道检查连击，不能把三押只当作一只手。
    /// </summary>
    private static void ReviewPhrases(List<PlannedNote> notes, FingeringOptions o)
    {
        for (var start = 0; start < notes.Count;)
        {
            if (notes[start].IsChord)
            {
                start++;
                continue;
            }

            var end = start;
            while (end + 1 < notes.Count && !notes[end + 1].IsChord
                && notes[end + 1].Time - notes[end].Time <= o.ResetWindow + Epsilon)
            {
                end++;
            }

            var length = end - start + 1;
            if (length >= 2)
            {
                var medium = true;
                var shortRoll = o.UseRoll && o.OuterRoll
                    && length >= o.ReviewShortMinNotes && length <= o.ReviewShortMaxNotes;
                var firstGap = notes[start + 1].Time - notes[start].Time;
                for (var i = start + 1; i <= end; i++)
                {
                    var gap = notes[i].Time - notes[i - 1].Time;
                    medium &= gap > (o.ResetWindow / 2) + Epsilon;
                    shortRoll &= gap >= o.ReviewShortMinGap - Epsilon
                        && gap <= Math.Min(o.ReviewShortMaxGap, o.ResetWindow / 2) + Epsilon
                        && Math.Abs(gap - firstGap) <= o.ReviewTimingTolerance + Epsilon;
                }

                if (medium || shortRoll)
                {
                    var preferred = RightIndex;
                    if (start > 0 && notes[start - 1].IsChord
                        && notes[start].Time - notes[start - 1].Time <= Math.Min(o.ChordFocusWindow, o.ResetWindow) + Epsilon)
                    {
                        // PairHand follows ChordsOnOtherHand and the two-key hand of
                        // a triple. The following checks still inspect its complete lane set.
                        preferred = Lanes.IndexLane(1 - notes[start - 1].PairHand);
                    }

                    var firstLane = ReviewPhaseFits(notes, start, end, preferred, o) ? preferred
                        : ReviewPhaseFits(notes, start, end, OtherIndex(preferred), o) ? OtherIndex(preferred) : -1;
                    if (firstLane >= 0)
                    {
                        for (var i = start; i <= end; i++)
                        {
                            notes[i] = WithLane(notes[i], (i - start) % 2 == 0 ? firstLane : OtherIndex(firstLane));
                        }
                    }
                }
            }

            start = end + 1;
        }
    }

    private static int OtherIndex(int lane) => lane == RightIndex ? LeftIndex : RightIndex;

    private static bool ReviewPhaseFits(List<PlannedNote> notes, int start, int end, int firstLane, FingeringOptions o)
    {
        var lastLane = (end - start) % 2 == 0 ? firstLane : OtherIndex(firstLane);
        if (start > 0 && !ReviewBoundaryFits(notes[start - 1], notes[start].Time, firstLane, true, o))
        {
            return false;
        }

        return end + 1 >= notes.Count || ReviewBoundaryFits(notes[end + 1], notes[end].Time, lastLane, false, o);
    }

    private static bool ReviewBoundaryFits(PlannedNote neighbour, double time, int lane, bool neighbourBefore, FingeringOptions o)
    {
        if (Jacks(neighbour, lane, time, o))
        {
            return false;
        }

        if (!o.UseRoll || !o.OuterRoll || neighbour.IsChord || Math.Abs(neighbour.Time - time) > o.ResetWindow + Epsilon)
        {
            return true;
        }

        var other = neighbour.Lanes.Keys.First();
        var from = neighbourBefore ? other : lane;
        var to = neighbourBefore ? lane : other;
        return !((from == LeftIndex && to == LeftOuter) || (from == RightIndex && to == RightOuter));
    }

    /// <summary>让手的单押按这只手的食指；食指刚按过（会连按）才用外侧手指。</summary>
    private static int FocusLane(int hand, double time, PlannedNote? last, FingeringOptions o)
    {
        var index = Lanes.IndexLane(hand);
        return Blocked(index, time, last, o) ? Lanes.Partner(index) : index;
    }

    /// <summary>
    /// 把同时到的砖合成一个时刻（双押、三押、四押，含伪多押）。有转角时看转角：和上一块之间转得很小（≤ ChordAngle）、
    /// 又挨得近（≤ ChordMaxGap）就是同一下，一块接一块串起来；读不到转角时和这一组第一块相隔不到 ChordWindow 算同一下。
    /// 双押砖（TileHit.Taps ≥ 2）要按几下就在时刻里占几个位置，BuildChord 一个位置给一个键，所以它和挨着的砖合成的伪多押一样排（双押同手）。
    /// 一共超过 4 下：4K 只有四个键，是连打不是多押，按砖全部拆开，每块砖自己最多 4 个键。
    /// 这里的 4 是 4K 的键数（Lanes.Count），6K、8K 也一样：多押怎么合不随键数变，提示的个数和分组和 4K 一样（ChordPresses 再给超过 4 下的双押砖补键）。
    /// </summary>
    private static List<List<TileHit>> Moments(IReadOnlyList<TileHit> tiles, FingeringOptions o)
    {
        var moments = new List<List<TileHit>>();
        foreach (var tile in tiles)
        {
            if (moments.Count > 0 && SamePress(moments[moments.Count - 1], tile, o))
            {
                moments[moments.Count - 1].Add(tile);
            }
            else
            {
                moments.Add(new List<TileHit> { tile });
            }
        }

        return moments.SelectMany(m => m.Sum(Presses) > Lanes.Count ? m.Select(t => Expand(new[] { t })) : new[] { Expand(m) }).ToList();
    }

    /// <summary>这块砖要按几下（至少 1）。</summary>
    private static int Presses(TileHit tile) => Math.Max(1, tile.Taps);

    /// <summary>每块砖按几下就放几份，最多 4 份（四个键）。</summary>
    private static List<TileHit> Expand(IEnumerable<TileHit> tiles)
    {
        var presses = new List<TileHit>();
        foreach (var tile in tiles)
        {
            for (var k = 0; k < Presses(tile) && presses.Count < Lanes.Count; k++)
            {
                presses.Add(tile);
            }
        }

        return presses;
    }

    /// <summary>tile 和 group 是不是同一下。玩家反馈旧版只看时间，把快速轮指段识别成双押：极快的连打转角正常、只是 BPM 高。</summary>
    private static bool SamePress(List<TileHit> group, TileHit tile, FingeringOptions o) =>
        double.IsNaN(tile.Angle)
            ? tile.Time - group[0].Time < o.ChordWindow
            : tile.Angle <= o.ChordAngle + AngleTolerance && tile.Time - group[group.Count - 1].Time <= o.ChordMaxGap + Epsilon;

    /// <summary>
    /// 用户：双押优先右手（J K）；双押选了另一只手时优先左手（D F）。偏好的手上一下刚按过、来不及时才放另一只手。
    /// </summary>
    private static int ChordHand(int preferred, double time, PlannedNote? last, FingeringOptions o)
    {
        bool HandBlocked(int hand) => Blocked(hand * 2, time, last, o) || Blocked((hand * 2) + 1, time, last, o);
        return HandBlocked(preferred) && !HandBlocked(1 - preferred) ? 1 - preferred : preferred;
    }

    /// <summary>
    /// 双押只能是同一只手的两根手指：轨道 0+1 或 2+3，绝不跨手。三押（用户：跟着双押用手走）是这只手两个键、
    /// 另一只手的食指补一个（食指刚按过、会连按才用外侧手指）；四押四个键全按。砖按先后依次落在
    /// 这只手食指、这只手外侧、另一只手补的键、剩下那个键上，长按跟着各自的砖。
    /// </summary>
    private static PlannedNote BuildChord(List<TileHit> moment, int hand, PlannedNote? last, FingeringOptions o)
    {
        var first = moment[0];
        var note = new PlannedNote(first.TileIndex, first.Time) { LastTileIndex = moment[moment.Count - 1].TileIndex };
        var index = Lanes.IndexLane(hand);
        var otherIndex = Lanes.IndexLane(1 - hand);
        var extra = Blocked(otherIndex, first.Time, last, o) && !Blocked(Lanes.Partner(otherIndex), first.Time, last, o)
            ? Lanes.Partner(otherIndex)
            : otherIndex;
        var order = new[] { index, Lanes.Partner(index), extra, Lanes.Partner(extra) };
        for (var k = 0; k < moment.Count; k++)
        {
            note.Lanes[order[k]] = moment[k].HoldSeconds;
        }

        return note;
    }

    /// <summary>
    /// 外轮：每个轮指出来的外侧手指和它前一下（同一只手的食指）换位置，外侧手指先落。
    /// J K F D 变成 K J D F。换了会造成连按的不换。
    /// </summary>
    private static void OuterFirst(List<PlannedNote> notes, List<bool> rolled, FingeringOptions o)
    {
        for (var i = 1; i < notes.Count; i++)
        {
            var (prev, cur) = (notes[i - 1], notes[i]);
            if (!rolled[i] || prev.IsChord || cur.Time - prev.Time > o.ResetWindow + Epsilon)
            {
                continue;
            }

            var outer = cur.Lanes.Keys.First();
            var index = prev.Lanes.Keys.First();
            if (Lanes.Partner(outer) != index)
            {
                continue;
            }

            if ((i >= 2 && Jacks(notes[i - 2], outer, prev.Time, o))
                || (i + 1 < notes.Count && Jacks(notes[i + 1], index, cur.Time, o)))
            {
                // 换了会连按（前后夹着让手的单押）：这一下改按另一只手的食指，左右手轮流，
                // 不留「食指→外侧手指」（用户：不要有 21 式）
                // 这一下的长按要按到下一下、下一下又在另一只手上接力时不换：换过去就成了同一只手接（TUF 3303 的 3260、3261）
                var other = Lanes.IndexLane(1 - Lanes.HandOf(outer));
                var next = i + 1 < notes.Count ? notes[i + 1] : null;
                if (!Jacks(prev, other, cur.Time, o)
                    && !(next is not null && (Jacks(next, other, cur.Time, o) || HandsOverTo(cur, next, Lanes.HandOf(other)))))
                {
                    notes[i] = WithLane(cur, other);
                }

                continue;
            }

            notes[i - 1] = WithLane(prev, outer);
            notes[i] = WithLane(cur, index);
        }
    }

    /// <summary>note 的长按按到下一下（next）还没松开，而 next 有键在 hand 这只手上：note 放到 hand 就成了同一只手接力。</summary>
    private static bool HandsOverTo(PlannedNote note, PlannedNote next, int hand) =>
        note.Lanes.Values.Any(hold => StillHeld(note, hold, next.Time)) && next.Lanes.Keys.Any(lane => Lanes.HandOf(lane) == hand);

    /// <summary>在 time 改按 lane 会不会和相邻那一下（neighbour）同轨又太近。</summary>
    private static bool Jacks(PlannedNote neighbour, int lane, double time, FingeringOptions o) =>
        neighbour.Lanes.ContainsKey(lane) && Math.Abs(neighbour.Time - time) < o.JackWindow - Epsilon;

    private static PlannedNote WithLane(PlannedNote note, int lane)
    {
        var copy = new PlannedNote(note.TileIndex, note.Time) { LastTileIndex = note.LastTileIndex };
        copy.Lanes[lane] = note.Lanes.Values.First();
        return copy;
    }

    /// <summary>
    /// 这条轨道用不了：上一下刚按过（同一条轨道连续两下间隔不到 JackWindow），或者上一下在这条轨道上的长按到这一下还没松开。
    /// 双押选手、三押补键、让手的单押都看它，所以多押会自动避开按着的键。
    /// </summary>
    private static bool Blocked(int lane, double time, PlannedNote? last, FingeringOptions o) =>
        last is not null && last.Lanes.TryGetValue(lane, out var hold)
        && (time - last.Time < o.JackWindow - Epsilon || StillHeld(last, hold, time));

    /// <summary>上一下按着的长按到 time 还没松开（连续的长按砖：前一块按满的那一刻正好要按下一块）。</summary>
    private static bool StillHeld(PlannedNote last, double hold, double time) => hold > 0 && last.Time + hold > time - Epsilon;

    /// <summary>上一下的长按到这一下还没松开时，按着长按的那只手；没有、或两只手都按着时是 -1。</summary>
    private static int HoldingHand(PlannedNote? last, double time, KeyLayout layout)
    {
        if (last is null)
        {
            return -1;
        }

        var hands = last.Lanes.Where(kv => StillHeld(last, kv.Value, time)).Select(kv => layout.HandOf(kv.Key)).Distinct().ToList();
        return hands.Count == 1 ? hands[0] : -1;
    }

    /// <summary>
    /// 单押和上一下同轨又太近（通常紧跟在双押后面）：换到上一下没用的轨道里离上一下最近的那条（一样近时取离原位置近的），
    /// 例如 ##.. 之后本来要按 0，就改按 2，接得上又不连按。
    /// </summary>
    private static int NearestFreeLane(int lane, double time, FingeringOptions o, PlannedNote last, KeyLayout layout)
    {
        var best = lane;
        var bestScore = int.MaxValue;
        for (var c = 0; c < layout.Count; c++)
        {
            if (Blocked(c, time, last, o))
            {
                continue;
            }

            var score = (last.Lanes.Keys.Min(p => Math.Abs(p - c)) * 10) + Math.Abs(c - lane);
            if (score < bestScore)
            {
                best = c;
                bestScore = score;
            }
        }

        return best;
    }
}
