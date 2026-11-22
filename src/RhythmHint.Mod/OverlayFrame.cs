using System;
using System.Collections.Generic;
using RhythmHint.Core;
using UnityEngine;

namespace RhythmHint;

/// <summary>一帧的绘制数据：关卡模式和演示模式都往这里填，视图只读它。</summary>
internal sealed class OverlayFrame
{
    public double Now;
    public float Bpm;
    public double EndTime;
    public int NextNote;

    /// <summary>入场 / 出场动画进度，0..1。</summary>
    public float Presence;

    /// <summary>本关的估算难度档位（1–60，见 DifficultyTiers）；0 表示不画。Load 时清零，换关后重新估。</summary>
    public int DifficultyTier;

    /// <summary>顶部条右侧的小字说明（演示模式用），画的时候按当前界面语言翻译；空表示不显示。</summary>
    public Message Caption = Message.Empty;

    public IReadOnlyList<PlannedNote> Notes = Array.Empty<PlannedNote>();
    public IReadOnlyList<BeatLine> Beats = Array.Empty<BeatLine>();
    public readonly Dictionary<int, (float Time, Color Color)> Flash = new();
    public (float Time, Color Color) TaikoFlash = (-10f, Painter.Orange);
    /// <summary>每个按键框对应的键按着没有（最多 8 个，前 <see cref="Keys"/> 个有用）。</summary>
    public readonly bool[] KeysDown = new bool[8];
    public string[] KeyLabels = Array.Empty<string>();

    /// <summary>按几键显示（4、6、8）：<see cref="Load"/> 时定，换关、换设置后跟着变。</summary>
    public int Keys { get; private set; } = 4;

    private int _lastSeq = -1;

    /// <summary>换了一套音符（换关、重开、进出演示）时调用。<paramref name="keys"/> 是音符按几键排的。</summary>
    public void Load(IReadOnlyList<PlannedNote> notes, IReadOnlyList<BeatLine> beats, double endTime, int keys = 4)
    {
        Notes = notes;
        Keys = keys;
        Beats = beats;
        EndTime = endTime;
        DifficultyTier = 0;
        Flash.Clear();
        _lastSeq = -1;
    }

    /// <summary>
    /// 砖块序号前进时，把刚经过的音符所在轨道点亮；倒退（检查点重来、演示循环）时重新定位。
    /// 伪多押由几块挨着的砖组成，都按过（LastTileIndex）才算这个提示过去。
    /// </summary>
    public void TrackHits(int seq)
    {
        if (seq < _lastSeq || _lastSeq < 0)
        {
            NextNote = FirstPending(seq);
        }
        else
        {
            while (NextNote < Notes.Count && Notes[NextNote].LastTileIndex <= seq)
            {
                var note = Notes[NextNote];
                foreach (var lane in note.Lanes.Keys)
                {
                    Flash[lane] = (Time.unscaledTime, Painter.NoteColor(note));
                }

                TaikoFlash = (Time.unscaledTime, Painter.NoteColor(note));
                NextNote++;
            }
        }

        _lastSeq = seq;
    }

    /// <summary>
    /// 音符离判定还有多少秒，画位置用。伪双押的第一块砖已经按到、还在等约 25 ms 后的第二块时算 0：
    /// 停在判定处，不越过去（越过看起来像漏按了）。
    /// </summary>
    public double TimeTo(PlannedNote note)
    {
        var dt = note.Time - Now;
        return dt < 0 && note.TileIndex <= _lastSeq ? 0 : dt;
    }

    /// <summary>从 NextNote 往后，到达时间在 lookAhead 秒以内的音符到哪里为止（不含）。从远往近画时用。</summary>
    public int VisibleEnd(double lookAhead)
    {
        var end = NextNote;
        while (end < Notes.Count && Notes[end].Time - Now <= lookAhead)
        {
            end++;
        }

        return end;
    }

