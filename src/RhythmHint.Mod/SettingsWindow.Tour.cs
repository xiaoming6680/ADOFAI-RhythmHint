using RhythmHint.Core;
using UnityEngine;
using static RhythmHint.WindowSkin;

namespace RhythmHint;

/// <summary>
/// 首次打开的引导（2026-11-19 用户：玩家都不知道能换皮肤、改排键）：一句欢迎，然后依次照亮外观、按键、排键、专属手序和关闭键，
/// 每一步切到对应的页；可以跳过，标题栏的问号可以再看一遍。看完或跳过后记进设置（<see cref="Settings.GuideVersion"/>），以后不再自动弹。
/// </summary>
internal sealed partial class SettingsWindow
{
    /// <summary>引导的版本：内容大改时加一，看过旧版的玩家也会再看一次（2：2026-11-22 设置重新分组、多了按键页）。</summary>
    private const int GuideRevision = 2;

    /// <summary>欢迎、外观、按键、排键、专属手序、关闭键。</summary>
    private const int TourSteps = 6;

    private int _tourStep = -1;

    private void StartTour()
    {
        _tourStep = 0;
        SelectPage(Page.Home);
        Motion.Set("tour:in", 0);
    }

    private void EndTour()
    {
        _tourStep = -1;
        var s = Main.Settings;
        if (s.GuideVersion < GuideRevision)
        {
            s.GuideVersion = GuideRevision;
            s.Save(Main.Mod);
        }
    }

    private void NextTourStep()
    {
        _tourStep++;
        if (_tourStep >= TourSteps)
        {
            EndTour();
            SelectPage(Page.Home);
            return;
        }

        SelectPage(_tourStep switch
        {
            1 => Page.Look,
            2 => Page.Keys,
            3 => Page.Rules,
            4 => Page.Custom,
            _ => Page.Home,
        });
    }

    /// <summary>引导盖在整个窗口上：四周压暗，照亮的那一块描一圈一呼一吸的橙边，旁边一张说明卡。</summary>
    private void Tour(Rect whole)
    {
        _ui.Interactive = Visible;
        _ui.Clip = whole;
        _ui.Scope = "tour:";
        var appear = Motion.Ease("tour:in", 1, 0.3f);
        Rect? target = _tourStep switch
        {
            1 => _navRects[Page.Look],
            2 => _navRects[Page.Keys],
            3 => _navRects[Page.Rules],
            4 => _navRects[Page.Custom],
            5 => _closeRect,
            _ => null,
        };
        var dim = new Color(0.024f, 0.027f, 0.035f, 0.74f * appear);
        if (target is { } t)
        {
            var goal = new Rect(t.x - 4, t.y - 4, t.width + 8, t.height + 8);
            var hole = new Rect(Motion.Ease("tour:x", goal.x, 0.35f), Motion.Ease("tour:y", goal.y, 0.35f),
                Motion.Ease("tour:w", goal.width, 0.35f), Motion.Ease("tour:h", goal.height, 0.35f));
            Fill(new Rect(0, 0, whole.width, hole.y), dim, new Vector4(Corner, Corner, 0, 0));
            Fill(new Rect(0, hole.yMax, whole.width, whole.height - hole.yMax), dim, new Vector4(0, 0, Corner, Corner));
            Fill(new Rect(0, hole.y, hole.x, hole.height), dim);
            Fill(new Rect(hole.xMax, hole.y, whole.width - hole.xMax, hole.height), dim);
            WindowSkin.Stroke(hole, With(Accent, appear), 2, 12);
            var p = Mathf.Repeat(Time.unscaledTime * 0.8f, 1f);
            var grow = 2 + (8 * p);
            WindowSkin.Stroke(new Rect(hole.x - grow, hole.y - grow, hole.width + (grow * 2), hole.height + (grow * 2)), With(Accent, 0.35f * (1 - p) * appear), 2, 12 + grow);
            Coach(whole, hole, appear);
        }
        else
        {
            // 欢迎那一步：整个窗口压暗；聚光圈从窗口中间飞到第一个目标
            Fill(whole, dim, Corner);
            Motion.Set("tour:x", whole.center.x);
            Motion.Set("tour:y", whole.center.y);
            Motion.Set("tour:w", 0);
            Motion.Set("tour:h", 0);
            Coach(whole, null, appear);
        }
    }

