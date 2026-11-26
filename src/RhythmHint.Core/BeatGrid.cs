using System;
using System.Collections.Generic;

namespace RhythmHint.Core;

/// <summary>一条节拍线。</summary>
/// <param name="Time">秒。</param>
/// <param name="IsMeasure">小节线（每小节第一拍），画得更亮。</param>
public readonly record struct BeatLine(double Time, bool IsMeasure);

/// <summary>
/// 歌曲的节拍线：画在轨道和太鼓条上、交给手序编辑器做参照。游戏里和编辑器用 Infer（从关卡变速和砖块推拍子和小节）；
/// MusicalTempo + Build 是原来的做法（每次真换 BPM 都重新起小节），留给设置页演示谱、排键试验台的导出（试验台网页有同样的 JS 版）。
/// 排键和难度估算都不看节拍线。
/// </summary>
public static class BeatGrid
{
    /// <summary>节拍线数量上限，防止异常数据（每拍时长接近 0）卡死。</summary>
    public const int MaxLines = 20000;

    /// <summary>
    /// 从砖块速度推出歌曲的拍子。ADOFAI 里 ×2、×0.5、×4 这类 2 的整数次幂变速几乎都是改砖块疏密，
    /// 歌曲的拍子没变；其它比例才当成真的换了 BPM。
    /// </summary>
    /// <param name="speeds">变速点：从 Time 开始砖块速度倍率为 Speed（相对关卡起始 BPM），按时间排好。</param>
    /// <param name="crotchetAtStart">关卡起始 BPM 下一拍的秒数。</param>
    public static List<(double Time, double SecondsPerBeat)> MusicalTempo(
        IReadOnlyList<(double Time, double Speed)> speeds, double crotchetAtStart)
    {
        var tempo = new List<(double, double)>();
        var tempoFactor = 1.0;   // 真实 BPM 相对起始 BPM 的倍率
        var density = 1.0;       // 砖块疏密倍率（2 的整数次幂）
        foreach (var (time, speed) in speeds)
        {
            if (speed <= 0)
            {
                continue;
            }

            var ratio = speed / (tempoFactor * density);
            var log2 = Math.Log(ratio, 2);
            if (Math.Abs(log2 - Math.Round(log2)) < 0.01)
            {
                density *= Math.Pow(2, Math.Round(log2));
                if (tempo.Count > 0)
                {
                    continue; // 只是疏密变了，拍子不变
                }
            }
            else
            {
                tempoFactor = speed / density;
            }

            tempo.Add((time, crotchetAtStart / tempoFactor));
        }

        return tempo;
    }

    /// <param name="tempo">变速点：从 Time 开始每拍 SecondsPerBeat 秒，按时间排好。</param>
    public static List<BeatLine> Build(IReadOnlyList<(double Time, double SecondsPerBeat)> tempo, double start, double end, int beatsPerMeasure = 4)
    {
        var lines = new List<BeatLine>();
        if (tempo.Count == 0 || end <= start)
        {
            return lines;
        }

        var seg = 0;
        var t = start;
        var beat = 0;
        while (t <= end && lines.Count < MaxLines)
        {
            while (seg + 1 < tempo.Count && tempo[seg + 1].Time <= t + 1e-6)
            {
                seg++;
            }

            var spb = tempo[seg].SecondsPerBeat;
            if (spb <= 0.01)
            {
                spb = 0.01;
            }

            lines.Add(new BeatLine(t, beat % Math.Max(1, beatsPerMeasure) == 0));
            var next = t + spb;

            // 下一拍跨过了变速点：从变速点重新起拍，节拍线和新速度对齐
            if (seg + 1 < tempo.Count && next > tempo[seg + 1].Time + 1e-6)
            {
                next = tempo[seg + 1].Time;
                beat = -1;
            }

            t = next;
            beat++;
        }

        return lines;
    }

    /// <summary>
    /// 画节拍线用：从关卡变速和砖块推音乐的拍子和小节（见 BeatInference）。拍子跨变速连续数，变速点不落在拍上才重新起小节；
    /// 关卡 BPM 不合理时从砖块推。只用来画，排键和难度估算不看节拍线。
    /// </summary>
    /// <param name="hits">要按的砖。</param>
    /// <param name="speeds">变速点：从 Time 开始砖块速度倍率为 Speed（相对关卡起始 BPM），按时间排好。</param>
    /// <param name="crotchetAtStart">关卡起始 BPM 下一拍的秒数。</param>
    /// <param name="start">从哪里开始画（第 0 块砖的时间）。</param>
    /// <param name="end">画到哪里。</param>
    public static BeatMap Infer(IReadOnlyList<TileHit> hits, IReadOnlyList<(double Time, double Speed)> speeds, double crotchetAtStart, double start, double end) =>
        BeatInference.Infer(hits, speeds, crotchetAtStart, start, end);

    /// <summary>双手稳拍的拍网保留每块砖的时刻，以便密集拍中的细分参与判断。</summary>
    public static BeatMap InferForSteadyHands(IReadOnlyList<TileHit> hits, IReadOnlyList<(double Time, double Speed)> speeds,
        double crotchetAtStart, double start, double end) =>
        BeatInference.Infer(hits, speeds, crotchetAtStart, start, end, mergeOnsets: false);

    /// <summary>时间 t 是否落在某条节拍线上（误差不超过一拍的 tolerance 倍）。</summary>
    public static bool IsOnBeat(IReadOnlyList<BeatLine> lines, double t, double tolerance = 0.03)
    {
        var i = FirstAtOrAfter(lines, t);
        for (var k = Math.Max(0, i - 1); k <= Math.Min(lines.Count - 1, i); k++)
        {
            var spb = k + 1 < lines.Count ? lines[k + 1].Time - lines[k].Time
                : k > 0 ? lines[k].Time - lines[k - 1].Time : 0;
            if (spb > 0 && Math.Abs(lines[k].Time - t) <= spb * tolerance)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>第一条时间不早于 t 的节拍线的下标。</summary>
    public static int FirstAtOrAfter(IReadOnlyList<BeatLine> lines, double t)
    {
        int lo = 0, hi = lines.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (lines[mid].Time < t)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }
}
