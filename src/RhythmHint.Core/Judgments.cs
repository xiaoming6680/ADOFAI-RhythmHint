using System;
using System.Collections.Generic;

namespace RhythmHint.Core;

/// <summary>
/// 下落面板上显示的判定种类：游戏的判定（HitMargin）里玩家按出来的那几种，按名字换过来（<see cref="JudgmentRules.FromGameName"/>）。
/// 自动砖、中旋、死后补记的砖游戏也记判定，但不是玩家按的，不显示。
/// </summary>
public enum Judgment
{
    /// <summary>太快，没算按上（星球不前进）。</summary>
    TooEarly,

    /// <summary>快，算按上。</summary>
    VeryEarly,

    /// <summary>稍快（偏快的完美）。</summary>
    EarlyPerfect,

    /// <summary>完美（含 X 完美、正负完美）。</summary>
    Perfect,

    LatePerfect,

    VeryLate,

    TooLate,

    /// <summary>漏按：免死模式下差得太远照样前进时游戏记的。</summary>
    Miss,

    /// <summary>失败条满了（过载）。</summary>
    Overload,
}

/// <summary>判定提示的规则（2026-11-23 用户：多 K 面板上加判定提示，但不能喧宾夺主）。</summary>
public static class JudgmentRules
{
    /// <summary>
    /// 刚按过去的音符离现在不超过这么久（秒）才读它的判定。游戏的砖块判定默认是「太快」（枚举第一项），没按过的砖也读得出来：
    /// 编辑器从中间某块砖开始播放、进度往前跳时，跳过的那些音符离现在很远，靠它排除。
    /// </summary>
    public const double FreshWindow = 0.5;

    /// <summary>离准点多远：完美 0，稍快稍慢 1，快、慢 2，太快、太慢 3，漏按、过载 4。伪多押几块砖取最大的。</summary>
    public static int Severity(Judgment judgment) => judgment switch
    {
        Judgment.Perfect => 0,
        Judgment.EarlyPerfect or Judgment.LatePerfect => 1,
        Judgment.VeryEarly or Judgment.VeryLate => 2,
        Judgment.TooEarly or Judgment.TooLate => 3,
        _ => 4,
    };

    /// <summary>要不要显示：不完美的都显示，完美的只在玩家选了「完美也显示」时显示（默认不显示，大部分按键都是完美，全写出来太密）。</summary>
    public static bool Shows(Judgment judgment, bool showPerfect) => showPerfect || judgment != Judgment.Perfect;

    /// <summary>
    /// 游戏判定（HitMargin）的名字换成面板的判定种类；自动砖、中旋、死后补记的砖、多按这些不是玩家按出来的判定，为 null。
    /// 按名字认、不按编号（2026-11-28）：HitMargin 是游戏的枚举，编号编译时就定死，游戏 3.4.0 加了 X 完美那三项（PerfectMinus、XPerfect、PerfectPlus），
    /// 之前的游戏只有 Perfect，后面的编号整体错位。名字在运行时从玩家的游戏里取，新旧游戏都认得对。
    /// </summary>
    public static Judgment? FromGameName(string name) => name switch
    {
        "TooEarly" => Judgment.TooEarly,
        "VeryEarly" => Judgment.VeryEarly,
        "EarlyPerfect" => Judgment.EarlyPerfect,
        "Perfect" or "PerfectMinus" or "XPerfect" or "PerfectPlus" => Judgment.Perfect,
        "LatePerfect" => Judgment.LatePerfect,
        "VeryLate" => Judgment.VeryLate,
        "TooLate" => Judgment.TooLate,
        "FailMiss" => Judgment.Miss,
        "FailOverload" => Judgment.Overload,
        _ => null,
    };

    /// <summary>
    /// 判定文字的颜色（0xRRGGBB），仿照游戏默认配色（2026-11-28 用户：不用游戏自己的颜色，仿照原版做）。
    /// 取自开发时游戏（3.4.0）RDConstants.hitMarginColours 的值：太快太慢红、快慢橙、稍快稍慢黄绿、完美绿、漏按和过载紫。
    /// 玩家在游戏里改了判定配色时这里不跟。
    /// </summary>
    public static uint Rgb(Judgment judgment) => judgment switch
    {
        Judgment.TooEarly or Judgment.TooLate => 0xFF0000,
        Judgment.VeryEarly or Judgment.VeryLate => 0xFF6F4E,
        Judgment.EarlyPerfect or Judgment.LatePerfect => 0xA0FF4E,
        Judgment.Perfect => 0x60FF4E,
        _ => 0xD958FF,
    };

