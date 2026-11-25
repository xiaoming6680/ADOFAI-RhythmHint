using System.Collections.Generic;

namespace RhythmHint.Core;

/// <summary>四键模式下的轨道数。</summary>
public static class Lanes
{
    public const int Count = 4;

    /// <summary>轨道属于哪只手：0、1 左手，2、3 右手。</summary>
    public static int HandOf(int lane) => lane < 2 ? 0 : 1;

    /// <summary>食指所在的轨道：左手 1，右手 2。</summary>
    public static int IndexLane(int hand) => hand == 0 ? 1 : 2;

    /// <summary>同一只手的另一根手指：0↔1，2↔3。</summary>
    public static int Partner(int lane) => lane switch
    {
        0 => 1,
        1 => 0,
        2 => 3,
        _ => 2,
    };
}

/// <summary>关卡里一次需要按键的砖块。</summary>
/// <param name="TileIndex">砖块序号（scrFloor.seqID）。</param>
/// <param name="Time">到达时间，秒，和游戏的歌曲进度同一时间轴。</param>
/// <param name="HoldSeconds">长按砖的持续时间，普通砖为 0。</param>
/// <param name="Angle">
/// 从上一块要按的砖到这块，星球转过的角度（度，180° 是一拍）。谱师把要同时按的砖画成很小的转角，
/// 用它分辨多押和极快的连打。NaN 表示不知道（这时只看时间）。
/// </param>
/// <param name="Taps">
/// 这块砖要按几下（按游戏现在的设置算，至少 1）。双押砖（关卡事件 Multitap）要同时按好几下，游戏设置成只按一下时是 1。
/// 不写进手序文件、不算进谱面指纹。
/// </param>
/// <param name="Multitap">是双押砖（关卡里要按两下及以上，不管游戏设置）：套用专属手序时按 Taps 补键、减键只动这种砖。</param>
public readonly record struct TileHit(int TileIndex, double Time, double HoldSeconds = 0, double Angle = double.NaN, int Taps = 1, bool Multitap = false);

/// <summary>
/// 提示画在什么时候到判定线。伪多押由几块相隔二三十毫秒的砖组成，玩家照着提示一次按下几个键：
/// 提示放在第一块砖时，后面几块总是按早（2026-11-25 用户：多押每次跟着提示按，总是稍早或稍晚）。
/// 所以放在第一块和最后一块砖的正中间，最早那块稍慢、最晚那块稍快，偏差各一半。只管画，排键照旧用 <see cref="PlannedNote.Time"/>。
/// </summary>
public static class HintTiming
{
    /// <summary>每个音符的提示时间，和 notes 一一对应。hits 里找不到最后一块砖时用 <see cref="PlannedNote.Time"/>。</summary>
    public static double[] Times(IReadOnlyList<PlannedNote> notes, IReadOnlyList<TileHit> hits)
    {
        var timeOf = new Dictionary<int, double>(hits.Count);
        foreach (var hit in hits)
        {
            timeOf[hit.TileIndex] = hit.Time;
        }

        var times = new double[notes.Count];
        for (var i = 0; i < notes.Count; i++)
        {
            var note = notes[i];
            times[i] = note.LastTileIndex != note.TileIndex && timeOf.TryGetValue(note.LastTileIndex, out var last)
                ? (note.Time + last) / 2
                : note.Time;
        }

        return times;
    }
}

/// <summary>最终画到屏幕上的一个音符。</summary>
public sealed class PlannedNote
{
    public PlannedNote(int tileIndex, double time)
    {
        TileIndex = tileIndex;
        LastTileIndex = tileIndex;
        Time = time;
    }

    /// <summary>这个音符的第一块砖。</summary>
    public int TileIndex { get; }

    /// <summary>这个音符的最后一块砖（伪双押由两块挨着的砖组成）。这块砖被按过后音符消失。</summary>
    public int LastTileIndex { get; set; }

    public double Time { get; }

    /// <summary>这一下要按的轨道及各自的长按时长（0 为普通音符）。</summary>
    public SortedDictionary<int, double> Lanes { get; } = new();

    /// <summary>复制一份（轨道和长按一起复制）。</summary>
    public PlannedNote Clone()
    {
        var copy = new PlannedNote(TileIndex, Time) { LastTileIndex = LastTileIndex };
        foreach (var kv in Lanes)
        {
            copy.Lanes[kv.Key] = kv.Value;
        }

        return copy;
    }

    /// <summary>同时按两个及以上轨道。</summary>
    public bool IsChord => Lanes.Count > 1;

    /// <summary>三押、四押（同时按三个及以上轨道），画成紫色；双押是蓝色。</summary>
    public bool IsMultiChord => Lanes.Count > 2;

    /// <summary>
    /// 按两个键的那只手（0 左、1 右）：双押就是它那只手；三押是按两个键的那只手（另一只手只补一个键）；
    /// 四押两只手都按两个，算右手。单押是它那只手。
    /// </summary>
    public int PairHand
    {
        get
        {
            var right = 0;
            foreach (var lane in Lanes.Keys)
            {
                right += Core.Lanes.HandOf(lane);
            }

            return right * 2 >= Lanes.Count ? 1 : 0;
        }
    }
}