    /// <summary>说明卡：第几步、标题、一段话、进度点、跳过 / 下一步。放在照亮那块的右边，放不下就放左边。</summary>
    private void Coach(Rect whole, Rect? hole, float appear)
    {
        var step = _tourStep;
        var (title, text) = step switch
        {
            1 => (PageInfo(Page.Look).Title, Lang.T("音符样式、面板位置和大小都在这里调整，后方的演示会立即显示效果。")),
            2 => (PageInfo(Page.Keys).Title, Lang.T("选择使用的键数，设置各键数对应的按键和打开本窗口的快捷键。")),
            3 => (PageInfo(Page.Rules).Title, Lang.T("选择排法、惯用手和轮指方式，自动排键随之更新。")),
            4 => (PageInfo(Page.Custom).Title, Lang.T("在浏览器中为一首歌逐个改键；保存后进入这首歌时自动使用，也可以分享给他人。")),
            5 => (Lang.T("随时打开或关闭"), Lang.T("按 {0} 打开或关闭本窗口，关闭时自动保存。点击问号按钮可以再次查看本引导。",
                KeyBindings.Display(Main.Settings.SettingsHotkey))),
            _ => (Lang.T("欢迎使用全节奏提示"), Lang.T("用半分钟了解设置窗口：外观、排键和专属手序都可以按自己的习惯调整。")),
        };
        var welcome = step == 0;
        var w = welcome ? 420f : 344f;
        var textH = WrappedHeight(text, 14, w - 40);
        var head = welcome ? 56f : 28f;
        var h = 20 + head + 30 + textH + 20 + 36 + 18;

        Vector2 goal;
        if (hole is { } t)
        {
            var x = t.xMax + 18 + w > whole.width - 12 ? t.x - 18 - w : t.xMax + 18;
            goal = new Vector2(x, Mathf.Clamp(t.y - 10, 16, whole.height - h - 16));
        }
        else
        {
            goal = new Vector2(whole.center.x - (w / 2), whole.center.y - (h / 2) - 20);
        }

        var card = new Rect(Motion.Ease("tour:cx", goal.x, 0.35f), Motion.Ease("tour:cy", goal.y, 0.35f) + ((1 - appear) * 10), w, h);
        if (welcome)
        {
            Motion.Set("tour:cx", goal.x);
            Motion.Set("tour:cy", goal.y);
        }

        var saved = Alpha;
        Alpha *= appear;
        Shadow(card, 18, 0.6f, 12);
        Fill(card, new Color(0.137f, 0.149f, 0.173f), 14);
        WindowSkin.Stroke(card, Border2, 1, 14);
        var y = card.y + 20;
        if (welcome)
        {
            Logo(new Rect(card.x + 20, y, 40, 36), 1);
        }
        else
        {
            Icon(new Vector2(card.x + 28, y + 9), 14, "sparkle", Accent2);
            Label(new Rect(card.x + 42, y, w - 60, 18), Lang.T("第 {0} 步，共 {1} 步", step, TourSteps - 1), 12, Accent2, bold: true);
        }

        y += head;
        Label(new Rect(card.x + 20, y, w - 40, 26), title, 18, Text, Face.Display, true);
        y += 30;
        Label(new Rect(card.x + 20, y, w - 40, textH), text, 14, Text2, wrap: true, anchor: TextAnchor.UpperLeft);
        y += textH + 20;

        var dx = card.x + 20;
        for (var i = 0; i < TourSteps; i++)
        {
            var dw = Motion.Ease("tour:dot" + i, i == step ? 18 : 6, 0.25f);
            Fill(new Rect(dx, y + 15, dw, 6), i == step ? Accent : Border2, 3);
            dx += dw + 5;
        }

        var next = welcome ? Lang.T("开始") : step == TourSteps - 1 ? Lang.T("完成") : Lang.T("下一步");
        var skip = Lang.T("跳过");
        var nw = Ui.ButtonWidth(next, null);
        var sw = Ui.ButtonWidth(skip, null);
        var nextRect = new Rect(card.xMax - 20 - nw, y, nw, 36);
        if (_ui.Button("next", nextRect, next, null, ButtonKind.Primary))
        {
            NextTourStep();
        }

        if (step < TourSteps - 1 && _ui.Button("skip", new Rect(nextRect.x - 8 - sw, y, sw, 36), skip, null, ButtonKind.Ghost))
        {
            EndTour();
        }

        Alpha = saved;
    }
}
