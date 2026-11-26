using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace RhythmHint.Core;

public enum TrackAxis { None, Horizontal, Vertical }

/// <summary>Musical tempo in chart seconds and the incoming segment of each tile.</summary>
public sealed record FingeringRhythm(double Origin, IReadOnlyList<(double Time, double SecondsPerBeat)> Tempo,
    IReadOnlyDictionary<int, TrackAxis>? IncomingAxes = null, IReadOnlyList<BeatLine>? Beats = null)
{
    public static Dictionary<int, TrackAxis> IncomingDirections(IReadOnlyList<PathPoint> path)
    {
        var axes = new Dictionary<int, TrackAxis>();
        for (var i = 1; i < path.Count; i++)
        {
            var point = path[i];
            if (!point.Hit) continue;
            var angle = point.Entry;
            if (double.IsNaN(angle))
            {
                var dx = point.X - path[i - 1].X;
                var dy = point.Y - path[i - 1].Y;
                if (Math.Abs(dx) + Math.Abs(dy) < .0001) continue;
                angle = Math.Atan2(dy, dx);
            }
            if (Math.Abs(Math.Cos(angle)) >= .966) axes[point.Tile] = TrackAxis.Horizontal;
            else if (Math.Abs(Math.Sin(angle)) >= .966) axes[point.Tile] = TrackAxis.Vertical;
        }
        return axes;
    }
}

/// <summary>
/// 一串砖的节奏：每块砖和前一块的间隔、转角、有没有长按。节奏对齐（<see cref="FingeringMatcher"/>）里反复比较，
/// 先算好放进数组，不在几百万次比较里一次次取 TileHit。
/// </summary>
internal sealed class Rhythm
{
    /// <summary>两块砖算「同一块」：间隔相差不超过 4 毫秒或 3%，转角相差不超过 1.5°。</summary>
    private const double GapTolerance = 0.004;

    private const double GapRatio = 0.03;

    private const double AngleTolerance = 1.5;

    /// <summary>
    /// 间隔分桶（只给 <see cref="Bound"/> 预筛用）：桶号 = ⌊ln(1 + 间隔 / C) / W⌋。算「同一块」的两个间隔，ln(1 + 间隔 / C) 相差不超过
    /// max(ln(1 + 0.004 / C), ln(1 / 0.97)) ≈ 0.03046（C 取 0.004 / ln(1 / 0.97)，让 4 毫秒和 3% 两种容差的上界一样），
    /// 比 W 小，所以一定落在同一个桶或相邻的桶。负的间隔（没排好的砖）按 0 算，同样成立。
    /// </summary>
    private const double BucketC = 0.1313;

    private const double BucketW = 0.031;

    /// <summary>预筛用的片段长度：连续几块砖。</summary>
    private const int Window = 4;

    public Rhythm(IReadOnlyList<TileHit> hits)
    {
        Count = hits.Count;
        Gap = new double[Count];
        Angle = new double[Count];
        Hold = new bool[Count];
        Bucket = new int[Count];
        var previous = 0.0;
        for (var i = 0; i < Count; i++)
        {
            var h = hits[i];
            Gap[i] = i == 0 ? 0 : h.Time - previous;
            Angle[i] = h.Angle;
            Hold[i] = h.HoldSeconds > 0;
            Bucket[i] = (int)Math.Min(0xFFF0, Math.Floor(Math.Log(1 + (Math.Max(0, Gap[i]) / BucketC)) / BucketW));
            previous = h.Time;
        }
    }

    public int Count { get; }

    /// <summary>和前一块砖的间隔（秒）；第 0 块为 0（不比）。</summary>
    public double[] Gap { get; }

    public double[] Angle { get; }

    public bool[] Hold { get; }

    public int[] Bucket { get; }

    /// <summary>a 的第 i 块和 b 的第 j 块算不算同一块砖：长按一样、转角差不超过 1.5°（有一边读不到就不比）、间隔差不超过 4 毫秒或 3%（有一边是第一块就不比）。</summary>
    public static bool Same(Rhythm a, int i, Rhythm b, int j) => Same(a.Hold[i], a.Angle[i], a.Gap[i], i == 0, b, j);

    /// <summary>同上，a 的那块砖的长按、转角、间隔、是不是第一块先取出来（对齐时一整行都是同一块，每一格调一次，几百万次）。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Same(bool hold, double angle, double gap, bool first, Rhythm b, int j)
    {
        if (hold != b.Hold[j])
        {
            return false;
        }

        var other = b.Angle[j];
        if (!double.IsNaN(angle) && !double.IsNaN(other) && Math.Abs(angle - other) > AngleTolerance)
        {
            return false;
        }

        if (first || j == 0)
        {
            return true;
        }

        var g = b.Gap[j];
        return Math.Abs(gap - g) <= Math.Max(GapTolerance, GapRatio * Math.Max(gap, g));
    }

    /// <summary>两串砖一样多，并且第 i 块和第 i 块都算同一块（谱面的节奏没改过）。遇到第一块不一样的就停。</summary>
    public static bool SameOneByOne(Rhythm a, Rhythm b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!Same(a, i, b, i))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 本关最多能有多少块砖对上 <paramref name="file"/>（上限，只用来跳过不可能对上的文件，不改结果）。
    /// 对上的砖都在两边连续至少 <paramref name="minRun"/> 块的整段里；这样一段里的每块砖都在段内某个连续 4 块的片段里，
    /// 两边这 4 块的长按一样、后 3 个间隔的桶号相差不超过 1。所以数一数本关有多少块砖落在「文件里有对得上的片段」的片段里：
    /// 真正对上的砖一块不漏，BPM、节奏不一样的歌大多数片段都对不上。
    /// </summary>
    public static int Bound(Rhythm current, Rhythm file, int minRun)
    {
        if (current.Count < Math.Max(minRun, Window) || file.Count < Math.Max(minRun, Window))
        {
            return 0;
        }

        var pieces = new HashSet<long>();
        for (var s = 0; s + Window <= file.Count; s++)
        {
            pieces.Add(file.Piece(s, 0, 0, 0));
        }

        int covered = 0, coveredTo = 0;
        for (var s = 0; s + Window <= current.Count; s++)
        {
            if (current.Near(s, pieces))
            {
                covered += s + Window - Math.Max(s, coveredTo);
                coveredTo = s + Window;
            }
        }

        return covered;
    }

    /// <summary>从第 s 块起的 4 块砖里，有没有一个（后 3 个间隔各差一个桶以内的）片段在 pieces 里。</summary>
    private bool Near(int s, HashSet<long> pieces)
    {
        for (var d1 = -1; d1 <= 1; d1++)
        {
            for (var d2 = -1; d2 <= 1; d2++)
            {
                for (var d3 = -1; d3 <= 1; d3++)
                {
                    if (pieces.Contains(Piece(s, d1, d2, d3)))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>从第 s 块起 4 块砖的片段：4 个长按标记 + 后 3 块的间隔桶号（各加 d，再加 1 让 -1 也是非负数）。</summary>
    private long Piece(int s, int d1, int d2, int d3)
    {
        long holds = (Hold[s] ? 1 : 0) | (Hold[s + 1] ? 2 : 0) | (Hold[s + 2] ? 4 : 0) | (Hold[s + 3] ? 8 : 0);
        return holds | ((long)(Bucket[s + 1] + d1 + 1) << 4) | ((long)(Bucket[s + 2] + d2 + 1) << 20) | ((long)(Bucket[s + 3] + d3 + 1) << 36);
    }
}