    /// <summary>音符时间离现在够近，它那几块砖上的判定是这一次按出来的。</summary>
    public static bool IsFresh(double noteTime, double now) => Math.Abs(noteTime - now) <= FreshWindow;

    /// <summary>
    /// 小字至少隔几条轨道才能并排（再近就换掉旧的）。<paramref name="labelWidth"/> 是一行判定文字大约多宽、<paramref name="laneWidth"/> 是轨道宽（同一单位）：
    /// 4K 轨道宽，相邻轨道的字放得下；6K、8K 轨道窄，相邻两条的字会叠在一起，旧的让位。至少 0.75 条（同一条轨道总是换掉）。
    /// </summary>
    public static double MinGap(double labelWidth, double laneWidth) => laneWidth > 0 ? Math.Max(0.75, labelWidth / laneWidth) : 0.75;

    /// <summary>
    /// 设置窗口演示里的示例判定（演示里每块砖都按得准，判定是编出来给玩家看样子的）：大部分完美，偶尔稍快、稍慢、快、慢，快慢对称，
    /// 不让玩家以为画面偏移不对。按音符序号循环，每次看到的一样。
    /// </summary>
    public static Judgment DemoAt(int index) => DemoCycle[((index % DemoCycle.Length) + DemoCycle.Length) % DemoCycle.Length];

    private static readonly Judgment[] DemoCycle =
    {
        Judgment.Perfect, Judgment.Perfect, Judgment.EarlyPerfect, Judgment.Perfect, Judgment.Perfect, Judgment.Perfect,
        Judgment.LatePerfect, Judgment.Perfect, Judgment.Perfect, Judgment.VeryEarly, Judgment.Perfect, Judgment.Perfect,
        Judgment.Perfect, Judgment.LatePerfect, Judgment.Perfect, Judgment.EarlyPerfect, Judgment.Perfect, Judgment.Perfect,
        Judgment.Perfect, Judgment.VeryLate, Judgment.Perfect, Judgment.Perfect,
    };
}

/// <summary>
/// 下落面板上正在显示的判定小字。每个落在一个位置（轨道中心，按轨道算：0.5 是第一条正中，多押取几条轨道的平均），
/// 显示 <see cref="Life"/> 秒后消失。新的一个出来时，离它不到 minGap 条轨道的旧字直接换掉：快速连打同一条轨道时字不叠在一起。
/// </summary>
/// <typeparam name="T">判定本身（Mod 里是 <see cref="Judgment"/>）。</typeparam>
public sealed class JudgmentPops<T>
{
    /// <summary>一个字显示多久（秒）。</summary>
    public const double Life = 0.45;

    private readonly List<Pop> _items = new();

    /// <param name="Center">位置：轨道中心，按轨道算。</param>
    /// <param name="Value">判定。</param>
    /// <param name="At">出现的时刻（秒，和 <see cref="Expire"/> 传的同一个时钟）。</param>
    public readonly record struct Pop(double Center, T Value, double At)
    {
        /// <summary>显示到哪里了：0 刚出现，1 消失。</summary>
        public double Age(double now) => Math.Max(0, Math.Min(1, (now - At) / Life));
    }

    public IReadOnlyList<Pop> Items => _items;

    /// <summary>在这几条轨道上方出一个字；轨道为空时不出。</summary>
    public void Add(IEnumerable<int> lanes, T value, double at, double minGap = 0.75)
    {
        double sum = 0;
        var count = 0;
        foreach (var lane in lanes)
        {
            sum += lane + 0.5;
            count++;
        }

        if (count == 0)
        {
            return;
        }

        var center = sum / count;
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            if (Math.Abs(_items[i].Center - center) < minGap)
            {
                _items.RemoveAt(i);
            }
        }

        _items.Add(new Pop(center, value, at));
    }

    /// <summary>去掉已经显示完的（时钟倒退时全部去掉，例如换了时钟）。</summary>
    public void Expire(double now)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            var age = now - _items[i].At;
            if (age >= Life || age < 0)
            {
                _items.RemoveAt(i);
            }
        }
    }

    public void Clear() => _items.Clear();
}
