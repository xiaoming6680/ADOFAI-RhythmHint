using RhythmHint.Core;
using UnityEngine;

namespace RhythmHint;

/// <summary>
/// 太鼓条（横条，在屏幕上面或下面）：下落面板那一侧是 BPM 和剩余时间（面板在中间时放左边），其余是太鼓式节奏条（圆形音符从右往左滚到判定圈）。
/// </summary>
internal static class TopBarView
{
    private const float BaseSpeed = 650f;
    private const float FlashSeconds = 0.15f;

    // 分成几块画（2026-11-22 用户：开关太鼓条、BPM 框也要有动画）：太鼓条和 BPM 框各自滑进滑出，Overlay 给每块套各自的平移和透明度。
    // 先画横条和节奏条，再画 BPM 框盖在上面，最后画分隔线和说明

    /// <summary>横贯屏幕的太鼓条：底色和节奏条（节奏条让开 BPM 框占的那一截）。</summary>
    public static void DrawStrip(OverlayFrame f, OverlayLayout l, Settings s)
    {
        Painter.Rect(new Rect(0, l.BarY, Screen.width, l.RowHeight), Painter.PanelColor.WithAlpha(Mathf.Clamp01(s.PanelOpacity)));

        // BPM 框在下落面板那一侧（面板在右边时框也在右边，在中间时放左边），节奏条占剩下的宽度。
        // 换边、开关 BPM 框的途中框滑过去，节奏条跟着挪、跟着伸缩
        var occupied = l.PanelWidth * l.Pose.Bpm;
        DrawTrack(f, l, s, new Rect(Mathf.Round(occupied * (1 - l.Pose.Right)), l.BarY, Screen.width - occupied, l.RowHeight));
    }

    /// <summary>太鼓条的分隔线：在横条靠屏幕中间的那一边。</summary>
    public static void DrawLine(OverlayLayout l)
    {
        var lineY = l.TaikoBottom ? l.BarY : l.BarY + l.RowHeight - (2 * l.Scale);
        Painter.Rect(new Rect(0, lineY, Screen.width, 2 * l.Scale), new Color(0.3f, 0.3f, 0.34f));
    }

    /// <summary>BPM 与剩余时间（还有估算难度）：太鼓条开着时在横条里，关着时在下落面板那一列的顶端或底端。</summary>
    public static void DrawBox(OverlayFrame f, OverlayLayout l, Settings s)
    {
        var box = new Rect(l.BoxX, l.BoxY, l.PanelWidth, l.RowHeight);
        DrawBpmBox(f, l, box, s.ShowRealBpm);
        if (s.ShowDifficulty)
        {
            DifficultyBadge.Draw(box, l.Scale, f.DifficultyTier);
        }
    }

    /// <summary>
    /// 演示的说明：紧挨着横条（在上面时画在它下面，在下面时画在它上面；太鼓条关掉时贴着屏幕边），只占下落面板旁边空着的那一边，放不下就缩小字号。
    /// 复现（用户录屏）：英文、韩文、日文的说明比中文长得多，原来从屏幕左边画起，前半句压在四轨面板底下
    /// </summary>
    public static void DrawCaption(OverlayFrame f, OverlayLayout l, Settings s)
    {
        if (f.Caption.Source.Length == 0)
        {
            return;
        }

        var pad = 16 * l.Scale;
        var onRight = !l.LanesRight; // 面板在左边或中间：说明放右边那一块
        var left = !s.ShowLanes ? pad : onRight ? l.PanelX + l.PanelWidth + pad : pad;
        var right = !s.ShowLanes ? Screen.width - pad : onRight ? Screen.width - pad : l.PanelX - pad;
        var strip = l.StripHeight;
        var y = l.TaikoBottom ? Screen.height - strip - (30 * l.Scale) : strip + (6 * l.Scale);
        Painter.FitText(new Rect(left, y, right - left, 24 * l.Scale), Lang.T(f.Caption),
            Mathf.RoundToInt(18 * l.Scale), new Color(1f, 0.85f, 0.4f), true, onRight ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft);
    }

    private static void DrawBpmBox(OverlayFrame f, OverlayLayout l, Rect box, bool realBpm)
    {
        Painter.Rect(box, Painter.BpmBoxColor);
        var right = box.width - (18 * l.Scale);
        Painter.Text(new Rect(box.x, box.y + (8 * l.Scale), right, 64 * l.Scale), Mathf.RoundToInt(f.Bpm).ToString(),
            Mathf.RoundToInt(56 * l.Scale), Color.white, TextAnchor.MiddleRight, true);
        Painter.Text(new Rect(box.x, box.y + (66 * l.Scale), right, 20 * l.Scale), "BPM",
            Mathf.RoundToInt(16 * l.Scale), new Color(0.85f, 0.9f, 1f), TextAnchor.MiddleRight, true);
        if (realBpm)
        {
            // 实际 BPM 放在「BPM」左边同一行（2026-11-26 用户选方案 A：大号数字仍是砖块 BPM）；停顿、还没开始时写「-」，这一行不跳
            var real = f.RealBpm > 0 ? Mathf.RoundToInt(f.RealBpm).ToString() : "-";
            Painter.Text(new Rect(box.x, box.y + (66 * l.Scale), right - (46 * l.Scale), 20 * l.Scale), Lang.T("实际 {0}", real),
                Mathf.RoundToInt(16 * l.Scale), new Color(1f, 0.85f, 0.4f), TextAnchor.MiddleRight, true);
        }

        var left = System.Math.Max(0, f.EndTime - f.Now);
        var text = $"{(int)(left / 60)}:{(int)(left % 60):00}";
        Painter.Text(new Rect(box.x, box.y + (88 * l.Scale), right, 30 * l.Scale), text,
            Mathf.RoundToInt(24 * l.Scale), Color.white, TextAnchor.MiddleRight, true);
    }

