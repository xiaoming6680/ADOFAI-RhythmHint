using System;
using System.Collections.Generic;
using RhythmHint.Core;
using UnityEngine;
using static RhythmHint.WindowSkin;

namespace RhythmHint;

/// <summary>
/// 设置窗口里的小插图：音符样式预览、屏幕位置示意、手序编辑器示意、键位图、节奏分界尺。
/// 四轨面板、太鼓条、BPM 框照游戏里的画法缩小（同样用 Painter、BallSkin，尺寸和颜色和 LaneView、TopBarView 一样；
/// 2026-11-19 用户：插图和游戏里的不像），放一小段循环的演示谱；编辑器示意照编辑器的谱面（竖排轨道，两边两种自动排法压暗）。
/// 跟着设置变化时带动画。只能在 OnGUI 里调用。
/// </summary>
internal static class SettingsArt
{
    /// <summary>演示谱一轮多长（秒）：4 拍，每拍 0.6 秒（100 BPM）。</summary>
    private const float Period = 2.4f;

    private const float Beat = 0.6f;

    /// <summary>命中闪光多久（同 LaneView）。</summary>
    private const float FlashSeconds = 0.18f;

    private static readonly Color Separator = new(0.2f, 0.2f, 0.22f);
    private static readonly Color Scene = new(0.11f, 0.09f, 0.17f);

    /// <summary>演示谱：时间、轨道、长按多长。单押、双押、三押和一个长按都有。</summary>
    private static readonly (float Time, int[] Lanes, float Hold)[] Pattern =
    {
        (0f, new[] { 1 }, 0f), (0.3f, new[] { 2, 3 }, 0f), (0.6f, new[] { 0 }, 0f), (0.9f, new[] { 0, 1, 2 }, 0f),
        (1.2f, new[] { 3 }, 0.45f), (1.8f, new[] { 1 }, 0f), (2.1f, new[] { 2 }, 0f),
    };

    private static Color ColorOf(int lanes) => lanes > 2 ? Painter.Purple : lanes > 1 ? Painter.Blue : Painter.Orange;

    /// <summary>离这个音符下一次到达还有多久（0 到一轮）。</summary>
    private static float Until(float time, float now) => Mathf.Repeat(time - now, Period);

    /// <summary>这个音符上一次到达过去多久。</summary>
    private static float Since(float time, float now) => Mathf.Repeat(now - time, Period);

    /// <summary>按键框上写的键名（玩家自己的键位，长名字缩写）。</summary>
    public static string[] KeyLabels(Settings s) => Array.ConvertAll(KeyBindings.Split(s.KeyLabels), KeyBindings.Display);

    // ---------- 四轨面板 ----------

    /// <summary>
    /// 音符样式预览：深色背景正中一块缩小的四轨面板（高度占满 r），演示谱一直往下落。
    /// </summary>
    public static void LanePreview(Rect r, bool round, string[] labels)
    {
        Fill(r, Scene, 8);
        Glow(new Rect(r.x, r.y - (r.height * 0.3f), r.width, r.height * 1.3f), With(new Color(0.45f, 0.3f, 0.75f), 0.18f));
        var s = r.height / 270f;
        var w = 272 * s;
        LanePanel(new Rect(r.center.x - (w / 2), r.y, w, r.height), s, round, labels, 210 * s);
    }

    /// <summary>
    /// 缩小的四轨面板，画法和 LaneView 一样：s 是相对 1080p 的缩放（面板宽 272×s），r 的高度就是面板高度，speed 是下落速度（像素 / 秒）。
    /// dim 小于 1 时音符压暗（编辑器示意两边的自动排法）；keys 为 false 时不画判定线下面的按键框。
    /// </summary>
    public static void LanePanel(Rect r, float s, bool round, string[] labels, float speed, float dim = 1, bool keys = true)
    {
        if (!Repaint || r.width <= 0 || r.height <= 0)
        {
            return;
        }

        var saved = Painter.Alpha;
        Painter.Alpha = Alpha;
        GUI.BeginGroup(r);
        try
        {
            DrawLanes(r.width, r.height, s, round, labels, speed, dim, keys, Time.unscaledTime);
        }
        finally
        {
            GUI.EndGroup();
            Painter.Alpha = saved;
        }
    }

    /// <summary>判定线（横条样式）或判定圈圆心（圆形样式）离面板顶多远，和 <see cref="LanePanel"/> 里一样算。</summary>
    private static float HitY(float h, float s, bool round, bool keys) =>
        round ? h - ((29 + 18) * s) : h - (keys ? 64 * s : 0) - (16 * s);

