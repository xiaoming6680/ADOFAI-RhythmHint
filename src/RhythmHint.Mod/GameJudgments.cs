using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RhythmHint.Core;
using UnityEngine;

namespace RhythmHint;

/// <summary>
/// 判定提示的文字、颜色和游戏判定（HitMargin）的换算。文字和颜色仿照游戏默认的、由本 Mod 自带
/// （2026-11-28 用户：不用游戏自己的判定文字和颜色，仿照原版做，少受游戏版本影响）：1.5.0 读游戏的「完美文字」设置和配色，
/// 3.4.0 之前的游戏没有这些接口，整个叠加层停用（2026-11-27 玩家截图）。
/// 判定按名字认（<see cref="JudgmentRules.FromGameName"/>），本 Mod 的代码里不写 HitMargin 的常量：枚举编号编译时就定死，新旧游戏不一样。
/// 还要读游戏的只剩「玩家按出了什么判定」，在 <see cref="JudgmentApi"/>；缺接口时只关掉判定提示（用户：可以按需关闭部分功能）。
/// </summary>
internal static class GameJudgments
{
    /// <summary>一行判定文字大约多宽（1080p 像素，「Overload」「EPerfect」这样的长度），用来定窄轨道上相邻的字要不要让位。</summary>
    private const double LabelWidth = 64;

    private static readonly Judgment[] All = (Judgment[])Enum.GetValues(typeof(Judgment));
    private static Dictionary<int, Judgment>? _byMargin;
    private static bool _disabled;

    /// <summary>
    /// 这个游戏版本能不能用判定提示：读判定用到的接口缺了时由 <see cref="Disable"/> 改成不能用。
    /// 不能用时不读、不画判定，设置页说明原因，玩家的开关设置不改（更新游戏后照常）。
    /// </summary>
    public static bool Available => !_disabled;

    /// <summary>
    /// 面板显示的判定种类；自动砖、中旋、死后补记的砖、多按这些不是玩家按出来的判定，为 null。
    /// 按玩家游戏里的名字换（第一次用时把游戏这个枚举的每个值都换一遍）。
    /// </summary>
    public static Judgment? Of(HitMargin margin)
    {
        _byMargin ??= MapByName();
        return _byMargin.TryGetValue((int)margin, out var judgment) ? judgment : null;
    }

    /// <summary>按设置要不要显示这个判定（判定提示开着、游戏支持；完美的只在「显示完美判定」时显示）。</summary>
    public static bool Shows(Judgment judgment, Settings s) =>
        s.ShowJudgments && Available && JudgmentRules.Shows(judgment, s.ShowPerfectJudgments);

    /// <summary>这个键数下，新出的字离旧字不到几条轨道时把旧字换掉（见 <see cref="JudgmentRules.MinGap"/>）。</summary>
    public static double MinGap(int keys) => JudgmentRules.MinGap(LabelWidth, OverlayLayout.LaneBase(keys));

    /// <summary>
    /// 判定文字，跟界面语言走。仿照游戏默认显示在星球旁边的那句（游戏的本地化表，五种语言），去掉标点（2026-11-23 用户）：
    /// 游戏里快和太快都是「太快」、只差感叹号个数，这里一样，靠颜色分；X 完美、正负完美按游戏默认都写成完美。
    /// </summary>
    public static string Text(Judgment judgment) => judgment switch
    {
        Judgment.TooEarly or Judgment.VeryEarly => Lang.T("太快"),
        Judgment.EarlyPerfect => Lang.T("稍快"),
        Judgment.Perfect => Lang.T("完美"),
        Judgment.LatePerfect => Lang.T("稍慢"),
        Judgment.VeryLate or Judgment.TooLate => Lang.T("太慢"),
        Judgment.Miss => Lang.T("失误"),
        _ => Lang.T("按太快"),
    };

    /// <summary>判定文字的颜色（游戏默认配色，见 <see cref="JudgmentRules.Rgb"/>）。</summary>
    public static Color Color(Judgment judgment)
    {
        var rgb = JudgmentRules.Rgb(judgment);
        return new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
    }

    /// <summary>所有判定文字（先排进字体贴图，第一次出字时不卡）。</summary>
    public static string AllTexts()
    {
        var text = string.Empty;
        foreach (var judgment in All)
        {
            text += Text(judgment);
        }

        return text;
    }

