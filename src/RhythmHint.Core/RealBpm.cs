using System.Collections.Generic;

namespace RhythmHint.Core;

/// <summary>
/// 实际 BPM：60 ÷ 相邻两下按键的间隔（真实秒数），即按键速度（2026-11-26 玩家：BPM 框显示的是砖块 BPM，
/// 打雪花、直线这类匀速段时想看实际按多快）。砖块 BPM 是转半圈的速度，转角小的砖一下不到一拍，所以两者不同。
/// 多押、伪多押、双押砖按一下算（以排好的音符为单位，不以砖块为单位）；停顿超过 <see cref="RestSeconds"/> 秒不算。
/// </summary>
public static class RealBpm
{
    /// <summary>相邻两下隔得比这更久（秒）就当作停顿，不显示（否则停顿后那一下会显示成二三十）。</summary>
    public const double RestSeconds = 2.0;

    /// <summary>
    /// 将要按的第 <paramref name="next"/> 个音符和上一个音符之间的实际 BPM；没有上一个、已经按完、停顿或数据不对时返回 0（不显示）。
    /// <paramref name="rate"/> 是音乐的播放速度：音符时间是歌曲进度，变速后真实间隔是它除以播放速度。
    /// </summary>
    public static double At(IReadOnlyList<PlannedNote> notes, int next, double rate)
    {
        if (next <= 0 || next >= notes.Count || !(rate > 0))
        {
            return 0;
        }

        var seconds = (notes[next].Time - notes[next - 1].Time) / rate;
        return seconds > 0 && seconds <= RestSeconds ? 60 / seconds : 0;
    }
}