    private static void DrawLanes(float w, float h, float s, bool round, string[] labels, float speed, float dim, bool keys, float now)
    {
        Color Dim(Color c) => dim >= 1 ? c : Color.Lerp(Painter.PanelColor, c, dim);
        var lane = w / 4;
        var judgeY = HitY(h, s, false, keys);
        var hitY = HitY(h, s, round, keys);
        Painter.Rect(new Rect(0, 0, w, h), Painter.PanelColor);

        var sep = Mathf.Max(1, 2 * s);
        for (var i = 0; i <= 4; i++)
        {
            Painter.Rect(new Rect(Mathf.Clamp((i * lane) - s, 0, w - sep), 0, sep, round ? h : judgeY), Separator);
        }

        // 节拍线：每轮第一拍是小节线（亮一点、粗一点）
        for (var k = 0; k < 4; k++)
        {
            var y = hitY - (Until(k * Beat, now) * speed);
            var bh = Mathf.Max(1, (k == 0 ? 2f : 1f) * s);
            Painter.Rect(new Rect(0, y - (bh / 2), w, bh), k == 0 ? new Color(0.36f, 0.36f, 0.4f) : new Color(0.2f, 0.2f, 0.23f));
        }

        // 这一刻每条轨道：按着没有、命中闪光、按住中的长按
        var lit = new bool[4];
        var flash = new float[4];
        var flashColor = new Color[4];
        var holds = new List<(int Lane, float Left, Color Color)>();
        foreach (var (time, lanes, hold) in Pattern)
        {
            var since = Since(time, now);
            var color = Dim(ColorOf(lanes.Length));
            foreach (var l in lanes)
            {
                if (since < FlashSeconds && 1 - (since / FlashSeconds) > flash[l])
                {
                    flash[l] = 1 - (since / FlashSeconds);
                    flashColor[l] = color;
                }

                lit[l] |= since < 0.1f || since < hold;
                if (hold > 0 && since < hold)
                {
                    holds.Add((l, hold - since, color));
                }
            }
        }

        if (round)
        {
            DrawRound(w, s, labels, speed, keys, now, hitY, lit, flash, flashColor, holds, Dim);
        }
        else
        {
            DrawBars(w, h, s, labels, speed, keys, now, judgeY, lit, flash, flashColor, holds, Dim);
        }
    }