    /// <summary>游戏里缺本 Mod 编译时引用的类型或成员（玩家的游戏比开发时旧）。</summary>
    public static bool IsMissingApi(Exception ex) => ex is TypeLoadException or MissingMemberException;

    /// <summary>读判定用到的游戏接口缺了：这次运行里关掉判定提示，记一次日志，其他功能照常。</summary>
    public static void Disable(Exception ex)
    {
        if (_disabled)
        {
            return;
        }

        _disabled = true;
        Main.Mod.Logger.Warning($"判定提示：当前游戏版本缺少所需接口（{ex.Message}），已关闭判定提示，其他功能照常");
    }

    /// <summary>
    /// 玩家游戏里 HitMargin 的每个值按名字换成面板的判定种类，换不了的（自动、中旋等）不放进来。
    /// 认到的名字记一次日志：玩家报旧版游戏的问题时看得出游戏有哪几种判定。
    /// </summary>
    private static Dictionary<int, Judgment> MapByName()
    {
        var map = new Dictionary<int, Judgment>();
        var names = new List<string>();
        foreach (HitMargin margin in Enum.GetValues(typeof(HitMargin)))
        {
            if (JudgmentRules.FromGameName(margin.ToString()) is { } judgment)
            {
                map[(int)margin] = judgment;
                names.Add($"{margin}={(int)margin}");
            }
        }

        Main.Mod.Logger.Log($"判定提示：按名字认得游戏的 {map.Count} 种判定（{string.Join(", ", names)}）");
        return map;
    }
}

/// <summary>
/// 读判定用到的、1.3.5 没用过的游戏接口（scrFloor.grade / hideJudgment、scrMistakesManager.marginTrackers、scrController.coopMode、
/// RDC.auto / useOldAuto），旧版游戏有没有没法核对，所以隔离在这里（2026-11-27：旧版游戏缺一个类型就整个叠加层停用）。
/// 规矩：游戏里较新的类型只许写在本类的方法体里，字段和方法签名只用旧版游戏也有的类型（HitMargin、scrFloor、Core 和 Unity 的类型）；
/// 每个方法都不许内联。Mono 第一次用一个类时要排好它的字段布局，字段是缺的类型就整个类不能用；方法体里缺的类型、成员
/// 在第一次调用这个方法时才报错，异常抛在调用处，由 <see cref="JudgmentReader"/> 接住。
/// 往这里加游戏接口前先查 1.3.5 用没用过（docs/开发说明.md「判定提示」）。
/// </summary>
internal static class JudgmentApi
{
    /// <summary>单人模式下玩家的判定记录（scrMistakesManager.marginTrackers[0]）；多人模式不显示。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static List<HitMargin>? Margins()
    {
        if (scrController.coopMode)
        {
            return null;
        }

        var trackers = scrMistakesManager.marginTrackers;
        return trackers is { Length: > 0 } && trackers[0] != null ? trackers[0].hitMargins : null;
    }

    /// <summary>
    /// 一个音符的判定：它那几块砖里最差的一个。游戏自动播放时按完美（游戏这时记 X 完美）；
    /// 自动砖、中旋、假砖和设成不显示判定的砖（scrFloor.hideJudgment，游戏星球旁也不显示）不算。
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Judgment? NoteGrade(List<scrFloor> floors, PlannedNote note)
    {
        var auto = RDC.auto && !RDC.useOldAuto;
        Judgment? worst = null;
        for (var t = note.TileIndex; t <= note.LastTileIndex && t < floors.Count; t++)
        {
            var floor = floors[t];
            if (floor == null || floor.auto || floor.midSpin || floor.isFake || floor.hideJudgment)
            {
                continue;
            }

            var judgment = auto ? Judgment.Perfect : GameJudgments.Of(floor.grade);
            if (judgment is { } j && (worst is not { } w || JudgmentRules.Severity(j) > JudgmentRules.Severity(w)))
            {
                worst = j;
            }
        }

        return worst;
    }

    /// <summary>这块砖设成不显示判定（scrFloor.hideJudgment，游戏星球旁也不显示）。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool HidesJudgment(scrFloor floor) => floor.hideJudgment;
}