    private int FirstPending(int seq)
    {
        int lo = 0, hi = Notes.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (Notes[mid].LastTileIndex <= seq)
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

/// <summary>
/// 下落面板的宽度和位置、太鼓条在上还是在下、太鼓条和 BPM 框显示了多少。Right、Center 是面板在右边、中间的份量（0–1，左边 = 1 − Right − Center），
/// Bottom 是太鼓条在下面的份量，Taiko、Bpm 是太鼓条、BPM 框显示的份量；到位时都是 0 或 1，换位置、开关的途中在两者之间（<see cref="PanelMotion"/>）。
/// </summary>
internal readonly record struct PanelPose(float Width, float Right, float Center, float Bottom, float Taiko, float Bpm)
{
    public float Left => 1 - Right - Center;

    /// <summary>设置里选的位置、这个键数的面板宽度、开着哪些部分。</summary>
    public static PanelPose Target(Settings s, int keys) => new(
        Mathf.Round(OverlayLayout.LaneBase(keys) * keys * OverlayLayout.ScaleOf(s)),
        s.LanesInCenter || !s.LanesOnRight ? 0 : 1,
        s.LanesInCenter ? 1 : 0,
        s.TaikoAtBottom ? 1 : 0,
        s.ShowTaiko ? 1 : 0,
        s.ShowBpm ? 1 : 0);
}

/// <summary>
/// 换键数、换位置时面板平滑过去（2026-11-22 用户：切换 K 数时面板宽度一下跳过去很生硬，类似的地方也加过渡）：
/// 宽度、左中右、太鼓条上下都缓动；换键数时轨道里的东西从淡到实；开关下落面板、太鼓条、BPM 框时滑出 / 滑入
/// （太鼓条和 BPM 框同一天用户要求补上，原来只有下落面板有）。面板没显示时直接到位，进关卡不会从上一关的宽度滑过来。
/// </summary>
internal static class PanelMotion
{
    private const float Duration = 0.35f;
    private static int _keys;

    /// <summary>这一帧的位置和宽度（<see cref="Update"/> 算好的）。</summary>
    public static PanelPose Pose { get; private set; }

    /// <summary>轨道里的东西（节拍线、音符、按键框）的透明度：换键数时从淡到实。</summary>
    public static float ContentAlpha { get; private set; } = 1;

    /// <summary>下落面板显示了多少（0–1）：设置里开关下落面板时滑出 / 滑入，游戏画面跟着让位。</summary>
    public static float LanesShown { get; private set; } = 1;

    /// <summary>每帧调用一次，在游戏画面让位和画面板之前。</summary>
    public static void Update(Settings s, int keys, bool visible)
    {
        var target = PanelPose.Target(s, keys);
        if (!visible || Pose.Width <= 0)
        {
            Motion.Set("panel:w", target.Width);
            Motion.Set("panel:r", target.Right);
            Motion.Set("panel:c", target.Center);
            Motion.Set("panel:b", target.Bottom);
            Motion.Set("panel:taiko", target.Taiko);
            Motion.Set("panel:bpm", target.Bpm);
            Motion.Set("panel:fade", 1);
            Motion.Set("panel:lanes", s.ShowLanes ? 1 : 0);
            Pose = target;
            ContentAlpha = 1;
            LanesShown = s.ShowLanes ? 1 : 0;
            _keys = keys;
            return;
        }

        if (keys != _keys)
        {
            _keys = keys;
            Motion.Set("panel:fade", 0.2f);
        }

        Pose = new PanelPose(
            Motion.Ease("panel:w", target.Width, Duration),
            Motion.Ease("panel:r", target.Right, Duration),
            Motion.Ease("panel:c", target.Center, Duration),
            Motion.Ease("panel:b", target.Bottom, Duration),
            Motion.Ease("panel:taiko", target.Taiko, Duration),
            Motion.Ease("panel:bpm", target.Bpm, Duration));
        ContentAlpha = Motion.Ease("panel:fade", 1, 0.3f);
        LanesShown = Motion.Ease("panel:lanes", s.ShowLanes ? 1 : 0, Duration);
    }
}

/// <summary>
/// 按屏幕大小算出各部分的位置，基准是 1080p。下落面板在左边、中间或右边，太鼓条在上面或下面（设置里选，用户要求）；
/// 面板占满太鼓条以外的整个高度。坐标是 IMGUI 的：原点在左上角。
/// </summary>
internal readonly struct OverlayLayout
{
    /// <summary>直接按设置到位（不带过渡）。</summary>
    /// <param name="keys">下落面板几条轨道（这一关按几键显示，<see cref="OverlayFrame.Keys"/>）。</param>
    public OverlayLayout(Settings s, int keys = 4)
        : this(s, keys, PanelPose.Target(s, keys))
    {
    }

    /// <param name="pose">面板的宽度和位置（换位置、换键数的途中是过渡中的值，<see cref="PanelMotion.Pose"/>）。</param>
    public OverlayLayout(Settings s, int keys, PanelPose pose)
    {
        Scale = ScaleOf(s);
        Keys = keys;
        Pose = pose;
        // 面板宽、太鼓条高取整到整数像素：边缘和游戏画面让出来的区域正好对齐，中间不留一列没人画的像素
        // （用户反馈「一直以来都漏画了几个像素」：小数宽度时摄像机视口取整，和面板之间空出一列）。
        // 轨道铺满整个面板，不再两侧各留 12 像素空边（同一次反馈：看着像轨道没画满）
        PanelWidth = Mathf.Round(pose.Width);
        LaneWidth = PanelWidth / keys;
        Unit = LaneWidth / LaneBase(4); // 4K 时等于 Scale；换键数的途中跟着轨道宽变
        Padding = 0;
        RowHeight = Mathf.Round(128 * Scale);
        // 关掉太鼓条时横条整个收起（2026-11-18 用户），BPM 框只占下落面板那一列的顶端（或底端）。
        // 开关的途中（pose.Taiko、pose.Bpm 在 0 和 1 之间）这一行从屏幕边滑进滑出，下落面板、游戏画面让出的高度跟着连续变
        TopHeight = Mathf.Round(RowHeight * Mathf.Max(pose.Taiko, pose.Bpm));
        StripHeight = Mathf.Round(RowHeight * pose.Taiko);
        KeyBoxHeight = 64 * Scale;
        LanesRight = pose.Right > 0.5f;
        TaikoBottom = pose.Bottom > 0.5f;
        PanelX = Mathf.Round((pose.Right + (pose.Center / 2)) * (Screen.width - PanelWidth));
        PanelY = Mathf.Round((1 - pose.Bottom) * TopHeight);
        PanelHeight = Screen.height - TopHeight;
        BarY = EdgeY(StripHeight, RowHeight, pose.Bottom);
        BoxY = EdgeY(TopHeight, RowHeight, pose.Bottom);
        // BPM 框：太鼓条开着时在面板那一侧（面板在中间时放左边），关着时在面板那一列；
        // 太鼓条开着时关掉 BPM 框，框往它那一侧的屏幕边滑出去（太鼓条关着时跟着这一行竖着滑出）
        BoxX = Mathf.Round(Mathf.Lerp(PanelX, pose.Right * (Screen.width - PanelWidth), pose.Taiko)
            + ((1 - pose.Bpm) * pose.Taiko * (PanelWidth + 8) * ((2 * pose.Right) - 1)));
        JudgeY = PanelY + PanelHeight - KeyBoxHeight - (16 * Scale);
    }

    /// <summary>
    /// 高 row 的一行贴着屏幕上沿（或下沿）、露出 shown 那么高时它的上沿：完全露出时贴边，露出得少时往屏幕外退。
    /// bottom 是在下面的份量（上下换边的途中在两者之间）。
    /// </summary>
    private static float EdgeY(float shown, float row, float bottom) =>
        Mathf.Round(((1 - bottom) * (shown - row)) + (bottom * (Screen.height - shown)));

    /// <summary>1080p 时为 1，再乘设置里的缩放。</summary>
    public static float ScaleOf(Settings s) => Screen.height / 1080f * Mathf.Clamp(s.UiScale, 0.5f, 2f);

    /// <summary>面板的宽度和位置（过渡中的值）。</summary>
    public PanelPose Pose { get; }

    /// <summary>
    /// 太鼓条那一行里 BPM 框的左边：太鼓条开着时在面板那一侧（面板在中间时放左边），关着时在面板那一列的顶端（或底端）。
    /// </summary>
    public float BoxX { get; }

    /// <summary>BPM 框的上沿（高 <see cref="RowHeight"/>）：和下落面板让出的那一截对齐，开关时跟着滑。</summary>
    public float BoxY { get; }

    /// <summary>游戏画面左右让出多少（面板在中间时不让，盖在画面上）；lanes 是要让的面板宽。</summary>
    public (float Left, float Right) SideMargins(float lanes) => (Mathf.Round(lanes * Pose.Left), Mathf.Round(lanes * Pose.Right));

    /// <summary>
    /// 1080p、缩放 1 时每条轨道多宽：4K 68、6K 56、8K 48（2026-11-22 用户：每轨稍窄、面板稍宽，6K 面板 336、8K 384）。
    /// </summary>
    public static float LaneBase(int keys) => keys switch
    {
        6 => 56,
        8 => 48,
        _ => 68,
    };

    /// <summary>几条轨道。</summary>
    public int Keys { get; }

    /// <summary>轨道里的东西（球、判定圈、按键框边距、键名字号）的缩放：轨道窄了跟着缩，4K 时等于 <see cref="Scale"/>。</summary>
    public float Unit { get; }

    /// <summary>下落面板在右边（过渡途中过了一半算）。</summary>
    public bool LanesRight { get; }

    /// <summary>太鼓条在下面（过渡途中过了一半算）。</summary>
    public bool TaikoBottom { get; }

    /// <summary>四轨面板左上角和高度。</summary>
    public float PanelX { get; }

    public float PanelY { get; }

    public float PanelHeight { get; }

    /// <summary>太鼓条（横条）的上沿；高度是 <see cref="RowHeight"/>，开关的途中从屏幕边滑进滑出。</summary>
    public float BarY { get; }

    public float Scale { get; }

    public float LaneWidth { get; }

    public float Padding { get; }

    public float PanelWidth { get; }

    /// <summary>太鼓条那一行本身多高（太鼓条和 BPM 框都按这个高度画）。</summary>
    public float RowHeight { get; }

    /// <summary>太鼓条那一行让出的高度（太鼓条或 BPM 框开着时有，开关途中在 0 和 <see cref="RowHeight"/> 之间）；下落面板让出这么高。</summary>
    public float TopHeight { get; }

    /// <summary>横贯整个屏幕的太鼓条露出多高（太鼓条关掉时为 0）；游戏画面只让出这么高。</summary>
    public float StripHeight { get; }

    public float KeyBoxHeight { get; }

    public float JudgeY { get; }

    public float LaneX(int lane) => Padding + (lane * LaneWidth);

    public float LaneCenter(int lane) => LaneX(lane) + (LaneWidth / 2);
}