    /// <summary>横条样式：命中时往上的光柱、按住中的长按、音符横条（白边）、判定线和按键框（同 LaneView）。</summary>
    private static void DrawBars(float w, float h, float s, string[] labels, float speed, bool keys, float now, float judgeY,
        bool[] lit, float[] flash, Color[] flashColor, List<(int Lane, float Left, Color Color)> holds, Func<Color, Color> dim)
    {
        var lane = w / 4;
        var inset = 6 * s;
        var noteW = lane - (inset * 2);
        for (var i = 0; i < 4; i++)
        {
            if (flash[i] > 0)
            {
                Painter.FadeUp(new Rect(i * lane, judgeY - (200 * s), lane, 200 * s), flashColor[i].WithAlpha(0.45f * flash[i]));
            }
        }

        var pulse = 0.5f + (0.5f * Mathf.Sin(now * 18f));
        foreach (var (l, left, color) in holds)
        {
            var x = l * lane;
            Painter.FadeUp(new Rect(x, judgeY - (200 * s), lane, 200 * s), color.WithAlpha(0.28f + (0.12f * pulse)));
            var tail = left * speed;
            Painter.Rect(new Rect(x + inset + (noteW * 0.25f), judgeY - tail, noteW * 0.5f, tail), color.WithAlpha(0.85f), 3 * s);
            Painter.Rect(new Rect(x + inset + (noteW * 0.42f), judgeY - tail, noteW * 0.16f, tail), Color.white.WithAlpha(0.55f), 2 * s);
            var head = new Rect(x + inset, judgeY - (8 * s), noteW, 16 * s);
            Painter.Rect(head, Color.Lerp(color, Color.white, 0.35f * pulse), 4 * s);
            Painter.Border(head, Color.white, Mathf.Max(1, 2 * s), 4 * s);
        }

        var barH = 16 * s;
        var outline = dim(Color.white).WithAlpha(0.85f);
        foreach (var (time, lanes, hold) in Pattern)
        {
            var y = judgeY - (Until(time, now) * speed);
            if (y < -(hold * speed) - barH)
            {
                continue;
            }

            var color = dim(ColorOf(lanes.Length));
            foreach (var l in lanes)
            {
                var x = (l * lane) + inset;
                if (hold > 0)
                {
                    var tail = hold * speed;
                    Painter.Rect(new Rect(x + (noteW * 0.25f), y - tail, noteW * 0.5f, tail), color.WithAlpha(0.6f), 3 * s);
                }

                var bar = new Rect(x, y - (barH / 2), noteW, barH);
                Painter.Rect(bar, color, 4 * s);
                Painter.Border(bar, outline, Mathf.Max(1, 2 * s), 4 * s);
            }
        }

        Painter.Rect(new Rect(0, judgeY - (4 * s), w, 8 * s), new Color(0, 0, 0, 0.7f));
        Painter.Rect(new Rect(0, judgeY - Mathf.Max(0.5f, 2 * s), w, Mathf.Max(1, 4 * s)), dim(Color.white));
        if (!keys)
        {
            return;
        }

        var size = Mathf.RoundToInt(22 * s);
        for (var i = 0; i < 4; i++)
        {
            var box = new Rect((i * lane) + (4 * s), judgeY + (10 * s), lane - (8 * s), (64 * s) - (12 * s));
            Painter.Rect(box, lit[i] ? new Color(0.3f, 0.3f, 0.34f) : new Color(0.14f, 0.14f, 0.16f), 6 * s);
            if (flash[i] > 0)
            {
                Painter.Rect(box, flashColor[i].WithAlpha(0.7f * flash[i]), 6 * s);
            }

            var held = holds.FindIndex(x => x.Lane == i);
            if (held >= 0)
            {
                Painter.Rect(box, holds[held].Color.WithAlpha(0.55f), 6 * s);
            }

            Painter.Border(box, lit[i] ? Color.white : new Color(0.35f, 0.35f, 0.4f), Mathf.Max(1, 2 * s), 6 * s);
            if (size >= 7 && i < labels.Length)
            {
                Painter.FitText(box, labels[i], size, new Color(0.85f, 0.85f, 0.9f), true);
            }
        }
    }

    /// <summary>圆形样式：判定圈（圈里写键名）、命中和长按的柔光、胶囊形长按、圆球音符（同 LaneView、BallSkin）。</summary>
    private static void DrawRound(float w, float s, string[] labels, float speed, bool keys, float now, float hitY,
        bool[] lit, float[] flash, Color[] flashColor, List<(int Lane, float Left, Color Color)> holds, Func<Color, Color> dim)
    {
        var lane = w / 4;
        var judge = 29 * s;
        var ball = 25 * s;
        var rim = Mathf.Max(1, 3 * s);
        Painter.Rect(new Rect(0, hitY - s, w, Mathf.Max(1, 2 * s)), new Color(0.28f, 0.28f, 0.32f));
        var breath = 0.5f + (0.5f * Mathf.Sin(now * 6f));
        foreach (var (l, _, color) in holds)
        {
            Painter.Glow(new Vector2((l * lane) + (lane / 2), hitY), judge * 2, color.WithAlpha(0.3f + (0.2f * breath)));
        }

        for (var i = 0; i < 4; i++)
        {
            if (flash[i] > 0)
            {
                Painter.Glow(new Vector2((i * lane) + (lane / 2), hitY), judge * 2.2f, flashColor[i].WithAlpha(0.7f * flash[i]));
            }
        }

        if (keys)
        {
            var size = Mathf.RoundToInt(20 * s);
            for (var i = 0; i < 4; i++)
            {
                var c = new Vector2((i * lane) + (lane / 2), hitY);
                BallSkin.Judge(c, judge, rim, lit[i], flash[i], flashColor[i]);
                if (size >= 7 && i < labels.Length)
                {
                    Painter.FitText(new Rect(c.x - (judge * 0.8f), c.y - (judge * 0.8f), judge * 1.6f, judge * 1.6f), labels[i], size,
                        new Color(0.85f, 0.85f, 0.9f), true);
                }
            }
        }

        foreach (var (l, left, color) in holds)
        {
            var x = (l * lane) + (lane / 2);
            BallSkin.HoldBody(x, hitY, hitY - (left * speed), ball, color, rim);
            BallSkin.Note(new Vector2(x, hitY), ball, Color.Lerp(color, Color.white, 0.3f), rim);
        }

        // 从远到近画，近的压在上面（同 LaneView）
        var order = new List<(float Until, int Index)>();
        for (var i = 0; i < Pattern.Length; i++)
        {
            order.Add((Until(Pattern[i].Time, now), i));
        }

        order.Sort((a, b) => b.Until.CompareTo(a.Until));
        foreach (var (until, index) in order)
        {
            var (_, lanes, hold) = Pattern[index];
            var y = hitY - (until * speed);
            if (y < -(hold * speed) - ball)
            {
                continue;
            }

            var color = dim(ColorOf(lanes.Length));
            foreach (var l in lanes)
            {
                var x = (l * lane) + (lane / 2);
                if (hold > 0)
                {
                    BallSkin.HoldBody(x, y, y - (hold * speed), ball, color, rim);
                }

                BallSkin.Note(new Vector2(x, y), ball, color, rim);
            }
        }
    }

