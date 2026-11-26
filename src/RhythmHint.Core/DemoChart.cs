using System;
using System.Collections.Generic;

namespace RhythmHint.Core;

/// <summary>
/// 设置页演示用的一小段节奏：16 拍一循环。开头 4 拍是每拍一下（第一拍双押），像节拍器一样方便对拍子校准；
/// 后面慢的、中速的、很快的、双押、三押、长按都有一点，改参数时能马上看出效果。
/// </summary>
public sealed class DemoChart
{
    private DemoChart(double secondsPerBeat, IReadOnlyList<TileHit> tiles)
    {
        SecondsPerBeat = secondsPerBeat;
        Tiles = tiles;
    }

    public double SecondsPerBeat { get; }

    public double LoopSeconds => SecondsPerBeat * 16;

    public IReadOnlyList<TileHit> Tiles { get; }

    public static DemoChart Build(double bpm) => Build(bpm, forSettings: false, holdLastNote: true, veryFastRun: false);

    /// <summary>
    /// 同一段短串用来对比两种排法。审核偏好演示去掉长按，以免整段回退到时间重置法。
    /// 6K、8K 的第 13 拍起换成超快连打，4K 保持 1/8 拍连打（2026-11-26 用户：4K 恢复原样）。
    /// </summary>
    public static DemoChart BuildForSettings(double bpm, FingeringStyle style, int keys) =>
        Build(bpm, forSettings: true, holdLastNote: style != FingeringStyle.ReviewPreferenceV1, veryFastRun: keys > Lanes.Count);

    private static DemoChart Build(double bpm, bool forSettings, bool holdLastNote, bool veryFastRun)
    {
        var spb = 60.0 / bpm;
        var tiles = new List<TileHit>();

        // 转角和游戏里一样从间隔换算（180° 一拍）；每轮第一块接在上一轮第 15 拍的长按后面，隔一拍
        void At(double time, double holdBeats = 0) =>
            tiles.Add(new TileHit(tiles.Count + 1, time, holdBeats * spb,
                tiles.Count == 0 ? 180 : (time - tiles[tiles.Count - 1].Time) / spb * 180));

        void Add(double beat, double holdBeats = 0) => At(beat * spb, holdBeats);

        // 伪多押：几块砖各隔 25 ms（160 BPM 下转角 12°）
        void Chord(double beat, int keys = 2)
        {
            for (var k = 0; k < keys; k++)
            {
                At((beat * spb) + (k * 0.025));
            }
        }

        // 第 0–3 拍：每拍一下，第一拍双押（校准用）
        Chord(0);
        for (var b = 1; b < 4; b++)
        {
            Add(b);
        }

        // 第 4–5 拍：1/4 拍连打（中速，两只手食指轮流）
        for (var i = 0; i < 8; i++)
        {
            Add(4 + (i * 0.25));
        }

        // 第 6–7 拍：半拍
        for (var i = 0; i < 4; i++)
        {
            Add(6 + (i * 0.5));
        }

        if (forSettings)
        {
            // 预留第 8–9 拍给短串：每步 62.5 ms，在标准排法的轮指线（重置间隔一半，默认 65 ms）以内、交替优先的短串范围里，两种排法看得出区别。
            // 压缩快歌里的间隔，避免碰上下一个双押。
            var gap = Math.Min(1.0 / 16, spb / 3);
            for (var i = 0; i < 4; i++)
            {
                At((8.5 * spb) + (i * gap));
            }
        }
        else
        {
            // 第 8–9 拍：附点节奏
            foreach (var b in new[] { 8, 8.75, 9.5 })
            {
                Add(b);
            }
        }

        // 第 10–11 拍：每半拍一个双押（右手）
        for (var i = 0; i < 4; i++)
        {
            Chord(10 + (i * 0.5));
        }

        // 第 12–13 拍：1/8 拍连打（够快，同一只手两根手指轮指 K J D F，四条轨道都用到）
        // 6K、8K 的设置页演示后半拍换成每 20 ms 一下的超快连打（转角正常，像谱师用高 BPM 画的那种）：在这里用上外侧的手指（越快用的手指越多）。
        // 4K 只有四个键，换了只是挤成一段密楼梯，所以不换
        for (var i = 0; i < (veryFastRun ? 8 : 16); i++)
        {
            Add(12 + (i * 0.125));
        }

        if (veryFastRun)
        {
            for (var i = 0; i < 10; i++)
            {
                tiles.Add(new TileHit(tiles.Count + 1, (13 * spb) + (i * 0.02), 0, 180));
            }
        }

        // 第 14.5 拍：三押（紫色）。和前面的 1/8 拍连打隔开大半拍，免得小转角的连打和它串成一长串
        Chord(14.5, keys: 3);

        // 第 15 拍起长按大半拍（审核偏好演示用同一时刻的单押）。
        Add(15, holdBeats: holdLastNote ? 0.75 : 0);
        return new DemoChart(spb, tiles);
    }

    /// <summary>把演示节奏首尾相接重复几遍，序号连续，用来无缝循环播放。</summary>
    public static List<TileHit> Repeat(DemoChart demo, int times)
    {
        var all = new List<TileHit>(demo.Tiles.Count * times);
        for (var k = 0; k < times; k++)
        {
            foreach (var t in demo.Tiles)
            {
                all.Add(t with { TileIndex = all.Count + 1, Time = t.Time + (k * demo.LoopSeconds) });
            }
        }

        return all;
    }
}
