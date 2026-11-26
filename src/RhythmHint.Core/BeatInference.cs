using System;
using System.Collections.Generic;
using System.Linq;

namespace RhythmHint.Core;

/// <summary>节拍线的拍子从哪来。</summary>
public enum BeatSource
{
    /// <summary>没有砖块，没画节拍线。</summary>
    None,

    /// <summary>关卡的 BPM（按 2 的整数次幂折到合理范围）。</summary>
    Level,

    /// <summary>关卡 BPM 和砖块对不上，从砖块间隔推出来。</summary>
    Tiles,
}

/// <summary>一段速度不变的拍子：从 Start 起，拍点在 Anchor + k × SecondsPerBeat；Restart 表示这里重新数小节。</summary>
public readonly record struct TempoSection(double Start, double Anchor, double SecondsPerBeat, bool Restart)
{
    public double Bpm => 60 / SecondsPerBeat;

    public override string ToString() => $"{Start:F3}s 起 {Bpm:0.###} BPM（拍点 {Anchor:F3}s）{(Restart ? "，重新起小节" : string.Empty)}";
}

/// <summary>推出来的节拍：拍线（含小节线）和用来核对的摘要。</summary>
public sealed class BeatMap
{
    public List<BeatLine> Lines { get; } = new();

    public List<TempoSection> Sections { get; } = new();

    /// <summary>主 BPM：按持续时长最长的那段拍子。</summary>
    public double MainBpm { get; set; }

    public BeatSource Source { get; set; }

    /// <summary>写日志、离线预览用的一句摘要（中文，不给玩家看）。</summary>
    public string Summary()
    {
        var measures = Lines.Count(l => l.IsMeasure);
        var restarts = Sections.Count(s => s.Restart);
        var source = Source == BeatSource.Tiles ? "从砖块间隔推" : "关卡 BPM";
        return $"主 BPM {MainBpm:0.##}（{source}），{Lines.Count} 条节拍线、{measures} 个小节、{restarts} 处重新起小节";
    }
}

/// <summary>
/// 给节拍线推音乐的拍子和小节（只用来画，不影响排键和难度估算）。
/// 拍和小节分两层：拍子跨变速连续数，变速点只换每拍秒数；只有变速点不落在拍上时才重新起小节，小节起点按砖块的重音猜。
/// </summary>
internal static class BeatInference
{
    /// <summary>候选 BPM 范围；关卡 BPM 落在里面、砖块又对得上就直接用，不在就按 2 的整数次幂折进 100–200。</summary>
    private const double MinBpm = 60, MaxBpm = 300, PreferredMin = 100;

    /// <summary>同一类 BPM（差 2 的整数次幂）的容差，按 log2 算（约 0.014%，只容得下小数位的舍入）。</summary>
    private const double ClassTolerance = 2e-4;

    /// <summary>MainClass 按 log2 的小数部分分几个桶（见 ClassBucket）。</summary>
    private const int ClassBuckets = 1024;

    /// <summary>认细分时试的分母，越靠前越「整」；一格短于 MinStep 的不试（慢拍子里的 1/64 也算，快拍子里不算）。</summary>
    private static readonly int[] Divisions = { 1, 2, 4, 3, 8, 6, 16, 12, 32, 24, 64, 48 };

    /// <summary>细分一格至少多少秒。</summary>
    private const double MinStep = 0.006;

    /// <summary>几拍之内就回到原来速度的变速当作写法（伪双押、挤进一拍的连打、短的连音），不当作换了速度。</summary>
    private const double QuickBeats = 8;

    /// <summary>写法上的变速最多几段就回到原来的速度。</summary>
    private const int QuickSegments = 4;

    /// <summary>相隔不到这么久（秒）的两下当作同时按。</summary>
    private const double SameTime = 0.012;

    /// <summary>砖块有这么多落在拍网上就算对得上。</summary>
    private const double GoodFit = 0.9;

    /// <summary>关卡 BPM 的拍网只对得上这么少，才试着从砖块推拍子。</summary>
    private const double PoorFit = 0.6;