    // ---------- 太鼓条、BPM 框 ----------

    /// <summary>缩小的太鼓条节奏区（BPM 框以外那一段），画法和 TopBarView 一样：圆球从右往左滚到判定圈，长按带尾巴。</summary>
    public static void TaikoTrack(Rect r, float s, float speed, bool chordCount)
    {
        if (!Repaint || r.width <= 0 || r.height <= 0)
        {
            return;
        }

        var saved = Painter.Alpha;
        Painter.Alpha = Alpha;
        GUI.BeginGroup(r);
        try
        {
            DrawTrack(r.width, r.height, s, speed, Time.unscaledTime, chordCount);
        }
        finally
        {
            GUI.EndGroup();
            Painter.Alpha = saved;
        }
    }

    private static void DrawTrack(float w, float h, float s, float speed, float now, bool chordCount)
    {
        var cy = h / 2;
        var judgeX = 84 * s;
        var cycles = Mathf.CeilToInt((w - judgeX) / Mathf.Max(1, speed * Period)) + 1; // 节奏区比一轮长时多画几轮
        for (var c = 0; c < cycles; c++)
        {
            for (var k = 0; k < 4; k++)
            {
                var x = judgeX + ((Until(k * Beat, now) + (c * Period)) * speed);
                var bw = Mathf.Max(1, (k == 0 ? 2f : 1f) * s);
                Painter.Rect(new Rect(x - (bw / 2), 6 * s, bw, h - (12 * s)), k == 0 ? new Color(0.38f, 0.38f, 0.42f) : new Color(0.2f, 0.2f, 0.23f));
            }
        }

        var flash = 0f;
        var flashColor = Painter.Orange;
        foreach (var (time, lanes, _) in Pattern)
        {
            var since = Since(time, now);
            if (since < 0.15f && 1 - (since / 0.15f) > flash)
            {
                flash = 1 - (since / 0.15f);
                flashColor = ColorOf(lanes.Length);
            }
        }

        BallSkin.Judge(new Vector2(judgeX, cy), 46 * s, Mathf.Max(1, 4 * s), false, flash, flashColor);
        var pulse = 0.5f + (0.5f * Mathf.Sin(now * 18f));
        foreach (var (time, lanes, hold) in Pattern)
        {
            var since = Since(time, now);
            if (hold > 0 && since < hold)
            {
                var r = (lanes.Length > 1 ? 40 : 34) * s;
                var color = ColorOf(lanes.Length);
                BallSkin.Tail(new Rect(judgeX, cy - (r * 0.55f), (hold - since) * speed, r * 1.1f), color.WithAlpha(0.8f));
                BallSkin.HoldHead(new Vector2(judgeX, cy), r, color, Mathf.Max(1, 4 * s), pulse);
                if (chordCount)
                {
                    BallSkin.ChordCount(new Vector2(judgeX, cy), r, lanes.Length);
                }
            }
        }

        // 从近到远画，后面的音符压在前面的上面（同 TopBarView）
        var order = new List<(float Until, int Index)>();
        for (var c = 0; c < cycles; c++)
        {
            for (var i = 0; i < Pattern.Length; i++)
            {
                order.Add((Until(Pattern[i].Time, now) + (c * Period), i));
            }
        }

        order.Sort((a, b) => a.Until.CompareTo(b.Until));
        foreach (var (until, index) in order)
        {
            var (_, lanes, hold) = Pattern[index];
            var x = judgeX + (until * speed);
            if (x > w + (40 * s))
            {
                break;
            }

            var r = (lanes.Length > 1 ? 40 : 34) * s;
            var color = ColorOf(lanes.Length);
            if (hold > 0)
            {
                BallSkin.Tail(new Rect(x, cy - (r * 0.55f), hold * speed, r * 1.1f), color.WithAlpha(0.6f));
            }

            BallSkin.Note(new Vector2(x, cy), r, color, Mathf.Max(1, 4 * s));
            if (chordCount)
            {
                BallSkin.ChordCount(new Vector2(x, cy), r, lanes.Length);
            }
        }
    }

