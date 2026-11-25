using System.Collections.Generic;
using RhythmHint.Core;
using UnityEngine;

namespace RhythmHint;

/// <summary>
/// 游戏的判定（HitMargin）：换成面板的判定种类，取游戏自己的判定文字和颜色（2026-11-23 用户：判定文字用游戏的）。
/// 接口来自反编译 Assembly-CSharp：文字是 RDUtils.ToLocalized（星球旁边默认显示的那句，跟游戏语言走），
/// 颜色照 scrHitTextMesh.Show 的选法读 RDC.hitMarginColoursBySettings（跟玩家的判定配色设置）。游戏大更新后可能要改。
/// </summary>
internal static class GameJudgments
{
    /// <summary>一行判定文字大约多宽（1080p 像素，「太快！！」「EPerfect」这样的长度），用来定窄轨道上相邻的字要不要让位。</summary>
    private const double LabelWidth = 64;

    private static readonly string[] Texts = new string[32];
    private static SystemLanguage _textLanguage = SystemLanguage.Unknown;
    private static HitMarginPerfectTextPreset _textPreset;
    private static ColourSchemeHitMargin? _colors;
    private static int _colorsFrame = -1;

    /// <summary>面板显示的判定种类；自动砖、中旋、死后补记的砖、多按这些不是玩家按出来的判定，为 null。</summary>
    public static Judgment? Of(HitMargin margin) => margin switch
    {
        HitMargin.TooEarly => Judgment.TooEarly,
        HitMargin.VeryEarly => Judgment.VeryEarly,
        HitMargin.EarlyPerfect => Judgment.EarlyPerfect,
        HitMargin.PerfectMinus or HitMargin.XPerfect or HitMargin.PerfectPlus => Judgment.Perfect,
        HitMargin.LatePerfect => Judgment.LatePerfect,
        HitMargin.VeryLate => Judgment.VeryLate,
        HitMargin.TooLate => Judgment.TooLate,
        HitMargin.FailMiss => Judgment.Miss,
        HitMargin.FailOverload => Judgment.Overload,
        _ => null,
    };

    /// <summary>演示里编出来的判定换成游戏的（取文字和颜色用）。</summary>
    public static HitMargin From(Judgment judgment) => judgment switch
    {
        Judgment.TooEarly => HitMargin.TooEarly,
        Judgment.VeryEarly => HitMargin.VeryEarly,
        Judgment.EarlyPerfect => HitMargin.EarlyPerfect,
        Judgment.LatePerfect => HitMargin.LatePerfect,
        Judgment.VeryLate => HitMargin.VeryLate,
        Judgment.TooLate => HitMargin.TooLate,
        Judgment.Miss => HitMargin.FailMiss,
        Judgment.Overload => HitMargin.FailOverload,
        _ => HitMargin.XPerfect,
    };

    /// <summary>按设置要不要显示这个判定（判定提示开着；完美的只在「显示完美判定」时显示）。</summary>
    public static bool Shows(HitMargin margin, Settings s) =>
        s.ShowJudgments && Of(margin) is { } judgment && JudgmentRules.Shows(judgment, s.ShowPerfectJudgments);

    /// <summary>这个键数下，新出的字离旧字不到几条轨道时把旧字换掉（见 <see cref="JudgmentRules.MinGap"/>）。</summary>
    public static double MinGap(int keys) => JudgmentRules.MinGap(LabelWidth, OverlayLayout.LaneBase(keys));

    /// <summary>
    /// 游戏星球旁边显示的判定文字（完美类按玩家的「完美文字」设置换成完美或 X 完美）。每帧都要画，
    /// 游戏语言和完美文字设置没变时用上次取的，不重复拼本地化键名。
    /// </summary>
    public static string Text(HitMargin margin)
    {
        var language = RDString.language;
        var preset = Persistence.hitMarginPerfectText;
        if (language != _textLanguage || preset != _textPreset)
        {
            System.Array.Clear(Texts, 0, Texts.Length);
            _textLanguage = language;
            _textPreset = preset;
        }

        // 去掉标点（2026-11-23 用户）：「太快！！」画成「太快」
        var i = (int)margin;
        if (i < 0 || i >= Texts.Length)
        {
            return JudgmentRules.StripPunctuation(margin.ToLocalized(preset));
        }

        return Texts[i] ??= JudgmentRules.StripPunctuation(margin.ToLocalized(preset));
    }

    /// <summary>游戏星球旁边这个判定的颜色。X 完美在玩家没开「显示 X 完美」时和完美同色（同 scrHitTextMesh）。</summary>
    public static Color Color(HitMargin margin)
    {
        // RDC.hitMarginColoursBySettings 每次读都可能新建一份配色，一帧只读一次
        if (_colors == null || _colorsFrame != Time.frameCount)
        {
            _colors = RDC.hitMarginColoursBySettings;
            _colorsFrame = Time.frameCount;
        }

        return margin == HitMargin.XPerfect && !Persistence.hitMarginPerfectText.IsShowXPerfect()
            ? _colors.colourPerfect
            : _colors.SelectByHitMargin(margin);
    }