    /// <summary>一下（同时按的几块砖算一下）：几块、有没有长按、和上一下隔多久。</summary>
    private readonly record struct Onset(double Time, int Size, bool Hold, double Gap);

    /// <summary>关卡的一个变速段；FromTiles 表示 Spb 不是关卡写的，是从砖块间隔推出来的。</summary>
    private readonly record struct Segment(double Start, double End, double Spb, bool FromTiles = false);

    /// <summary>
    /// 一拍、起止时间不是有限数时不画；变速点、砖块时间不是有限数的那一项跳过（Segments、Onsets）。
    /// 复现（2026-11-19 评审）：一拍 +∞、速度 NaN 或 ∞ 时主 BPM 那一类找不到段，First() 抛 InvalidOperationException。
    /// </summary>
    internal static BeatMap Infer(IReadOnlyList<TileHit> hits, IReadOnlyList<(double Time, double Speed)> speeds, double crotchet,
        double start, double end, bool mergeOnsets = true)
    {
        var map = new BeatMap();
        var onsets = Onsets(hits, mergeOnsets);
        if (onsets.Count == 0 || !(crotchet > 0) || !IsFinite(crotchet) || !IsFinite(start) || !IsFinite(end) || end <= start)
        {
            return map;
        }

        var segments = Segments(speeds, crotchet, start, end, onsets[0].Time);
        for (var i = 0; i < segments.Count; i++)
        {
            segments[i] = FromTiles(segments[i], onsets);
        }

        var mainLevel = MainClass(segments);
        var mainSpb = MusicSpb(segments, mainLevel);
        PlanSections(map.Sections, segments, onsets, mainLevel, mainSpb);
        map.Source = segments.Any(s => s.FromTiles && SameClass(s.Spb, mainLevel)) ? BeatSource.Tiles : BeatSource.Level;
        map.MainBpm = 60 / mainSpb;
        BuildLines(map, onsets, start, end);
        return map;
    }

    /// <summary>
    /// 合并同时按的砖（排键默认的多押规则，再加上和这一组第一块相隔不到 SameTime 的：有的谱用两个极端 BPM 交替、180° 转角画伪双押），
    /// 记下多押几块、长按、和上一下隔多久。
    /// </summary>
    private static List<Onset> Onsets(IReadOnlyList<TileHit> hits, bool mergeOnsets)
    {
        var o = new FingeringOptions();
        var onsets = new List<Onset>();
        double groupStart = 0, last = double.NegativeInfinity;
        foreach (var h in hits)
        {
            if (!IsFinite(h.Time))
            {
                continue; // 坏数据：画不出来，也不能让它打乱二分查找
            }

            var same = mergeOnsets && onsets.Count > 0 && (h.Time - groupStart < SameTime || (double.IsNaN(h.Angle)
                ? h.Time - groupStart < o.ChordWindow
                : h.Angle <= o.ChordAngle + 1e-3 && h.Time - last <= o.ChordMaxGap + 1e-6));
            if (same)
            {
                var g = onsets[onsets.Count - 1];
                onsets[onsets.Count - 1] = g with { Size = g.Size + 1, Hold = g.Hold || h.HoldSeconds > 0 };
            }
            else
            {
                onsets.Add(new Onset(h.Time, 1, h.HoldSeconds > 0, onsets.Count == 0 ? double.PositiveInfinity : h.Time - last));
                groupStart = h.Time;
            }

            last = h.Time;
        }

        return onsets;
    }