    /// <summary>缩小的 BPM 框（同 TopBarView）：蓝底，右边写 BPM 和剩余时间；字太小时不写。</summary>
    public static void BpmBox(Rect r, float s)
    {
        if (!Repaint)
        {
            return;
        }

        var saved = Painter.Alpha;
        Painter.Alpha = Alpha;
        Painter.Rect(r, Painter.BpmBoxColor);
        var right = r.width - (18 * s);
        var big = Mathf.RoundToInt(56 * s);
        if (big >= 8)
        {
            Painter.Text(new Rect(r.x, r.y + (8 * s), right, 64 * s), "100", big, Color.white, TextAnchor.MiddleRight, true);
        }

        if (Mathf.RoundToInt(16 * s) >= 6)
        {
            Painter.Text(new Rect(r.x, r.y + (66 * s), right, 20 * s), "BPM", Mathf.RoundToInt(16 * s), new Color(0.85f, 0.9f, 1f), TextAnchor.MiddleRight, true);
            Painter.Text(new Rect(r.x, r.y + (88 * s), right, 30 * s), "1:23", Mathf.RoundToInt(24 * s), Color.white, TextAnchor.MiddleRight, true);
        }

        Painter.Alpha = saved;
    }

    /// <summary>
    /// 屏幕示意图（外观页「位置」）：按 1920×1080 的比例缩小画整个屏幕，四轨面板、太鼓条、BPM 框都照游戏里的样子，
    /// 跟着设置走（左中右、上下、显示哪些、音符样式、键位）；换边时滑过去，自适应画幅开着时游戏画面（两颗星球）让到剩下的区域
    /// （面板在中间时不让，盖在画面上）。
    /// </summary>
    public static void ScreenLayout(Rect r, Settings s)
    {
        var k = r.width / 1920f;
        var keys = DemoPlayer.DemoKeys(s);
        var panelW = Motion.Ease("art:scr:panelW", OverlayLayout.LaneBase(keys) * keys * k, 0.2f);
        var stripH = 128 * k;
        var top = s.ShowTaiko || s.ShowBpm;
        var lanesA = Motion.Ease("art:scr:lanes", s.ShowLanes ? 1 : 0, 0.18f);
        var stripA = Motion.Ease("art:scr:strip", top ? 1 : 0, 0.18f);
        var right = Motion.Ease("art:scr:right", !s.LanesInCenter && s.LanesOnRight ? 1 : 0, 0.28f);
        var center = Motion.Ease("art:scr:center", s.LanesInCenter ? 1 : 0, 0.28f);
        var leftW = 1 - right - center;
        var lx = (right + (center / 2)) * (r.width - panelW);
        var boxX = s.ShowTaiko ? right * (r.width - panelW) : lx; // 太鼓条开着时 BPM 框在面板那一侧，面板在中间时放左边
        var by = Motion.Ease("art:scr:by", s.TaikoAtBottom ? r.height - stripH : 0, 0.28f);
        var lanesTop = Motion.Ease("art:scr:lt", s.TaikoAtBottom || !top ? 0 : stripH, 0.28f);
        var lanesH = Motion.Ease("art:scr:lh", top ? r.height - stripH : r.height, 0.28f);

        // 游戏画面：自适应画幅时缩到面板以外的区域
        Fill(r, Scene, 8);
        Glow(new Rect(r.x + (r.width * 0.15f), r.y - (r.height * 0.2f), r.width * 0.85f, r.height * 1.3f), With(new Color(0.45f, 0.3f, 0.75f), 0.25f));
        var shrink = Motion.Ease("art:scr:g", s.ShrinkGameView ? 1 : 0, 0.2f);
        var freeX = panelW * lanesA * leftW * shrink;
        var freeW = r.width - (panelW * lanesA * (leftW + right) * shrink);
        var freeY = (s.TaikoAtBottom ? 0 : stripH * stripA) * shrink;
        var freeH = r.height - (stripH * stripA * shrink);
        var planets = new Vector2(r.x + freeX + (freeW / 2), r.y + freeY + (freeH / 2));
        var radius = 34 * k * Mathf.Lerp(1, freeH / r.height, shrink);
        Circle(new Vector2(planets.x - (radius * 1.2f), planets.y), radius, new Color(0.89f, 0.34f, 0.29f));
        Circle(new Vector2(planets.x + (radius * 1.2f), planets.y), radius, new Color(0.29f, 0.56f, 0.89f));

        if (Repaint)
        {
            GUI.BeginGroup(r);
            var saved = Alpha;
            try
            {
                Alpha = saved * stripA;
                if (stripA > 0.01f)
                {
                    if (s.ShowTaiko)
                    {
                        Fill(new Rect(0, by, r.width, stripH), Painter.PanelColor);
                        var trackX = s.ShowBpm ? panelW * (1 - (boxX / Mathf.Max(1, r.width - panelW))) : 0;
                        var trackW = s.ShowBpm ? r.width - panelW : r.width;
                        TaikoTrack(new Rect(trackX, by, trackW, stripH), k, 650 * k, s.ShowChordCount);
                    }

                    if (s.ShowBpm)
                    {
                        BpmBox(new Rect(boxX, by, panelW, stripH), k);
                    }
                }

                Alpha = saved * lanesA;
                if (lanesA > 0.01f)
                {
                    LanePanel(new Rect(lx, lanesTop, panelW, lanesH), k, s.RoundNotes, KeyLabels(s), 1100 * k * 0.45f);
                }
            }
            finally
            {
                Alpha = saved;
                GUI.EndGroup();
            }
        }

        Stroke(r, Border2, 1, 8);
    }