/// <summary>
/// 读游戏记下的判定，记到下落面板上对应的轨道（2026-11-23 用户：多 K 面板上加判定提示，但不能喧宾夺主）。不打补丁，每帧看两样：
/// <list type="number">
/// <item>这一帧刚按过去的音符：读它那几块砖的 scrFloor.grade（游戏命中时写进被按到的那块砖），伪多押取最差的</item>
/// <item>没让星球前进的按键（太快、太慢、失败条满）：游戏把判定追加到 marginTracker.hitMargins，砖块不动，记到下一个要按的音符的轨道</item>
/// </list>
/// grade 的默认值是「太快」（枚举第一项），没按过的砖也读得出来：只在这一帧 hitMargins 变长了（游戏这一帧确实记了判定）、
/// 音符时间离现在不远（<see cref="JudgmentRules.IsFresh"/>）时才信它。读游戏的部分在 <see cref="JudgmentApi"/>，
/// 旧版游戏缺接口时只关掉判定提示（见 <see cref="GameJudgments.Available"/>）。
/// </summary>
internal sealed class JudgmentReader
{
    private readonly List<Judgment> _fromTiles = new();
    private List<HitMargin>? _list;
    private int _seen;
    private int _seq = -1;

    /// <summary>换关、重开后重新对齐（下一帧只记下游戏现在记了多少，不出字）。</summary>
    public void Reset() => _list = null;

    /// <summary>每帧在 <see cref="OverlayFrame.TrackHits"/> 之后调用。游戏不支持判定提示时什么也不做。</summary>
    public void Update(LevelState level, OverlayFrame frame, Settings s)
    {
        if (!GameJudgments.Available)
        {
            return;
        }

        try
        {
            Read(level, frame, s);
        }
        catch (Exception ex) when (GameJudgments.IsMissingApi(ex))
        {
            GameJudgments.Disable(ex);
        }
    }

    private void Read(LevelState level, OverlayFrame frame, Settings s)
    {
        var list = JudgmentApi.Margins();
        var seq = level.Controller.currentSeqID;
        var moved = seq != _seq;
        _seq = seq;
        if (list == null || !ReferenceEquals(list, _list) || list.Count < _seen)
        {
            // 第一次、重开（游戏换了一个新列表）、回到检查点（从尾部删掉）：只对齐，不出字
            _list = list;
            _seen = list?.Count ?? 0;
            return;
        }

        var from = _seen;
        _seen = list.Count;
        if (from == list.Count || !s.ShowJudgments)
        {
            return;
        }

        var now = Time.unscaledTime;
        var gap = GameJudgments.MinGap(frame.Keys);
        _fromTiles.Clear();
        foreach (var note in frame.Passed)
        {
            if (!JudgmentRules.IsFresh(note.Time, frame.Now) || JudgmentApi.NoteGrade(level.Floors, note) is not { } judgment)
            {
                continue;
            }

            _fromTiles.Add(judgment);
            if (GameJudgments.Shows(judgment, s))
            {
                frame.Judgments.Add(note.Lanes.Keys, judgment, now, gap);
            }
        }

        for (var i = from; i < list.Count; i++)
        {
            // 星球这一帧没动：新记的都是没按上的（练习模式只算完美时，快、稍快也会没按上）。动了：只有太快、太慢、过载
            // 不会是让星球前进的那一下（伪多押的第一块砖先按到、音符还没过去时也不会误记）；砖块上读到过的不再记一遍（调试模式下太快也能前进）
            if (GameJudgments.Of(list[i]) is not { } judgment)
            {
                continue;
            }

            var missed = !moved || judgment is Judgment.TooEarly or Judgment.TooLate or Judgment.Overload;
            if (!missed || _fromTiles.Remove(judgment))
            {
                continue;
            }

            if (frame.NextNote < frame.Notes.Count && !CurrentFloorHidesJudgment(level) && GameJudgments.Shows(judgment, s))
            {
                frame.Judgments.Add(frame.Notes[frame.NextNote].Lanes.Keys, judgment, now, gap);
            }
        }
    }

    /// <summary>星球所在的砖设成不显示判定时，没前进的按键游戏星球旁也不显示判定。</summary>
    private static bool CurrentFloorHidesJudgment(LevelState level)
    {
        var seq = level.Controller.currentSeqID;
        var floors = level.Floors;
        return seq >= 0 && seq < floors.Count && floors[seq] != null && JudgmentApi.HidesJudgment(floors[seq]);
    }
}
