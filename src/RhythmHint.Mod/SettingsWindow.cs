using System;
using System.Collections.Generic;
using System.Diagnostics;
using RhythmHint.Core;
using UnityEngine;
using static RhythmHint.WindowSkin;

namespace RhythmHint;

/// <summary>
/// 独立的设置窗口（默认按 F8 打开 / 关闭，UMM 页面里也有按钮）。2026-11-19 按玩家视角重做（用户：玩家都不知道能换皮肤、改排键）：
/// 左边导航分概览、外观、排键、专属手序、校准、通用、关于；风格和手序编辑器一样（ComfyUI 那种深色卡片），有图标、动效和首次引导。
/// 窗口放在提示面板以外区域的正中，面板换边时自动让开（玩家拖过就不再自动挪）。开着时演示照常播放，改什么都立刻看得到；关窗口时保存。
/// 各页内容见 SettingsWindow.Pages.cs、SettingsWindow.Fingering.cs，引导见 SettingsWindow.Tour.cs。
/// </summary>
internal sealed partial class SettingsWindow
{
    private const int WindowId = 0x52484D57; // "RHMW"
    private const float WindowWidth = 1080;
    private const float WindowHeight = 800;
    private const float HeaderHeight = 60;
    private const float NavWidth = 236;
    private const float Corner = 14;

    /// <summary>交流 QQ 群（关于页）。编辑器的关于页也写着它，tests/about.test.cjs 核对两边一致。</summary>
    private const string QqGroup = "1136407588";

    private static readonly Page[] NavPages = { Page.Home, Page.Look, Page.Rules, Page.Custom, Page.Calib, Page.General };

    private readonly Ui _ui = new();
    private readonly KeyPicker _keyPicker = new();
    private readonly HotkeyPicker _hotkeyPicker = new();
    private readonly Dictionary<Page, Rect> _navRects = new();
    private Rect _closeRect;
    private Rect _rect = new(0, 0, WindowWidth, WindowHeight);
    private bool _placed;
    private bool _userMoved;
    private Page _page = Page.Home;
    private bool _pageChanged = true;
    private string _confirmReset = string.Empty; // 「恢复本页默认值」点了一次、等确认的页
    private float _confirmUntil;
    private float _open; // 窗口出现的程度 0–1（开关动画）
    private string _slowLogged = string.Empty; // 已经记过「一帧太慢」的页，同一页只记一次
    private int _prewarmed; // 后台预排到第几步（7 页 + 5 步引导），排完以后不再做
    private long _prewarmTicks;

    /// <summary>导航里的各页。</summary>
    private enum Page
    {
        Home,
        Look,
        Rules,
        Custom,
        Calib,
        General,
        About,
    }

    public bool Visible { get; private set; }

    /// <summary>要不要画：开着，或者正在淡出。</summary>
    public bool NeedsDraw => Visible || _open > 0.001f;

    /// <summary>窗口在屏幕上的位置（像素，左上角为原点）；没显示时为 null。给 InputShield 挡住这块区域的鼠标用。</summary>
    public Rect? ScreenRect
    {
        get
        {
            if (!Visible || !_placed)
            {
                return null;
            }

            var scale = ScreenScale;
            return new Rect(_rect.x * scale, _rect.y * scale, _rect.width * scale, _rect.height * scale);
        }
    }

    public void Toggle()
    {
        if (Visible)
        {
            Close();
            return;
        }

        if (_open <= 0.001f)
        {
            Motion.Set("win:open", 0); // 从完全关着打开：从头播放出现动画（第一次打开也有）
        }

        WindowSkin.Rewarm();

        Visible = true;
        if (Main.Settings.GuideVersion < GuideRevision && _tourStep < 0)
        {
            StartTour(); // 第一次打开：自动播放引导
        }
    }

    public void Close()
    {
        Visible = false;
        if (_tourStep >= 0)
        {
            EndTour(); // 引导中途关掉也算看过，下次不再自动弹
        }

        Main.Settings.Save(Main.Mod);
    }