    // ---------- 键帽 ----------

    /// <summary>四个键帽（概览页「调整排键」的图）：右手食指那个描橙边。</summary>
    public static void Keys(Rect r, string[] keys)
    {
        const float size = 40;
        var total = (4 * size) + (2 * 8) + 26;
        var x = r.center.x - (total / 2);
        for (var i = 0; i < 4; i++)
        {
            var cap = new Rect(x, r.center.y - (size / 2), size, size);
            Cap(cap, KeyBindings.Display(i < keys.Length ? keys[i] : string.Empty), i == 2);
            x += size + (i == 1 ? 26 : 8);
        }
    }

    /// <summary>不能点的键帽（插图用）。</summary>
    public static void Cap(Rect r, string text, bool highlight, int size = 14)
    {
        Fill(new Rect(r.x, r.y + 3, r.width, r.height - 3), new Color(0.09f, 0.1f, 0.11f), 7);
        var face = new Rect(r.x, r.y, r.width, r.height - 3);
        Fill(face, Surface3, 7);
        Stroke(face, highlight ? Accent : With(Color.white, 0.06f), highlight ? 1.5f : 1, 7);
        Label(face, text, size, highlight ? Accent2 : Text, Face.Mono, true, TextAnchor.MiddleCenter);
    }

    // ---------- 编辑器 ----------