    /// <summary>
    /// 关卡的变速段（每段一个砖块速度）；第一块要按的砖之前的段并进后面那段。
    /// 时间或速度不是有限数的变速点当作没有；算出的每拍秒数不是正的有限数（速度太大、太小）的段跳过。
    /// </summary>
    private static List<Segment> Segments(IReadOnlyList<(double Time, double Speed)> levelSpeeds, double crotchet, double start, double end, double firstOnset)
    {
        var speeds = new List<(double Time, double Speed)>(levelSpeeds.Count);
        foreach (var s in levelSpeeds)
        {
            if (IsFinite(s.Time) && IsFinite(s.Speed))
            {
                speeds.Add(s);
            }
        }

        var segments = new List<Segment>();
        for (var i = 0; i < speeds.Count; i++)
        {
            var (time, speed) = speeds[i];
            var next = i + 1 < speeds.Count ? speeds[i + 1].Time : end;
            var spb = crotchet / speed;
            if (speed <= 0 || next <= start || time >= end || !(spb > 0) || double.IsInfinity(spb))
            {
                continue;
            }

            var from = Math.Max(start, time);
            if (segments.Count > 0 && (from <= firstOnset + 1e-6 || Math.Abs(Math.Log(spb / segments[segments.Count - 1].Spb)) < 1e-9))
            {
                // 还没到第一下：前面那段没有砖，用这一段的速度；速度一样的相邻段合并
                var prev = segments[segments.Count - 1];
                segments[segments.Count - 1] = from <= firstOnset + 1e-6 ? new Segment(prev.Start, Math.Min(end, next), spb) : prev with { End = Math.Min(end, next) };
                continue;
            }

            segments.Add(new Segment(from, Math.Min(end, next), spb));
        }

        if (segments.Count == 0)
        {
            segments.Add(new Segment(start, end, crotchet));
        }

        return segments;
    }