    public void Draw(Overlay overlay)
    {
        Ensure();
        _open = Motion.Ease("win:open", Visible ? 1 : 0, Visible ? 0.22f : 0.12f);
        if (!NeedsDraw)
        {
            return;
        }

        var scale = ScreenScale;
        Place(overlay, scale);
        var saved = GUI.matrix;

        // 出现时从 96.5% 放大、往上浮 10 像素、淡入；关掉时反过来
        var grow = Mathf.Lerp(0.965f, 1f, _open);
        var pivot = new Vector3(_rect.center.x, _rect.center.y, 0);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1))
            * Matrix4x4.Translate(pivot + new Vector3(0, (1 - _open) * 10, 0))
            * Matrix4x4.Scale(new Vector3(grow, grow, 1))
            * Matrix4x4.Translate(-pivot);
        Alpha = _open;
        Shadow(_rect, 26, 0.65f, 14);
        Alpha = 1;
        var moved = GUI.Window(WindowId, _rect, _ => Contents(overlay), GUIContent.none, GUIStyle.none);
        if (Visible && (!Mathf.Approximately(moved.x, _rect.x) || !Mathf.Approximately(moved.y, _rect.y)))
        {
            _userMoved = true; // 玩家拖过窗口：以后不再自动让开面板
            _rect.position = ClampToScreen(moved.position, scale);
        }

        GUI.matrix = saved;
    }

    /// <summary>
    /// 窗口还没打开时，每帧在后台把一页（再加引导的每一步）的排版走一遍：和真正打开时同一套代码、同样的缩放，
    /// 字形、字的宽高都提前算好，打开窗口、第一次点进每一页时就不卡（2026-11-19 用户：第一次点进选项卡还是会卡）。
    /// 只在布局事件里做（只量字、不画），不改玩家的设置；排完记一条日志。
    /// </summary>
    public void Prewarm(Overlay overlay)
    {
        var total = NavPages.Length + 1 + TourSteps;
        if (_prewarmed >= total || Visible || Event.current.type != EventType.Layout)
        {
            return;
        }

        Ensure();
        var started = Stopwatch.GetTimestamp();
        var scale = ScreenScale;
        var saved = (_page, _pageChanged, _tourStep, GUI.matrix);
        _rect.width = Mathf.Min(WindowWidth, (Screen.width / scale) - 16);
        _rect.height = Mathf.Min(WindowHeight, (Screen.height / scale) - 16);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
        GUI.BeginGroup(new Rect(0, 0, _rect.width, _rect.height));
        try
        {
            var step = _prewarmed;
            _page = step < NavPages.Length ? NavPages[step] : step == NavPages.Length ? Page.About : Page.Home;
            _tourStep = step > NavPages.Length ? step - NavPages.Length - 1 : -1;
            _pageChanged = true;
            DrawContents(overlay);
            _prewarmed++;
        }
        catch (Exception ex) when (ex is not ExitGUIException)
        {
            // 只是提速：出错就不再预排，打开窗口时照常现排
            _ui.Unwind();
            _prewarmed = total;
            Main.Mod.Logger.Warning($"设置窗口后台预排失败，打开时再排：{ex.Message}");
            Main.Mod.Logger.LogException(ex);
        }
        finally
        {
            GUI.EndGroup();
            (_page, _pageChanged, _tourStep, GUI.matrix) = saved;
            Alpha = 1;
            Motion.Forget("nav:y");
        }

        _prewarmTicks += Stopwatch.GetTimestamp() - started;
        if (_prewarmed == total)
        {
            Main.Mod.Logger.Log($"设置窗口：后台预排完 {NavPages.Length + 1} 页和 {TourSteps} 步引导，共用了 {_prewarmTicks * 1000 / Stopwatch.Frequency} 毫秒");
        }
    }

    /// <summary>
    /// 窗口放在提示面板以外区域的正中（2026-11-18 用户：原来贴右边压着面板）；面板换边、开关时平滑挪过去。玩家拖过以后只保证不出屏幕。
    /// </summary>
    private void Place(Overlay overlay, float scale)
    {
        var s = Main.Settings;
        var screenW = Screen.width / scale;
        var screenH = Screen.height / scale;
        _rect.width = Mathf.Min(WindowWidth, screenW - 16);
        _rect.height = Mathf.Min(WindowHeight, screenH - 16);

        // 面板显示时（关卡里，或者设置窗口开着播放演示）才让开
        var panels = s.ShowPreview || overlay.Current.Snapshot != null;
        var layout = new OverlayLayout(s, overlay.Frame.Keys);
        // 面板在中间时窗口放屏幕正中（两边都放不下，盖在面板上）
        var (left, right) = layout.SideMargins(panels && s.ShowLanes ? layout.PanelWidth / scale : 0);
        var top = panels ? layout.TopHeight / scale : 0;
        var free = new Rect(left, s.TaikoAtBottom ? 0 : top, screenW - left - right, screenH - top);
        var target = ClampToScreen(new Vector2(free.x + ((free.width - _rect.width) / 2), free.y + ((free.height - _rect.height) / 2)), scale);
        if (!_placed)
        {
            _placed = true;
            _rect.position = target;
            Motion.Set("win:x", target.x);
            Motion.Set("win:y", target.y);
        }
        else if (!_userMoved)
        {
            // 按住控件（拖滑块）时先不挪：拖「面板缩放」时面板变宽、窗口跟着居中，会把滑块从鼠标底下带走，越拖越远。松手后再平滑挪过去
            if (GUIUtility.hotControl == 0)
            {
                _rect.x = Motion.Ease("win:x", target.x, 0.45f);
                _rect.y = Motion.Ease("win:y", target.y, 0.45f);
            }
        }
        else
        {
            _rect.position = ClampToScreen(_rect.position, scale);
        }
    }

    /// <summary>窗口至少留标题栏的一截在屏幕里，拖不丢。</summary>
    private Vector2 ClampToScreen(Vector2 p, float scale)
    {
        var screenW = Screen.width / scale;
        var screenH = Screen.height / scale;
        return new Vector2(Mathf.Clamp(p.x, 160 - _rect.width, screenW - 160), Mathf.Clamp(p.y, 0, screenH - HeaderHeight));
    }

    private void Contents(Overlay overlay)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            DrawContents(overlay);
        }
        catch (Exception ex) when (ex is not ExitGUIException)
        {
            // 画到一半出错：收起还没关的裁剪组、关掉窗口，只记一次日志，不让它每帧刷错误拖慢游戏（重新打开会再试一次）
            _ui.Unwind();
            Alpha = 1;
            Visible = false;
            _open = 0;
            Motion.Set("win:open", 0);
            Main.Mod.Logger.Error($"设置窗口出错，已关闭（{_page} 页）：{ex.Message}");
            Main.Mod.Logger.LogException(ex);
        }

        // 一次事件超过 100 毫秒就记下来是哪一页、哪种事件（打开时字体、图标首次载入会慢一点；每页只记一次）
        var ms = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
        var key = _page + ":" + Event.current.type;
        if (ms > 100 && !_slowLogged.Contains("|" + key + "|"))
        {
            _slowLogged += "|" + key + "|";
            Main.Mod.Logger.Warning($"设置窗口：{_page} 页的 {Event.current.type} 事件用了 {ms:F0} 毫秒");
        }
    }

    private void DrawContents(Overlay overlay)
    {
        _ui.BeginFrame();
        var s = Main.Settings;
        Alpha = _open;
        var whole = new Rect(0, 0, _rect.width, _rect.height);
        _ui.Interactive = Visible && _tourStep < 0;
        _ui.Clip = whole;
        _ui.Scope = string.Empty;
        Fill(whole, Backdrop, Corner);
        Header(s, whole.width);
        Nav(whole.height);
        Content(s, overlay, new Rect(NavWidth, HeaderHeight, whole.width - NavWidth, whole.height - HeaderHeight));
        Stroke(whole, Border, 1, Corner);
        if (_tourStep >= 0)
        {
            Tour(whole);
        }

        if (Visible)
        {
            GUI.DragWindow(new Rect(0, 0, whole.width, HeaderHeight));
        }

        Alpha = 1;
    }

    // ---------- 标题栏 ----------

    private void Header(Settings s, float width)
    {
        Fill(new Rect(0, 0, width, HeaderHeight), Surface, new Vector4(Corner, Corner, 0, 0));
        Fill(new Rect(0, HeaderHeight - 1, width, 1), Border);
        Logo(new Rect(21, 19, 24, 22));
        var name = Lang.T("全节奏提示");
        var nameW = Measure(name, 18, Face.Display, true).x;
        Label(new Rect(56, 0, nameW + 4, HeaderHeight), name, 18, Text, Face.Display, true);
        var x = 56 + nameW + 12;
        Fill(new Rect(x, 22, 1, 16), Border2);
        x += 12;
        var sub = Lang.T("设置");
        var subW = Measure(sub, 14).x;
        Label(new Rect(x, 0, subW + 2, HeaderHeight), sub, 14, Text3);
        Ui.Chip(new Vector2(x + subW + 10, 18), Main.Mod.Info.Version, Face.Mono);

        // 右边：有新版本、重看引导、关闭（写着快捷键）
        var hotkey = KeyBindings.Display(s.SettingsHotkey);
        var capW = Mathf.Max(28, Measure(hotkey, 12, Face.Mono, true).x + 14);
        _closeRect = new Rect(width - 12 - (capW + 42), 13, capW + 42, 34);
        if (CloseButton(_closeRect, hotkey, capW))
        {
            Close();
        }

        var help = new Rect(_closeRect.x - 6 - 34, 13, 34, 34);
        if (_ui.Click(help))
        {
            StartTour();
        }

        var helpHover = _ui.Hover("help", help);
        Fill(help, With(Surface3, helpHover), 8);
        Icon(help.center, 19, "help", Mix(Text2, Text, helpHover));

        var text = AutoUpdater.State switch
        {
            UpdateState.Installed => Lang.T("重启后更新到 {0}", AutoUpdater.Version),
            UpdateState.Downloading => Lang.T("正在下载新版本 {0}", AutoUpdater.Version),
            _ => UpdateNotice.Newer is { } newer ? Lang.T("有新版本 {0}", newer) : null,
        };
        if (text != null)
        {
            var w = Measure(text, 13, Face.Body, true).x + 46;
            var pill = new Rect(help.x - 10 - w, 15, w, 30);
            if (_ui.Click(pill))
            {
                SelectPage(Page.About);
            }

            var hover = _ui.Hover("update", pill);
            var pulse = 0.5f + (0.5f * Mathf.Sin(Time.unscaledTime * 2.6f));
            Stroke(new Rect(pill.x - 3, pill.y - 3, pill.width + 6, pill.height + 6), With(Warn, 0.18f * pulse), 3, 18);
            Fill(pill, With(Warn, 0.14f + (0.08f * hover)), 15);
            Icon(new Vector2(pill.x + 18, pill.center.y), 16, "sparkle", Warn);
            Label(new Rect(pill.x + 32, pill.y, w - 36, pill.height), text, 13, Warn, bold: true);
        }
    }

    private bool CloseButton(Rect r, string hotkey, float capW)
    {
        var clicked = _ui.Click(r);
        var hover = _ui.Hover("close", r);
        Fill(r, With(Danger, 0.14f * hover), 8);
        var cap = new Rect(r.x + 8, r.y + 6, capW, 22);
        Fill(new Rect(cap.x, cap.y + 2, cap.width, cap.height - 2), new Color(0.09f, 0.1f, 0.11f), 5);
        Fill(new Rect(cap.x, cap.y, cap.width, cap.height - 2), Surface3, 5);
        Label(new Rect(cap.x, cap.y, cap.width, cap.height - 2), hotkey, 12, Text2, Face.Mono, true, TextAnchor.MiddleCenter);
        Icon(new Vector2(r.xMax - 17, r.center.y), 18, "close", Mix(Text2, new Color(1f, 0.55f, 0.55f), hover));
        return clicked;
    }

    // ---------- 导航 ----------

    private void Nav(float height)
    {
        Fill(new Rect(0, HeaderHeight, NavWidth, height - HeaderHeight), Surface, new Vector4(0, 0, 0, Corner));
        Fill(new Rect(NavWidth - 1, HeaderHeight, 1, height - HeaderHeight), Border);
        for (var i = 0; i < NavPages.Length; i++)
        {
            _navRects[NavPages[i]] = new Rect(12, HeaderHeight + 14 + (i * 58), NavWidth - 24, 56);
        }

        var footTop = height - 52;
        _navRects[Page.About] = new Rect(12, footTop - 66, NavWidth - 24, 56);

        // 选中条：弹簧滑到选中的那一项
        var target = _navRects[_page];
        var y = Motion.Spring("nav:y", target.y, 3.6f, 0.74f);
        Fill(new Rect(12, y, NavWidth - 24, 56), With(Accent, 0.13f), 10);
        Fill(new Rect(0, y + 14, 3, 28), Accent, new Vector4(0, 3, 3, 0));

        foreach (var page in NavPages)
        {
            NavItem(page);
        }

        Fill(new Rect(16, footTop - 78, NavWidth - 32, 1), Border);
        NavItem(Page.About);
        Icon(new Vector2(25, footTop + 13), 15, "check", Ok);
        Label(new Rect(38, footTop + 2, NavWidth - 50, 22), Ellipsize(Lang.T("修改即时生效"), 12, NavWidth - 52), 12, Text3);
        Label(new Rect(38, footTop + 20, NavWidth - 50, 22), Ellipsize(Lang.T("关闭窗口时自动保存"), 12, NavWidth - 52), 12, Text3);
    }

    private void NavItem(Page page)
    {
        var r = _navRects[page];
        if (_ui.Click(r))
        {
            SelectPage(page);
        }

        var (icon, title, sub) = PageInfo(page);
        var hover = _ui.Hover("nav" + page, r);
        var on = page == _page;
        if (!on)
        {
            Fill(r, With(Color.white, 0.035f * hover), 10);
        }

        Icon(new Vector2(r.x + 22, r.center.y), 20, icon, on ? Accent : Mix(Text3, Text2, hover));
        Label(new Rect(r.x + 44, r.y + 8, r.width - 52, 22), Ellipsize(title, 15, r.width - 52, Face.Body, true), 15, on ? Text : Mix(Text2, Text, hover), bold: true);
        Label(new Rect(r.x + 44, r.y + 30, r.width - 52, 18), Ellipsize(sub, 12, r.width - 52), 12, Text3);
    }

    /// <summary>每页的图标、名字和一句说明（导航和页标题用）。</summary>
    private static (string Icon, string Title, string Sub) PageInfo(Page page) => page switch
    {
        Page.Look => ("palette", Lang.T("外观"), Lang.T("音符、位置、键位、大小")),
        Page.Rules => ("hand", Lang.T("排键"), Lang.T("排法、惯用手、轮指")),
        Page.Custom => ("pen", Lang.T("专属手序"), Lang.T("为一首歌定制并分享")),
        Page.Calib => ("target", Lang.T("校准"), Lang.T("演示预览与画面偏移")),
        Page.General => ("gear", Lang.T("通用"), Lang.T("语言、快捷键、更新、运行状态")),
        Page.About => ("info", Lang.T("关于"), Lang.T("版本、交流群、仓库")),
        _ => ("home", Lang.T("概览"), Lang.T("快速上手与当前状态")),
    };

    private void SelectPage(Page page)
    {
        if (page == _page)
        {
            return;
        }

        _page = page;
        _pageChanged = true;
        _confirmReset = string.Empty;
        _keyPicker.Close();
        _hotkeyPicker.Close();
        Motion.Set("page:in", 0);
    }

    // ---------- 内容区 ----------

    private void Content(Settings s, Overlay overlay, Rect area)
    {
        // 换页：内容淡入、从下面浮上来 8 像素
        var appear = Motion.Ease("page:in", 1, 0.26f);
        var saved = Alpha;
        Alpha *= appear;
        _ui.Scope = _page + ":";
        _ui.BeginScroll(_page.ToString(), area, 34, 28 + ((1 - appear) * 8), _pageChanged);
        _pageChanged = false;
        switch (_page)
        {
            case Page.Look:
                LookPage(s, overlay);
                break;
            case Page.Rules:
                RulesPage(s, overlay);
                break;
            case Page.Custom:
                CustomPage(s, overlay);
                break;
            case Page.Calib:
                CalibPage(s, overlay);
                break;
            case Page.General:
                GeneralPage(s, overlay);
                break;
            case Page.About:
                AboutPage();
                break;
            default:
                HomePage(s, overlay);
                break;
        }

        _ui.EndScroll(30);
        _ui.Scope = string.Empty;
        Alpha = saved;
    }

    private void PageHeader(Page page, string desc)
    {
        var (icon, title, _) = PageInfo(page);
        _ui.PageHeader(icon, title, desc);
    }

    /// <summary>
    /// 每页底部的「恢复本页默认值」。点一次变成红色的确认按钮（底下一道 3 秒倒计时条），3 秒内再点才恢复，免得误点把调好的设置清掉。
    /// </summary>
    private void ResetFooter(Overlay overlay, string id, Action reset, string? note)
    {
        var confirming = _confirmReset == id && Time.unscaledTime < _confirmUntil;
        var label = confirming ? Lang.T("再次点击确认恢复") : Lang.T("恢复本页默认值");
        var icon = confirming ? "alert" : "reset";
        var bw = Ui.ButtonWidth(label, icon);
        var text = confirming ? Lang.T("3 秒内再次点击即恢复，无法撤销。") : note ?? string.Empty;
        var textW = _ui.Width - bw - 16;
        var th = WrappedHeight(text, 13, textW);
        var r = _ui.Take(Mathf.Max(38, th));
        Label(new Rect(r.x, r.y + Mathf.Max(0, (38 - th) / 2), textW, th), text, 13, confirming ? new Color(1f, 0.6f, 0.6f) : Text3,
            wrap: true, anchor: TextAnchor.UpperLeft);
        var button = new Rect(r.xMax - bw, r.y + 1, bw, 36);
        if (_ui.Button("reset", button, label, icon, confirming ? ButtonKind.Danger : ButtonKind.Ghost))
        {
            if (confirming)
            {
                reset();
                _confirmReset = string.Empty;
                overlay.Feedback.Show("reset:" + id, Lang.M("已恢复本页默认值。"), false, Time.unscaledTime);
            }
            else
            {
                _confirmReset = id;
                _confirmUntil = Time.unscaledTime + 3f;
            }
        }

        if (confirming)
        {
            var left = Mathf.Clamp01((_confirmUntil - Time.unscaledTime) / 3f);
            Fill(new Rect(button.x + 8, button.yMax - 4, (button.width - 16) * left, 2), new Color(1f, 0.48f, 0.48f), 1);
        }

        _ui.Gap(8);
        _ui.Feedback(overlay.Feedback, "reset:" + id, 0);
    }
}