    /// <summary>
    /// 手序编辑器的示意（概览页、专属手序页），照编辑器的谱面画：深色圆角框、顶上一条标题栏，左边小节号的窄栏，
    /// 中间是当前手序（和游戏一样的四轨、判定线、按键框），两边是两种自动排法（压暗、不画按键框）。
    /// 音符往下走，三押那一组描橙色的选中框，右下角一支笔。
    /// </summary>
    public static void EditorArt(Rect r, string[] labels)
    {
        if (!Repaint)
        {
            return;
        }

        Fill(r, new Color(0.09f, 0.094f, 0.11f), 8);
        const float header = 12;
        Fill(new Rect(r.x, r.y, r.width, header), Surface, new Vector4(8, 8, 0, 0));
        Fill(new Rect(r.x, r.y + header, r.width, 1), Border);
        var body = new Rect(r.x + 1, r.y + header + 1, r.width - 2, r.height - header - 2);
        const float ruler = 12;
        Fill(new Rect(body.x, body.y, ruler, body.height), new Color(0.082f, 0.082f, 0.098f), new Vector4(0, 0, 0, 7));
        var cols = body.width - ruler - 4;
        var mainW = cols * 0.46f;
        var sideW = (cols - mainW) / 2;
        var x = body.x + ruler + 2;
        var speed = body.height * 0.55f;
        var std = new Rect(x, body.y, sideW, body.height);
        var main = new Rect(std.xMax + 1, body.y, mainW - 2, body.height);
        var alt = new Rect(main.xMax + 1, body.y, sideW, body.height);

        // 两边的自动排法：和游戏同样的音符，压暗、窄一点、没有按键框
        LanePanel(std, std.width / 272f, false, labels, speed, 0.45f, false);
        LanePanel(alt, alt.width / 272f, false, labels, speed, 0.45f, false);
        var ms = main.width / 272f;
        LanePanel(main, ms, false, labels, speed);
        Fill(new Rect(std.xMax, body.y, 1, body.height), Border);
        Fill(new Rect(main.xMax, body.y, 1, body.height), Border);

        // 标题栏上三列的小标记：当前手序橙色，两种自动排法浅蓝、浅黄
        Fill(new Rect(std.center.x - 8, r.y + 4, 16, 3), With(new Color(0.56f, 0.83f, 1f), 0.7f), 1.5f);
        Fill(new Rect(main.center.x - 12, r.y + 4, 24, 3), Accent, 1.5f);
        Fill(new Rect(alt.center.x - 8, r.y + 4, 16, 3), With(new Color(1f, 0.82f, 0.56f), 0.7f), 1.5f);

        // 左边小节号栏：每拍一道刻度，小节线长一点
        var hitY = HitY(main.height, ms, false, true);
        var now = Time.unscaledTime;
        for (var k = 0; k < 4; k++)
        {
            var y = body.y + hitY - (Until(k * Beat, now) * speed);
            if (y > body.y && y < body.yMax)
            {
                Fill(new Rect(body.x + (k == 0 ? 3 : 7), y, k == 0 ? 8 : 4, 1), With(Text3, 0.8f));
            }
        }

        // 三押那一组：橙色选中框跟着走
        var triple = Pattern[3];
        var ty = main.y + hitY - (Until(triple.Time, now) * speed);
        var lane = main.width / 4;
        if (ty > main.y + 4 && ty < main.y + hitY - 2)
        {
            var box = new Rect(main.x + 2, ty - (10 * ms) - 2, (lane * 3) - 4, (20 * ms) + 4);
            Stroke(box, Accent2, 1.5f, 3);
        }

        Icon(new Rect(r.xMax - 20, r.yMax - 20, 14, 14), "edit", Accent2);
        Stroke(r, Border, 1, 8);
    }

    /// <summary>
    /// 键位图（排键页）：左手两个键、右手两个键；主手一组描橙框，多押优先用的两个键带蓝色光。键帽上写玩家自己的键位。
    /// </summary>
    public static void HandMap(Rect r, string[] keys, bool mainLeft, bool chordLeft, string leftLabel, string rightLabel, string mainLabel)
    {
        const float cap = 52;
        const float groupW = (cap * 2) + 8 + 28;
        const float groupH = cap + 52;
        var x = r.center.x - groupW - 18;
        for (var side = 0; side < 2; side++)
        {
            var left = side == 0;
            var g = new Rect(x, r.y, groupW, groupH);
            var main = Motion.Ease("art:hand:main" + side, left == mainLeft ? 1 : 0, 0.3f);
            var chord = Motion.Ease("art:hand:chord" + side, left == chordLeft ? 1 : 0, 0.3f);
            Fill(g, With(Accent, 0.07f * main), 12);
            Stroke(g, With(Accent, 0.4f * main), 1, 12);
            for (var i = 0; i < 2; i++)
            {
                var lane = (side * 2) + i;
                var c = new Rect(g.x + 14 + (i * (cap + 8)), g.y + 12, cap, cap);
                Glow(new Rect(c.x - 16, c.y - 12, c.width + 32, c.height + 30), With(Blue, 0.35f * chord));
                Fill(new Rect(c.x, c.y + 3, c.width, c.height - 3), new Color(0.09f, 0.1f, 0.11f), 8);
                var face = new Rect(c.x, c.y, c.width, c.height - 3);
                Fill(face, Surface3, 8);
                Stroke(face, Mix(With(Color.white, 0.06f), Blue, chord), 1 + (chord * 0.5f), 8);
                var text = KeyBindings.Display(lane < keys.Length ? keys[lane] : string.Empty);
                Label(face, text, text.Length > 3 ? 13 : 18, Mix(Text, new Color(0.56f, 0.79f, 0.95f), chord), Face.Mono, true, TextAnchor.MiddleCenter);
            }

            var label = left ? leftLabel : rightLabel;
            var lw = Measure(label, 13).x;
            var mw = Measure(mainLabel, 13, Face.Body, true).x;
            var total = lw + (main > 0.5f ? 10 + mw : 0);
            var lx = g.center.x - (total / 2);
            Label(new Rect(lx, g.yMax - 32, lw + 2, 22), label, 13, Text3);
            if (main > 0.01f)
            {
                Label(new Rect(lx + lw + 10, g.yMax - 32, mw + 2, 22), mainLabel, 13, With(Accent2, main), bold: true);
            }

            x += groupW + 36;
        }
    }