    private static void DrawTrack(OverlayFrame f, OverlayLayout l, Settings s, Rect area)
    {
        GUI.BeginGroup(area);
        try
        {
            DrawTrackInside(f, l, s, area);
        }
        finally
        {
            // 画到一半出错时也要关上裁剪组，否则后面画的东西（设置窗口）都被裁进太鼓条里（同 LaneView）
            GUI.EndGroup();
        }
    }

    private static void DrawTrackInside(OverlayFrame f, OverlayLayout l, Settings s, Rect area)
    {
        var cy = area.height / 2;
        var judgeX = 84 * l.Scale;
        var speed = BaseSpeed * Mathf.Max(0.2f, s.TaikoSpeed) * l.Scale;
        var lookAhead = (area.width - judgeX + (60 * l.Scale)) / speed;

        // 节拍线：小节线亮，普通拍暗
        for (var i = BeatGrid.FirstAtOrAfter(f.Beats, f.Now - 0.05); i < f.Beats.Count; i++)
        {
            var dt = f.Beats[i].Time - f.Now;
            if (dt > lookAhead)
            {
                break;
            }

            var x = judgeX + (float)(dt * speed);
            var measure = f.Beats[i].IsMeasure;
            var w = (measure ? 2f : 1f) * l.Scale;
            var c = measure ? new Color(0.38f, 0.38f, 0.42f) : new Color(0.2f, 0.2f, 0.23f);
            Painter.Rect(new Rect(x - (w / 2), 6 * l.Scale, w, area.height - (12 * l.Scale)), c);
        }

        DrawJudgeCircle(f, l, new Vector2(judgeX, cy));
        var alpha = Painter.Alpha;
        Painter.Alpha = alpha * Mathf.Clamp01(s.NoteOpacity); // 音符按「音符不透明度」淡下去，判定圈不变
        DrawActiveHolds(f, l, judgeX, cy, speed, s.ShowChordCount);
        DrawNotes(f, l, judgeX, cy, speed, lookAhead, s.ShowChordCount);
        Painter.Alpha = alpha;

        Painter.Text(new Rect(judgeX - (40 * l.Scale), area.height - (24 * l.Scale), 80 * l.Scale, 20 * l.Scale),
            f.NextNote.ToString(), Mathf.RoundToInt(15 * l.Scale), new Color(0.75f, 0.75f, 0.8f));
    }

    private static void DrawJudgeCircle(OverlayFrame f, OverlayLayout l, Vector2 c)
    {
        var a = 1f - ((Time.unscaledTime - f.TaikoFlash.Time) / FlashSeconds);
        BallSkin.Judge(c, 46 * l.Scale, 4 * l.Scale, false, a, f.TaikoFlash.Color);
    }

    /// <summary>按住中的长按：圆停在判定圈上脉动，尾巴从右往左缩短，松开时消失。</summary>
    private static void DrawActiveHolds(OverlayFrame f, OverlayLayout l, float judgeX, float cy, float speed, bool chordCount)
    {
        PlannedNote? drawn = null;
        var pulse = 0.5f + (0.5f * Mathf.Sin(Time.unscaledTime * 18f));
        foreach (var h in Holds.Active(f.Notes, f.NextNote, f.Now))
        {
            if (ReferenceEquals(h.Note, drawn))
            {
                continue; // 多押的几条长按画成一个
            }

            drawn = h.Note;
            var r = (h.Note.IsChord ? 40 : 34) * l.Scale;
            var color = Painter.NoteColor(h.Note);
            var len = (float)(h.Remaining * speed);
            BallSkin.Tail(new Rect(judgeX, cy - (r * 0.55f), len, r * 1.1f), color.WithAlpha(0.8f));
            BallSkin.HoldHead(new Vector2(judgeX, cy), r, color, 4 * l.Scale, pulse);
            if (chordCount)
            {
                BallSkin.ChordCount(new Vector2(judgeX, cy), r, h.Note.Lanes.Count);
            }
        }
    }

    private static void DrawNotes(OverlayFrame f, OverlayLayout l, float judgeX, float cy, float speed, double lookAhead, bool chordCount)
    {
        // 从近到远画，后面的音符压在前面的上面（用户要求；原来近的压在上面）
        for (var i = f.NextNote; i < f.VisibleEnd(lookAhead); i++)
        {
            var note = f.Notes[i];
            var dt = f.TimeTo(i);
            if (dt < -0.15)
            {
                continue;
            }

            var x = judgeX + (float)(dt * speed);
            var r = (note.IsChord ? 40 : 34) * l.Scale;
            var color = Painter.NoteColor(note);
            var hold = 0.0;
            foreach (var h in note.Lanes.Values)
            {
                hold = System.Math.Max(hold, h);
            }

            if (hold > 0)
            {
                var len = (float)(hold * speed);
                BallSkin.Tail(new Rect(x, cy - (r * 0.55f), len, r * 1.1f), color.WithAlpha(0.6f));
            }

            BallSkin.Note(new Vector2(x, cy), r, color, 4 * l.Scale);
            if (chordCount)
            {
                BallSkin.ChordCount(new Vector2(x, cy), r, note.Lanes.Count);
            }
        }
    }
}