    /// <summary>所有会显示的判定文字（先排进字体贴图，第一次出字时不卡）。</summary>
    public static string AllTexts()
    {
        var text = string.Empty;
        foreach (var margin in Shown)
        {
            text += Text(margin);
        }

        return text;
    }

    private static readonly HitMargin[] Shown =
    {
        HitMargin.TooEarly, HitMargin.VeryEarly, HitMargin.EarlyPerfect, HitMargin.PerfectMinus, HitMargin.XPerfect, HitMargin.PerfectPlus,
        HitMargin.LatePerfect, HitMargin.VeryLate, HitMargin.TooLate, HitMargin.FailMiss, HitMargin.FailOverload,
    };
}

/// <summary>
/// 读游戏记下的判定，记到下落面板上对应的轨道（2026-11-23 用户：多 K 面板上加判定提示，但不能喧宾夺主）。不打补丁，每帧看两样：
/// <list type="number">
/// <item>这一帧刚按过去的音符：读它那几块砖的 scrFloor.grade（游戏命中时写进被按到的那块砖），伪多押取最差的</item>
/// <item>没让星球前进的按键（太快、太慢、失败条满）：游戏把判定追加到 marginTracker.hitMargins，砖块不动，记到下一个要按的音符的轨道</item>
/// </list>
/// grade 的默认值是「太快」（枚举第一项），没按过的砖也读得出来：只在这一帧 hitMargins 变长了（游戏这一帧确实记了判定）、
/// 音符时间离现在不远（<see cref="JudgmentRules.IsFresh"/>）时才信它。
/// </summary>
internal sealed class JudgmentReader
{
    private readonly List<HitMargin> _fromTiles = new();
    private List<HitMargin>? _list;
    private int _seen;
    private int _seq = -1;

    /// <summary>换关、重开后重新对齐（下一帧只记下游戏现在记了多少，不出字）。</summary>
    public void Reset() => _list = null;

    /// <summary>每帧在 <see cref="OverlayFrame.TrackHits"/> 之后调用。</summary>
    public void Update(LevelState level, OverlayFrame frame, Settings s)
    {
        var list = Margins();
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
            if (!JudgmentRules.IsFresh(note.Time, frame.Now) || NoteGrade(level.Floors, note) is not { } grade)
            {
                continue;
            }

            _fromTiles.Add(grade);
            if (GameJudgments.Shows(grade, s))
            {
                frame.Judgments.Add(note.Lanes.Keys, grade, now, gap);
            }
        }

        for (var i = from; i < list.Count; i++)
        {
            // 星球这一帧没动：新记的都是没按上的（练习模式只算完美时，快、稍快也会没按上）。动了：只有太快、太慢、过载
            // 不会是让星球前进的那一下（伪多押的第一块砖先按到、音符还没过去时也不会误记）；砖块上读到过的不再记一遍（调试模式下太快也能前进）
            var margin = list[i];
            var missed = moved ? margin is HitMargin.TooEarly or HitMargin.TooLate or HitMargin.FailOverload : GameJudgments.Of(margin) != null;
            if (!missed || _fromTiles.Remove(margin))
            {
                continue;
            }

            if (frame.NextNote < frame.Notes.Count && !CurrentFloorHidesJudgment(level) && GameJudgments.Shows(margin, s))
            {
                frame.Judgments.Add(frame.Notes[frame.NextNote].Lanes.Keys, margin, now, gap);
            }
        }
    }

    /// <summary>单人模式下玩家的判定记录（scrMistakesManager.marginTrackers[0]）；多人模式不显示。</summary>
    private static List<HitMargin>? Margins()
    {
        if (scrController.coopMode)
        {
            return null;
        }

        var trackers = scrMistakesManager.marginTrackers;
        return trackers is { Length: > 0 } && trackers[0] != null ? trackers[0].hitMargins : null;
    }

    /// <summary>
    /// 一个音符的判定：它那几块砖里最差的一个。游戏自动播放时按 X 完美（游戏这时也这样记）；
    /// 自动砖、中旋、假砖和设成不显示判定的砖（scrFloor.hideJudgment，游戏星球旁也不显示）不算。
    /// </summary>
    private static HitMargin? NoteGrade(List<scrFloor> floors, PlannedNote note)
    {
        var auto = RDC.auto && !RDC.useOldAuto;
        HitMargin? worst = null;
        var worstSeverity = -1;
        for (var t = note.TileIndex; t <= note.LastTileIndex && t < floors.Count; t++)
        {
            var floor = floors[t];
            if (floor == null || floor.auto || floor.midSpin || floor.isFake || floor.hideJudgment)
            {
                continue;
            }

            var grade = auto ? HitMargin.XPerfect : floor.grade;
            if (GameJudgments.Of(grade) is { } judgment && JudgmentRules.Severity(judgment) > worstSeverity)
            {
                worst = grade;
                worstSeverity = JudgmentRules.Severity(judgment);
            }
        }

        return worst;
    }

    /// <summary>星球所在的砖设成不显示判定时，没前进的按键游戏星球旁也不显示判定。</summary>
    private static bool CurrentFloorHidesJudgment(LevelState level)
    {
        var seq = level.Controller.currentSeqID;
        var floors = level.Floors;
        return seq >= 0 && seq < floors.Count && floors[seq] != null && floors[seq].hideJudgment;
    }
}