    /// <summary>
    /// 节奏分界尺（排键页）：0–300 毫秒的色带，按主手重置间隔 R 分成三段：R/2 以内同手轮指、R/2–R 左右交替、R 以上主手食指单点。
    /// 拖滑块时分界线跟着滑。
    /// </summary>
    public static void Ruler(Rect r, int resetMs, string zone1, string zone2, string zone3, string axis)
    {
        const float max = 300;
        var reset = Motion.Ease("art:ruler", resetMs, 0.25f);
        var bar = new Rect(r.x, r.y + 26, r.width, 28);
        float X(float ms) => bar.x + (bar.width * Mathf.Clamp01(ms / max));
        var a = X(reset / 2);
        var b = X(reset);
        Zone(new Rect(bar.x, bar.y, a - bar.x, bar.height), Purple, zone1, new Vector4(8, 0, 0, 8));
        Zone(new Rect(a, bar.y, b - a, bar.height), Blue, zone2, Vector4.zero);
        Zone(new Rect(b, bar.y, bar.xMax - b, bar.height), Accent, zone3, new Vector4(0, 8, 8, 0));
        Tick(a, bar, Mathf.RoundToInt(reset / 2));
        Tick(b, bar, Mathf.RoundToInt(reset));
        for (var ms = 0; ms <= 300; ms += 100)
        {
            var text = ms == 300 ? "300 ms" : ms.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var w = Measure(text, 11, Face.Mono).x;
            var x = ms == 0 ? bar.x : ms == 300 ? bar.xMax - w : X(ms) - (w / 2);
            Label(new Rect(x, bar.yMax + 8, w + 2, 16), text, 11, Text3, Face.Mono);
        }

        var axisW = Measure(axis, 12).x;
        Label(new Rect(bar.center.x - (axisW / 2), bar.yMax + 26, axisW + 2, 18), axis, 12, Text3);
    }

    private static void Zone(Rect r, Color c, string label, Vector4 radii)
    {
        if (r.width <= 0.5f)
        {
            return;
        }

        Fill(r, With(c, 0.15f), radii);
        var text = Ellipsize(label, 12, r.width - 10, Face.Body, true);
        if (text.Length > 1)
        {
            Label(r, text, 12, Mix(c, Color.white, 0.35f), bold: true, anchor: TextAnchor.MiddleCenter);
        }
    }

    private static void Tick(float x, Rect bar, int ms)
    {
        Fill(new Rect(x - 1, bar.y - 8, 2, bar.height + 16), Text2, 1);
        var text = ms.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var w = Measure(text, 12, Face.Mono, true).x;
        Label(new Rect(x - (w / 2), bar.y - 28, w + 2, 18), text, 12, Text, Face.Mono, true);
    }

    /// <summary>
    /// 画面偏移示意（校准页）：中间一条判定线，音符按偏移量往「提前」或「推后」那边挪（正数是提前显示）。
    /// </summary>
    public static void Offset(Rect r, int offsetMs, string earlier, string later)
    {
        Fill(r, Well, 9);
        Stroke(r, Border, 1, 9);
        Label(new Rect(r.x + 14, r.y, r.width / 2, r.height), earlier, 12, Text3);
        Label(new Rect(r.center.x, r.y, (r.width / 2) - 14, r.height), later, 12, Text3, anchor: TextAnchor.MiddleRight);
        Fill(new Rect(r.center.x - 1, r.y + 7, 2, r.height - 14), With(Color.white, 0.4f), 1);
        var x = Motion.Ease("art:offset", r.center.x - (offsetMs * 1.6f), 0.18f);
        Glow(new Rect(x - 22, r.center.y - 22, 44, 44), With(Accent, 0.55f));
        Circle(new Vector2(x, r.center.y), 10, Accent);
    }
}