    /// <summary>
    /// 关卡 BPM 和砖块对不上时（谱师按转角凑节奏：比如 64.711 BPM 配 182° 的砖，每下正好 64 BPM 的一拍），从砖块间隔推这一段的拍子：
    /// 拿最常见的几种间隔，试它的 1、2、3、4 倍和几种简单分数当一拍，挑砖块落在拍网上最多的（要有九成）；八度取离关卡 BPM 最近的。
    /// 砖少（不到 8 下）或推不出来就照旧用关卡的。
    /// </summary>
    private static Segment FromTiles(Segment seg, List<Onset> onsets)
    {
        var inSeg = Range(onsets, seg.Start, seg.End + 1e-5).ToList();
        if (inSeg.Count < 8)
        {
            return seg;
        }

        // 长段只看前 256 下，免得长谱进关卡时卡顿
        var until = inSeg.Count > 256 ? inSeg[255].Time : seg.End;
        var levelShare = PhasedShare(onsets, seg.Start, until, seg.Spb);
        if (levelShare >= PoorFit)
        {
            return seg;
        }

        var best = seg;
        var bestShare = Math.Max(GoodFit, levelShare + 0.3);
        foreach (var ioi in CommonGaps(inSeg))
        {
            foreach (var multiple in new[] { 1, 2, 4, 3, 0.5, 1.5, 2.0 / 3, 4.0 / 3, 0.75, 6, 8 })
            {
                var spb = Fold(ioi * multiple, seg.Spb);
                spb = Nearest(spb, spb); // 只是夹进 60–300
                var share = PhasedShare(onsets, seg.Start, until, spb);
                if (share > bestShare + 1e-9)
                {
                    best = seg with { Spb = spb, FromTiles = true };
                    bestShare = share;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// 最常见的三种相邻间隔（相差 0.3% 以内算一种，取平均），50 ms–2.5 s 之外的不算。
    /// 和 FromTiles 算比例一样只看前 256 下（2026-11-19 起；工坊和 TUF 的 866 张谱推出的节拍线和原来看整段的逐条一样）。
    /// </summary>
    private static IEnumerable<double> CommonGaps(List<Onset> onsets)
    {
        var gaps = new List<double>();
        for (var i = 1; i < Math.Min(onsets.Count, 256); i++)
        {
            var gap = onsets[i].Time - onsets[i - 1].Time;
            if (gap >= 0.05 && gap <= 2.5)
            {
                gaps.Add(gap);
            }
        }

        gaps.Sort();
        var clusters = new List<(double Sum, int Count)>();
        var from = 0;
        var sum = 0.0;
        for (var i = 0; i < gaps.Count; i++)
        {
            if (gaps[i] > gaps[from] * 1.003)
            {
                clusters.Add((sum, i - from));
                (from, sum) = (i, 0.0);
            }

            sum += gaps[i]; // 按顺序从 0 加起，和 Enumerable.Sum 的顺序一样；不用 Skip().Take()：游戏的 Mono 上 Skip 每次都从头数
        }

        if (gaps.Count > 0)
        {
            clusters.Add((sum, gaps.Count - from));
        }

        return clusters.OrderByDescending(c => c.Count).Take(3).Select(c => c.Sum / c.Count);
    }

    /// <summary>[from, to] 里的砖按每拍 spb、挑好拍点后落在拍网上的比例。</summary>
    private static double PhasedShare(List<Onset> onsets, double from, double to, double spb)
    {
        var anchor = ChoosePhase(onsets, from, to, spb, from);
        return Share(onsets, from, to, (from - anchor) / spb, spb);
    }

    /// <summary>
    /// 按持续时长选主 BPM 那一类（差 2 的整数次幂算同一类）的一个关卡每拍秒数。每一段归进第一个和它同一类的类（按出现的先后）。
    /// 同一类的两个数 log2 的小数部分相差不到 ClassTolerance，按小数部分分成 ClassBuckets 个桶，只和自己、左右两个桶里的类比；
    /// 原来和所有类逐个比，段数 × 类数（类数最多约 2500，2026-11-19 评审）。
    /// </summary>
    private static double MainClass(List<Segment> segments)
    {
        var classes = new List<(double Spb, double Duration)>();
        var buckets = new Dictionary<int, List<int>>();
        foreach (var s in segments)
        {
            var bucket = ClassBucket(s.Spb);
            var k = -1;
            for (var b = bucket - 1; b <= bucket + 1; b++)
            {
                if (!buckets.TryGetValue((b + ClassBuckets) % ClassBuckets, out var members))
                {
                    continue;
                }

                foreach (var c in members) // 按出现的先后排着
                {
                    if (k >= 0 && c >= k)
                    {
                        break;
                    }

                    if (SameClass(classes[c].Spb, s.Spb))
                    {
                        k = c;
                        break;
                    }
                }
            }

            if (k < 0)
            {
                if (!buckets.TryGetValue(bucket, out var members))
                {
                    buckets[bucket] = members = new List<int>();
                }

                members.Add(classes.Count);
                classes.Add((s.Spb, s.End - s.Start));
            }
            else
            {
                classes[k] = (classes[k].Spb, classes[k].Duration + s.End - s.Start);
            }
        }

        return classes.OrderByDescending(c => c.Duration).First().Spb;
    }

    /// <summary>
    /// 主 BPM 那一类用哪个八度：这一类里关卡用得最久的那个 BPM（有关卡自己写的就只看关卡写的）在 60–300 就直接用，不在就折进 100–200。
    /// </summary>
    private static double MusicSpb(List<Segment> segments, double mainLevel)
    {
        var inClass = segments.Where(s => SameClass(s.Spb, mainLevel)).ToList();
        var longest = (inClass.Any(s => !s.FromTiles) ? inClass.Where(s => !s.FromTiles) : inClass)
            .GroupBy(s => Math.Round(Math.Log(s.Spb, 2), 3))
            .Select(g => (Spb: g.First().Spb, Duration: g.Sum(s => s.End - s.Start)))
            .OrderByDescending(g => g.Duration).First().Spb;
        return InRange(60 / longest) ? longest : Fold(mainLevel, 60 / (PreferredMin * Math.Sqrt(2)));
    }

    private static bool InRange(double bpm) => bpm >= MinBpm - 1e-6 && bpm <= MaxBpm + 1e-6;

    private static bool SameClass(double a, double b)
    {
        var d = Math.Log(a / b, 2);
        return Math.Abs(d - Math.Round(d)) < ClassTolerance;
    }

    /// <summary>
    /// MainClass 分桶用：log2(每拍秒数) 的小数部分落在第几个桶。一个桶宽 1/1024，比 ClassTolerance 宽近 5 倍，
    /// 同一类的两个数（小数部分相差不到 ClassTolerance，加上浮点误差也远小于一个桶）一定在同一个或相邻的桶里（首尾两个桶相邻）。
    /// </summary>
    private static int ClassBucket(double spb)
    {
        var log = Math.Log(spb, 2);
        return (int)((log - Math.Floor(log)) * ClassBuckets);
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    /// <summary>按 2 的整数次幂把每拍秒数折到离 target 最近（log 意义下，即 target 的 0.71–1.41 倍之间）的那个。</summary>
    private static double Fold(double spb, double target)
    {
        var k = Math.Round(Math.Log(target / spb, 2));
        return spb * Math.Pow(2, k);
    }

    /// <summary>
    /// 把变速段变成音乐的拍子。拍子跨变速连续数（全曲的第几拍是连续的），每段先定速度：
    /// 主 BPM 那一类只是疏密，用主 BPM；别的段几拍之内就回到现在这一类、回来时正好落在现在的拍网上，是伪双押、挤进一拍的连打之类的写法，
    /// 整段接着用现在的速度；和现在的拍子成简单比例（3:2、4:3 之类）、砖块又对得上，是连音的写法，也接着用；
    /// 否则是真的换了速度，用这一段自己的 BPM（按 2 的整数次幂折到离现在最近的那个）。
    /// 再定接着数还是重新起：只在换了速度或变速点不落在拍上时检查，从这里换个拍点、砖块明显更落在拍上（见 Restart），才重新起拍、重新起小节。
    /// </summary>
    private static void PlanSections(List<TempoSection> sections, List<Segment> segments, List<Onset> onsets, double mainLevel, double mainSpb)
    {
        var first = segments[0];
        var curLevel = first.Spb;
        var spb0 = SameClass(first.Spb, mainLevel) ? mainSpb : Nearest(first.Spb, mainSpb);
        var firstOnset = Range(onsets, first.Start, double.PositiveInfinity).Select(o => o.Time).DefaultIfEmpty(first.Start).First(); // 变速点全在砖块后面时没有
        sections.Add(new TempoSection(first.Start, ChoosePhase(onsets, first.Start, first.End, spb0, firstOnset), spb0, false));
        var runs = SameClassRuns(segments);
        for (var i = 1; i < segments.Count; i++)
        {
            var seg = segments[i];
            var cur = sections[sections.Count - 1];
            var beat = (seg.Start - cur.Anchor) / cur.SecondsPerBeat;
            if (!SameClass(seg.Spb, curLevel) && !SameClass(seg.Spb, mainLevel) && QuickReturn(segments, i, curLevel, cur) is int back)
            {
                i = back - 1;
                continue;
            }

            var main = SameClass(seg.Spb, mainLevel);
            var spb = main ? mainSpb
                : KeepsTempo(onsets, seg, beat, cur.SecondsPerBeat) ? cur.SecondsPerBeat
                : Nearest(seg.Spb, cur.SecondsPerBeat);
            if (!Same(spb, cur.SecondsPerBeat) || main)
            {
                curLevel = seg.Spb;
            }

            // 接着数：这一拍剩下的部分按新速度走完
            var onBeat = Math.Abs(beat - Math.Round(beat)) * cur.SecondsPerBeat <= Tolerance(cur.SecondsPerBeat, 1);
            var next = onBeat ? cur.Anchor + (Math.Round(beat) * cur.SecondsPerBeat) : seg.Start + ((Math.Ceiling(beat) - beat) * spb);
            if ((!onBeat || !Same(spb, cur.SecondsPerBeat)) && Restart(segments, i, runs, onsets, spb, next) is double anchor)
            {
                sections.Add(new TempoSection(seg.Start, anchor, spb, true));
            }
            else if (!Same(spb, cur.SecondsPerBeat))
            {
                sections.Add(new TempoSection(seg.Start, next, spb, false));
            }
        }
    }

    /// <summary>
    /// 第 i 段起要不要换拍点：看这一段和后面同一类速度的几段（最多 64 拍），砖块至少 6 下；
    /// 换个拍点（这一段前几下砖之一）后整拍、半拍上的砖（重音加权）比接着数多出三成以上才换，返回新的拍点。
    /// 加速、减速那种一块砖一个速度的段看不出来，接着数。
    /// </summary>
    /// <param name="runs">每段往后同一类速度连到哪段为止（SameClassRuns）。</param>
    private static double? Restart(List<Segment> segments, int i, int[] runs, List<Onset> onsets, double spb, double next)
    {
        var seg = segments[i];
        var until = segments[runs[i] - 1].End;
        var window = Range(onsets, seg.Start, Math.Min(until, seg.Start + (64 * spb)) + 1e-5).Take(256).ToList();
        if (window.Count < 6)
        {
            return null;
        }

        var best = ChoosePhase(onsets, seg.Start, Math.Min(until, seg.Start + (64 * spb)) + 1e-5, spb, next);
        return PhaseScore(window, best, spb) >= (1.3 * PhaseScore(window, next, spb)) + 1 ? best : null;
    }

    /// <summary>
    /// 每段往后、和这一段同一类速度的段连到哪段为止：第一个不是同一类的段的下标（都是就是段数）。
    /// 同样的每拍秒数记住上一次扫到哪，那里还在后面就直接用（中间的段都和它同一类），不用再扫。
    /// 复现（2026-11-19 评审）：×1 / ×2 在不落在整拍的位置来回切时每段都要看要不要重新起拍，原来每段都往后扫完所有同一类速度的段，
    /// 1.6 万个变速点 0.76 秒、6.7 万个 8.5 秒。
    /// </summary>
    private static int[] SameClassRuns(List<Segment> segments)
    {
        var runs = new int[segments.Count];
        var scanned = new Dictionary<double, int>();
        for (var i = 0; i < segments.Count; i++)
        {
            var spb = segments[i].Spb;
            if (!scanned.TryGetValue(spb, out var j) || j <= i)
            {
                j = i + 1;
                while (j < segments.Count && SameClass(segments[j].Spb, spb))
                {
                    j++;
                }

                scanned[spb] = j;
            }

            runs[i] = j;
        }

        return runs;
    }

    /// <summary>
    /// 从第 i 段起，QuickSegments 段、现在的拍子数 QuickBeats 拍以内关卡就回到 level 这一类速度、回来的那一刻落在现在的拍网（1/16 以内）上：
    /// 中间这几段只是写法上的变速。返回回来的那一段的下标，否则 null。段数也要限制：一块砖一个速度的加速段，偶尔正好加到原来的两倍，不能当成回来了。
    /// </summary>
    private static int? QuickReturn(List<Segment> segments, int i, double level, TempoSection cur)
    {
        var limit = segments[i].Start + (QuickBeats * cur.SecondsPerBeat);
        for (var j = i + 1; j < segments.Count && j <= i + QuickSegments && segments[j].Start <= limit + 1e-6; j++)
        {
            if (SameClass(segments[j].Spb, level))
            {
                var d = Subdivision((segments[j].Start - cur.Anchor) / cur.SecondsPerBeat, cur.SecondsPerBeat);
                return d is > 0 and <= 16 ? j : null;
            }
        }

        return null;
    }

    /// <summary>
    /// 这一段接着用现在的速度（每拍 spb，段首是全曲第 beat 拍）：段里的砖对得上拍网，而且要么和现在的拍子成简单比例（连音的写法），
    /// 要么段很短（不到一拍）、接下来两拍里（至少 4 下）也都对得上——有的谱每块砖换一个速度、再用转角把时间补回来，只是为了轨道的形状。
    /// </summary>
    private static bool KeepsTempo(List<Onset> onsets, Segment seg, double beat, double spb)
    {
        if (Share(onsets, seg.Start, seg.End, beat, spb) < GoodFit)
        {
            return false;
        }

        if (SimpleRatio(spb / seg.Spb))
        {
            return true;
        }

        var until = seg.Start + (2 * spb);
        return seg.End - seg.Start <= spb + 1e-6 && Range(onsets, seg.Start, until).Count() >= 4 && Share(onsets, seg.Start, until, beat, spb) >= GoodFit;
    }

    /// <summary>每拍里有几个关卡拍是不是简单比例（分母 16 以内的常见分母）：是的话变速可能只是连音的写法，不是真的换了速度。</summary>
    private static bool SimpleRatio(double ratio)
    {
        foreach (var q in new[] { 1, 2, 3, 4, 5, 6, 8, 12, 16 })
        {
            var p = Math.Round(ratio * q);
            if (p >= 1 && p <= 16 * q && Math.Abs((ratio * q) - p) < 1e-3 * q)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Same(double a, double b) => Math.Abs(Math.Log(a / b)) < 1e-9;

    private static double Nearest(double levelSpb, double near)
    {
        var spb = Fold(levelSpb, near);
        while (60 / spb > MaxBpm)
        {
            spb *= 2;
        }

        while (60 / spb < MinBpm)
        {
            spb /= 2;
        }

        return spb;
    }

    /// <summary>
    /// 从 from（全曲第 beat 拍，可以是小数）起按每拍 spb 接着数，[from, to] 里的砖（含 to 那一下，它的时间由这一段的长度决定）
    /// 落在拍网（1/64 以内、一格不短于 MinStep 的细分）上的比例。没有砖时算 1：没有证据就不换。
    /// </summary>
    private static double Share(List<Onset> onsets, double from, double to, double beat, double spb)
    {
        int total = 0, on = 0;
        foreach (var o in Range(onsets, from, to + 1e-5))
        {
            total++;
            on += Subdivision(beat + ((o.Time - from) / spb), spb) > 0 ? 1 : 0;
        }

        return total == 0 ? 1 : on / (double)total;
    }

    /// <summary>
    /// 拍点放哪：在 fallback 和 [from, to) 第一拍里的砖（最多 32 下，拍点每拍重复，第一拍里的砖已经代表了所有可能）里挑一个，
    /// 让后面（最多 64 拍、256 下）的砖尽量落在整拍、半拍上（重音权重高）；一样好时取靠前的候选。
    /// </summary>
    private static double ChoosePhase(List<Onset> onsets, double from, double to, double spb, double fallback)
    {
        var window = Range(onsets, from, Math.Min(to, from + (64 * spb))).Take(256).ToList();
        var best = fallback;
        var bestScore = PhaseScore(window, fallback, spb);
        var firstOnset = window.Count > 0 ? window[0].Time : from;
        foreach (var c in window.TakeWhile(o => o.Time < firstOnset + spb - 1e-6).Take(32).Select(o => o.Time))
        {
            var score = PhaseScore(window, c, spb);
            if (score > bestScore + 1e-9)
            {
                best = c;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>拍点放在 anchor 时这些砖的得分：整拍 1、半拍 0.6……不在拍网上 0，重音的砖加倍计。</summary>
    private static double PhaseScore(List<Onset> window, double anchor, double spb)
    {
        var score = 0.0;
        foreach (var o in window)
        {
            score += Weight(Subdivision((o.Time - anchor) / spb, spb)) * (1 + Accent(o, spb));
        }

        return score;
    }

    /// <summary>[from, to) 里的砖（二分找起点，长谱也快）。</summary>
    private static IEnumerable<Onset> Range(List<Onset> onsets, double from, double to)
    {
        int lo = 0, hi = onsets.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (onsets[mid].Time < from - 1e-6)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        for (var i = lo; i < onsets.Count && onsets[i].Time < to - 1e-6; i++)
        {
            yield return onsets[i];
        }
    }

    /// <summary>落在整拍上最重，越碎越轻；不在网格上为 0。</summary>
    private static double Weight(int division) => division switch
    {
        1 => 1,
        2 => 0.6,
        4 or 3 => 0.4,
        8 or 6 => 0.25,
        16 or 12 => 0.1,
        _ => 0,
    };

    /// <summary>重音：多押、长按、停顿（隔了一拍半以上）后的第一下更可能在强拍上。</summary>
    private static double Accent(Onset o, double spb) =>
        (o.Size > 1 ? 1 : 0) + (o.Hold ? 1 : 0) + (o.Gap >= 1.5 * spb ? 1 : 0);

    private static double Tolerance(double spb, int division) => Math.Min(0.002, 0.03 * spb / division);

    /// <summary>第 beat 拍（可以是小数，每拍 spb 秒）落在哪种细分上：1 整拍、2 半拍、4 四分之一……；都不在返回 0。</summary>
    private static int Subdivision(double beat, double spb)
    {
        var pos = beat - Math.Floor(beat);
        foreach (var d in Divisions)
        {
            if (spb / d < MinStep)
            {
                continue;
            }

            var x = pos * d;
            if (Math.Abs(x - Math.Round(x)) * spb / d <= Tolerance(spb, d))
            {
                return d;
            }
        }

        return 0;
    }

    /// <summary>按各段的拍点生成节拍线，再按重音给每一组（重新起小节之间）挑小节起点。</summary>
    private static void BuildLines(BeatMap map, List<Onset> onsets, double start, double end)
    {
        var beats = new List<(double Time, double Spb, int Group)>();
        var group = 0;
        for (var i = 0; i < map.Sections.Count && beats.Count < BeatGrid.MaxLines; i++)
        {
            var s = map.Sections[i];
            var from = i == 0 ? start : s.Start;
            var to = i + 1 < map.Sections.Count ? map.Sections[i + 1].Start : end;
            group += s.Restart ? 1 : 0;
            var k = Math.Ceiling(((from - s.Anchor) / s.SecondsPerBeat) - 1e-6);
            for (var t = s.Anchor + (k * s.SecondsPerBeat); t < to - 1e-6 || (i + 1 == map.Sections.Count && t <= end + 1e-6); t = s.Anchor + (++k * s.SecondsPerBeat))
            {
                if (beats.Count >= BeatGrid.MaxLines)
                {
                    break;
                }

                // 重新起拍的地方：上一段最后一拍和这一拍挨得太近（不到 1/4 拍）就去掉上一段的
                if (beats.Count > 0 && t - beats[beats.Count - 1].Time < 0.25 * s.SecondsPerBeat)
                {
                    beats.RemoveAt(beats.Count - 1);
                }

                beats.Add((t, s.SecondsPerBeat, group));
            }
        }

        var phase = MeasurePhases(beats, onsets);
        var index = 0;
        for (var i = 0; i < beats.Count; i++)
        {
            index = i > 0 && beats[i].Group != beats[i - 1].Group ? 0 : index;
            map.Lines.Add(new BeatLine(beats[i].Time, (index - phase[beats[i].Group] + 400) % 4 == 0));
            index++;
        }
    }

    /// <summary>每组挑小节起点（组内第几拍是第一拍，0–3）：落在那一拍上的重音加起来最多；分不出来时从第一块要按的砖所在的拍起。</summary>
    private static Dictionary<int, int> MeasurePhases(List<(double Time, double Spb, int Group)> beats, List<Onset> onsets)
    {
        var result = new Dictionary<int, int>();
        var scores = new Dictionary<int, double[]>();
        var firstOnsetBeat = new Dictionary<int, int>();
        int j = 0, index = 0;
        for (var i = 0; i < beats.Count; i++)
        {
            index = i > 0 && beats[i].Group != beats[i - 1].Group ? 0 : index;
            var (time, spb, g) = beats[i];
            if (!scores.TryGetValue(g, out var s))
            {
                scores[g] = s = new double[4];
            }

            var tol = Tolerance(spb, 1);
            while (j < onsets.Count && onsets[j].Time < time - tol)
            {
                j++;
            }

            for (var k = j; k < onsets.Count && onsets[k].Time <= time + tol; k++)
            {
                s[index % 4] += 0.25 + Accent(onsets[k], spb);
                if (!firstOnsetBeat.ContainsKey(g))
                {
                    firstOnsetBeat[g] = index % 4;
                }
            }

            index++;
        }

        foreach (var kv in scores)
        {
            var fallback = firstOnsetBeat.TryGetValue(kv.Key, out var f) ? f : 0;
            var best = fallback;
            for (var p = 0; p < 4; p++)
            {
                if (kv.Value[p] > kv.Value[best] * 1.1 + 1e-9)
                {
                    best = p;
                }
            }

            result[kv.Key] = best;
        }

        return result;
    }
}
