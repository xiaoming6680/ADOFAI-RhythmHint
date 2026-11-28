using System;
using System.Globalization;
using System.Text.RegularExpressions;
using RhythmHint.Core;
using UnityEngine;
using static RhythmHint.WindowSkin;

namespace RhythmHint;

/// <summary>设置窗口：概览、外观、校准、通用、关于这几页。排键和专属手序见 SettingsWindow.Fingering.cs。</summary>
internal sealed partial class SettingsWindow
{
    private string? _copied; // 刚复制到剪贴板的内容，对应的按钮显示「已复制」一会儿
    private float _copiedUntil;

    private static string Ms(float v) => Mathf.RoundToInt(v).ToString(CultureInfo.InvariantCulture) + " ms";

    private static string Times(float v) => "×" + v.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Percent(float v) => Mathf.RoundToInt(v * 100).ToString(CultureInfo.InvariantCulture) + "%";

    // ---------- 概览 ----------

    /// <summary>
    /// 概览：欢迎横幅、现在的状态、三张入口卡（外观 / 排键 / 专属手序，让玩家一眼知道能改什么）、四个显示开关。
    /// </summary>
    private void HomePage(Settings s, Overlay overlay)
    {
        Hero();
        SettingsNoticeCard();
        StatusCard(s, overlay);
        Tiles(s);

        _ui.BeginCard("quick", "sliders", Lang.T("快捷开关"));
        var row = _ui.Take(64);
        var items = new (string Icon, string Label, bool Value, string? Tag)[]
        {
            ("lanes", Lang.T("下落面板"), s.ShowLanes, null), ("drum", Lang.T("太鼓条"), s.ShowTaiko, null),
            ("gauge", Lang.T("BPM 与剩余时间"), s.ShowBpm, null), ("medal", Lang.T("估算难度"), s.ShowDifficulty, Lang.T("试验性")),
        };
        var w = (row.width - (Ui.Pad * 2) - 30) / 4;
        for (var i = 0; i < items.Length; i++)
        {
            var r = new Rect(row.x + Ui.Pad + (i * (w + 10)), row.y + 4, w, 48);
            if (QuickChip("q" + i, r, items[i].Icon, items[i].Label, items[i].Value, items[i].Tag))
            {
                switch (i)
                {
                    case 0: s.ShowLanes = !s.ShowLanes; break;
                    case 1: s.ShowTaiko = !s.ShowTaiko; break;
                    case 2: s.ShowBpm = !s.ShowBpm; break;
                    default: s.ShowDifficulty = !s.ShowDifficulty; break;
                }
            }
        }

        _ui.Gap(8);
        _ui.EndCard();
    }

    private void Hero()
    {
        var title = Lang.T("欢迎使用全节奏提示");
        var text = Lang.T("可按个人习惯调整下落面板与太鼓条的样式、位置及排键方式；修改即时生效，可在后方演示中查看效果。");
        var textW = _ui.Width - 200;
        var th = WrappedHeight(text, 14, textW);
        var r = _ui.Take(Mathf.Max(118, 70 + th));
        Fill(r, new Color(0.115f, 0.122f, 0.145f), 14);
        Glow(new Rect(r.xMax - 340, r.y - 120, 420, 260), With(Accent, 0.16f));
        Stroke(r, Border, 1, 14);
        Label(new Rect(r.x + 28, r.y + 24, textW, 32), title, 24, Text, Face.Display, true);
        Label(new Rect(r.x + 28, r.y + 62, textW, th), text, 14, Text2, wrap: true, anchor: TextAnchor.UpperLeft);
        Logo(new Rect(r.xMax - 108, r.y + 26, 72, 64), 1);
        _ui.Gap(Ui.CardGap);
    }

    /// <summary>启动时读设置文件出了问题（见 Main.Load）：说清哪些改回了默认值、原文件另存在哪，玩家点「知道了」后不再显示。</summary>
    private void SettingsNoticeCard()
    {
        var notice = Main.SettingsNotice;
        if (notice.Source.Length == 0)
        {
            return;
        }

        var label = Lang.T("知道了");
        _ui.BeginCard("settingsNotice", fill: new Color(0.17f, 0.12f, 0.115f), border: With(Danger, 0.35f));
        var r = _ui.Row("alert", Lang.T("设置文件读取出错"), Lang.T(notice), Ui.ButtonWidth(label, "check"), 36);
        if (_ui.Button("settingsNoticeOk", r, label, "check"))
        {
            Main.SettingsNotice = Message.Empty;
        }

        _ui.EndCard();
    }

    /// <summary>现在的状态：关卡里写曲名和正在用的手序，关卡外写演示；右边是排法和画面偏移。</summary>
    private void StatusCard(Settings s, Overlay overlay)
    {
        var snapshot = overlay.Current.Snapshot;
        var title = snapshot == null ? Lang.T("未进入关卡") : SongTitle(snapshot.Info);
        var sub = snapshot != null ? Lang.T(snapshot.Fingering.Status)
            : s.ShowPreview ? Lang.T("正在播放演示，修改设置后可直接查看效果。")
            : Lang.T("关卡外不播放演示，可在校准页开启。");
        var style = s.ActiveStyle switch
        {
            FingeringStyle.SteadyHands => Lang.T("按拍分手"),
            FingeringStyle.ReviewedTime => Lang.T("右手稳拍（强弱拍）"),
            FingeringStyle.ReviewPreferenceV1 => Lang.T("交替优先"),
            _ => Lang.T("标准"),
        };
        var offset = (s.VisualOffsetMs > 0 ? "+" : string.Empty) + Ms(s.VisualOffsetMs);
        var chips = Ui.ChipWidth(style) + Ui.ChipWidth(offset, Face.Mono) + 8;

        _ui.BeginCard("status");
        var textX = _ui.X + Ui.Pad + 54;
        var textW = _ui.Width - Ui.Pad - 54 - Ui.Pad - chips - 12;
        var subH = WrappedHeight(sub, 13, textW);
        var row = _ui.Take(Mathf.Max(68, subH + 46));
        var icon = new Rect(row.x + Ui.Pad, row.center.y - 20, 40, 40);
        var live = snapshot != null;
        Fill(icon, live ? With(Ok, 0.14f) : Surface3, 10);
        Icon(icon.center, 20, live ? "play" : "monitor", live ? Ok : Text2);
        if (live)
        {
            // 关卡里：图标外面一圈慢慢扩散的绿光
            var p = Mathf.Repeat(Time.unscaledTime * 0.7f, 1f);
            Stroke(new Rect(icon.x - (6 * p), icon.y - (6 * p), icon.width + (12 * p), icon.height + (12 * p)), With(Ok, 0.35f * (1 - p)), 2, 10 + (6 * p));
        }

        var top = row.center.y - ((22 + subH) / 2);
        Label(new Rect(textX, top, textW, 22), Ellipsize(title, 15, textW, Face.Body, true), 15, Text, bold: true);
        Label(new Rect(textX, top + 23, textW, subH), sub, 13, Text3, wrap: true, anchor: TextAnchor.UpperLeft);
        var x = row.xMax - Ui.Pad - chips;
        x += Ui.Chip(new Vector2(x, row.center.y - 12), style) + 8;
        Ui.Chip(new Vector2(x, row.center.y - 12), offset, Face.Mono);
        _ui.EndCard();
    }

    /// <summary>曲名（关卡里的富文本标签去掉）和作者。</summary>
    private static string SongTitle(LevelInfo info)
    {
        static string Plain(string text) => Regex.Replace(text, "<[^>]*>", string.Empty).Trim();
        var song = Plain(info.Song);
        var artist = Plain(info.Artist);
        return artist.Length > 0 ? song + " — " + artist : song;
    }

    /// <summary>三张入口卡：小插图、名字、一句话、「前往…」，悬停时浮起。</summary>
    private void Tiles(Settings s)
    {
        var tiles = new (Page Page, string Title, string Desc)[]
        {
            (Page.Look, Lang.T("更换外观"), Lang.T("音符样式、面板位置、大小和速度。")),
            (Page.Rules, Lang.T("调整排键"), Lang.T("选择排法、惯用手和轮指方式。")),
            (Page.Custom, Lang.T("制作专属手序"), Lang.T("为一首歌逐个改键，并分享给他人。")),
        };
        var w = (_ui.Width - 28) / 3;
        var descH = 0f;
        foreach (var t in tiles)
        {
            descH = Mathf.Max(descH, WrappedHeight(t.Desc, 13, w - 32));
        }

        var r = _ui.Take(118 + 18 + 24 + descH + 44);
        for (var i = 0; i < tiles.Length; i++)
        {
            var (page, title, desc) = tiles[i];
            var tile = new Rect(r.x + (i * (w + 14)), r.y, w, r.height);
            if (_ui.Click(tile))
            {
                SelectPage(page);
            }

            var hover = _ui.Hover("tile" + i, tile);
            var lifted = new Rect(tile.x, tile.y - (3 * hover), tile.width, tile.height);
            if (hover > 0.01f)
            {
                Shadow(lifted, 6, 0.4f * hover, 12);
            }

            Fill(lifted, Mix(Surface, new Color(0.122f, 0.129f, 0.149f), hover), 13);
            Stroke(lifted, Mix(Border, Border2, hover), 1, 13);
            var art = new Rect(lifted.x, lifted.y, lifted.width, 118);
            Fill(art, new Color(0.118f, 0.126f, 0.145f), new Vector4(13, 13, 0, 0));
            Fill(new Rect(art.x, art.yMax, art.width, 1), Border);
            switch (page)
            {
                case Page.Look:
                    SettingsArt.LanePreview(new Rect(art.center.x - 106, art.y + 14, 100, 90), false, SettingsArt.KeyLabels(s));
                    SettingsArt.LanePreview(new Rect(art.center.x + 6, art.y + 14, 100, 90), true, SettingsArt.KeyLabels(s));
                    break;
                case Page.Rules:
                    SettingsArt.Keys(art, KeyBindings.Split(s.KeyLabels));
                    break;
                default:
                    SettingsArt.EditorArt(new Rect(art.center.x - 100, art.y + 12, 200, 94), SettingsArt.KeyLabels(s));
                    break;
            }

            var y = art.yMax + 16;
            Label(new Rect(lifted.x + 16, y, w - 32, 22), title, 15, Text, bold: true);
            Label(new Rect(lifted.x + 16, y + 24, w - 32, descH), desc, 13, Text3, wrap: true, anchor: TextAnchor.UpperLeft);
            var go = Lang.T("前往{0}", PageInfo(page).Title);
            var goW = Measure(go, 13, Face.Body, true).x;
            Label(new Rect(lifted.x + 16, lifted.yMax - 34, goW + 4, 20), go, 13, Accent2, bold: true);
            Icon(new Vector2(lifted.x + 16 + goW + 12 + (3 * hover), lifted.yMax - 24), 14, "right", Accent2);
        }

        _ui.Gap(Ui.CardGap);
    }

    /// <summary>快捷开关：图标、名字和一个亮着 / 灭着的小圆点。</summary>
    private bool QuickChip(string id, Rect r, string icon, string label, bool on, string? tag)
    {
        var clicked = _ui.Click(r);
        var hover = _ui.Hover(id, r);
        var t = Motion.Ease("home:" + id, on ? 1 : 0, 0.18f);
        Fill(r, Mix(Surface2, Surface3, hover), 10);
        Stroke(r, Mix(Border, With(Accent, 0.45f), t), 1, 10);
        Icon(new Vector2(r.x + 22, r.center.y), 18, icon, Mix(Text3, Accent, t));
        // 标签（试验性）放得下才画：名字和标签一起要能放进圆点左边
        var room = r.width - 66;
        var labelW = Measure(label, 13).x;
        var tagW = tag == null ? 0 : Measure(tag, 11, Face.Body, true).x + 14;
        Label(new Rect(r.x + 40, r.y, room, r.height), Ellipsize(label, 13, room), 13, Text);
        if (tag != null && labelW + 8 + tagW <= room)
        {
            Ui.Tag(new Vector2(r.x + 40 + labelW + 8, r.center.y - 10), tag, Warn);
        }
        var dot = new Vector2(r.xMax - 18, r.center.y);
        Circle(dot, 8, With(Accent, 0.25f * t));
        Circle(dot, 4, Mix(Border2, Accent, t));
        return clicked;
    }

    // ---------- 外观 ----------

    private void LookPage(Settings s, Overlay overlay)
    {
        PageHeader(Page.Look, Lang.T("修改后，后方的演示会立即显示效果。"));

        _ui.BeginCard("notes", "lanes", Lang.T("音符样式"));
        var bar = (Title: Lang.T("横条"), Desc: Lang.T("音符为横条，按键框为方形。"));
        var round = (Title: Lang.T("圆形"), Desc: Lang.T("音符与太鼓条一致为圆球，按键框为判定圈。"));
        var w = (_ui.Width - (Ui.Pad * 2) - 12) / 2;
        var h = Mathf.Max(Ui.OptionHeight(w, bar.Desc, 160), Ui.OptionHeight(w, round.Desc, 160));
        var row = _ui.Take(h + 8);
        var labels = SettingsArt.KeyLabels(s);
        if (_ui.Option("bar", new Rect(row.x + Ui.Pad, row.y + 4, w, h), !s.RoundNotes, bar.Title, null, Accent, bar.Desc, 160,
            r => SettingsArt.LanePreview(r, false, labels)))
        {
            s.RoundNotes = false;
        }

        if (_ui.Option("round", new Rect(row.x + Ui.Pad + w + 12, row.y + 4, w, h), s.RoundNotes, round.Title, null, Accent, round.Desc, 160,
            r => SettingsArt.LanePreview(r, true, labels)))
        {
            s.RoundNotes = true;
        }

        _ui.Gap(12);
        _ui.EndCard();

        // 位置：左边一张屏幕示意图（跟着选择动），右边两个选择
        _ui.BeginCard("layout", "layout", Lang.T("位置"));
        var lanes = new[] { Lang.T("左边"), Lang.T("中间"), Lang.T("右边") };
        var taiko = (Lang.T("上面"), Lang.T("下面"));
        var hint = Lang.T("BPM 与剩余时间显示在太鼓条中下落面板所在的一侧（面板居中时显示在左侧），游戏画面缩放至其余区域；面板居中时覆盖在游戏画面上方。");
        var area = _ui.Take(214);
        var screen = new Rect(area.x + Ui.Pad, area.y + 6, 340, 191);
        SettingsArt.ScreenLayout(screen, s);
        var cx = screen.xMax + 24;
        var cw = area.xMax - Ui.Pad - cx;
        // 左边 / 中间 / 右边（2026-11-22 用户加中间）：中间存在 LanesInCenter，1.3.x 不认它、照 LanesOnRight 放
        var side = LayoutChoice("lanesSide", new Rect(cx, area.y + 8, cw, 36), Lang.T("下落面板"), s.LanesInCenter ? 1 : s.LanesOnRight ? 2 : 0, lanes);
        s.LanesInCenter = side == 1;
        s.LanesOnRight = side == 2;
        s.TaikoAtBottom = LayoutChoice("taikoSide", new Rect(cx, area.y + 54, cw, 36), Lang.T("太鼓条"), s.TaikoAtBottom ? 1 : 0, new[] { taiko.Item1, taiko.Item2 }) == 1;
        Label(new Rect(cx, area.y + 104, cw, 72), hint, 13, Text3, wrap: true, anchor: TextAnchor.UpperLeft);
        s.ShrinkGameView = _ui.Switch("shrink", "monitor", Lang.T("自适应游戏画幅"), Lang.T("面板显示时，游戏画面和界面文字缩到面板以外的区域。"), s.ShrinkGameView);
        _ui.EndCard();

        // 显示内容拆成三张抽屉卡片（2026-11-26 用户）：卡片第一行是这一项的开关，开着时下面拉开一格放它的子项，关掉时收起
        // （2026-11-23 起子项跟着父项收起：判定提示只画在下落面板上、多押数字只画在太鼓条上、估算难度和实际 BPM 画在 BPM 框里；设置值不变）
        s.ShowLanes = Drawer("lanes", "lanes", Lang.T("下落面板"), Lang.T("在屏幕左侧、中间或右侧显示按轨道下落的音符。"), s.ShowLanes,
            () => JudgmentChoice(s));
        s.ShowTaiko = Drawer("taiko", "drum", Lang.T("太鼓条"), Lang.T("在屏幕上方或下方显示横向滚动的音符。"), s.ShowTaiko,
            () => s.ShowChordCount = _ui.Switch("chordCount", "chord", Lang.T("多押数字"), Lang.T("太鼓条上的三押、四押音符中间显示 3、4。"), s.ShowChordCount));
        s.ShowBpm = Drawer("bpm", "gauge", Lang.T("BPM 与剩余时间"), Lang.T("在太鼓条一端显示当前 BPM 与本关剩余时间。"), s.ShowBpm, () =>
        {
            // 估算难度是试验性功能（2026-11-19 用户：试验阶段的功能统一标「试验性」），名字后面标出来
            s.ShowDifficulty = _ui.Switch("difficulty", "medal", Lang.T("估算难度"),
                Lang.T("在 BPM 框左侧显示按本关砖块估算的 TUF 难度档位，仅供参考，并非 TUF 官方评级。"), s.ShowDifficulty, Lang.T("试验性"));
            s.ShowRealBpm = _ui.Switch("realBpm", "gauge", Lang.T("实际 BPM"),
                Lang.T("在 BPM 数值下方显示实际按键速度，即 60 除以相邻两次按键的间隔秒数；多押按一次计，停顿超过 2 秒时不显示。"), s.ShowRealBpm);
        });

        // 按键框的键位 2026-11-22 搬到按键页（和用几键、快捷键放一起），这里只管长什么样
        _ui.BeginCard("size", "scale", Lang.T("尺寸与速度"));
        s.UiScale = _ui.Slider("scale", "scale", Lang.T("面板缩放"), null, s.UiScale, 0.5f, 2f, 0.05f, Times);
        s.LaneSpeed = _ui.Slider("fall", "fall", Lang.T("下落速度"), null, s.LaneSpeed, 0.3f, 3f, 0.05f, Times);
        s.TaikoSpeed = _ui.Slider("scroll", "scroll", Lang.T("太鼓条滚动速度"), null, s.TaikoSpeed, 0.3f, 3f, 0.05f, Times);
        s.PanelOpacity = _ui.Slider("opacity", "opacity", Lang.T("面板底色不透明度"), null, s.PanelOpacity, 0f, 1f, 0.05f, Percent);
        s.NoteOpacity = _ui.Slider("noteOpacity", "opacity", Lang.T("音符不透明度"), Lang.T("作用于下落面板和太鼓条上的音符，不影响判定线与按键框。"),
            s.NoteOpacity, 0.1f, 1f, 0.05f, Percent);
        _ui.EndCard();

        ResetFooter(overlay, "look", s.ResetDisplay, null);
    }

    /// <summary>
    /// 抽屉卡片：第一行是开关（图标、名字、说明），开着时下面拉开一格（缩进、深色底）放子项，关掉时平滑收起。返回开关的新值。
    /// </summary>
    private bool Drawer(string id, string icon, string title, string desc, bool on, Action items)
    {
        _ui.BeginCard(id + "Card");
        on = _ui.Switch(id, icon, title, desc, on);
        if (_ui.BeginFold(id + "Items", on))
        {
            var (x, width) = (_ui.X, _ui.Width);
            _ui.X += Ui.Pad;
            _ui.Width -= Ui.Pad * 2;
            _ui.BeginCard(id + "Drawer", fill: Well);
            items();
            _ui.EndCard();
            _ui.X = x;
            _ui.Width = width;
        }

        _ui.EndFold();
        _ui.EndCard();
        return on;
    }

    /// <summary>
    /// 判定提示（2026-11-23 用户）：判定线上方的小字，文字和颜色仿照游戏默认的（2026-11-28 用户）；完美的默认不显示。
    /// 同一天试过轨道染色（读谱时不用低头），用户看了让去掉。开着外观页时后面的演示出示例判定。
    /// 旧版游戏缺判定的接口时（<see cref="GameJudgments.Available"/>）用一行说明代替开关，玩家的设置不改。
    /// </summary>
    private void JudgmentChoice(Settings s)
    {
        if (!GameJudgments.Available)
        {
            _ui.Gap(8); // 抽屉上下各留 4，说明上下再各空 8，字在中间
            _ui.Hint(Lang.T("当前游戏版本不支持判定提示，游戏更新至最新版本后方可使用。"), 8);
            return;
        }

        s.ShowJudgments = _ui.Switch("judgments", "target", Lang.T("判定提示"),
            Lang.T("在下落面板判定线上方显示判定文字，文字与颜色采用游戏的默认样式。"), s.ShowJudgments);
        if (_ui.BeginFold("perfectJudgments", s.ShowJudgments))
        {
            s.ShowPerfectJudgments = _ui.Switch("perfectJudgments", "sparkle", Lang.T("显示完美判定"), Lang.T("关闭时仅显示非完美判定。"), s.ShowPerfectJudgments);
        }

        _ui.EndFold();
    }

    /// <summary>位置那两行：名字在左、分段选择在右，返回选中第几个。</summary>
    private int LayoutChoice(string id, Rect r, string label, int value, string[] labels)
    {
        var w = Ui.SegmentedWidth(labels);
        Label(new Rect(r.x, r.y, r.width - w - 8, r.height), Ellipsize(label, 15, r.width - w - 8), 15, Text);
        return _ui.Segmented(id, new Rect(r.xMax - w, r.y, w, r.height), labels, value);
    }

    // ---------- 校准 ----------

    private void CalibPage(Settings s, Overlay overlay)
    {
        PageHeader(Page.Calib, Lang.T("对照演示的节拍调整画面偏移，仅影响显示，不影响判定。"));

        _ui.BeginCard("offset", "clock", Lang.T("画面偏移"));
        var value = (s.VisualOffsetMs > 0 ? "+" : string.Empty) + s.VisualOffsetMs.ToString(CultureInfo.InvariantCulture);
        var big = _ui.Take(66);
        var vw = Measure(value, 52, Face.Display, true).x;
        var uw = Measure("ms", 18).x;
        var vx = big.center.x - ((vw + 6 + uw) / 2);
        Label(new Rect(vx, big.y, vw + 4, 66), value, 52, Text, Face.Display, true);
        Label(new Rect(vx + vw + 6, big.y + 22, uw + 2, 30), "ms", 18, Text3);

        var viz = _ui.Take(58);
        SettingsArt.Offset(new Rect(viz.x + Ui.Pad, viz.y + 6, viz.width - (Ui.Pad * 2), 46), s.VisualOffsetMs, Lang.T("提前显示"), Lang.T("推后显示"));

        var nudges = new[] { "−10", "−1", Lang.T("归零"), "+1", "+10" };
        var widths = Array.ConvertAll(nudges, n => Mathf.Max(60, Ui.ButtonWidth(n, n == nudges[2] ? "reset" : null)));
        var total = 0f;
        foreach (var bw in widths)
        {
            total += bw + 8;
        }

        var nr = _ui.Take(52);
        var x = nr.center.x - ((total - 8) / 2);
        for (var i = 0; i < nudges.Length; i++)
        {
            if (_ui.Button("nudge" + i, new Rect(x, nr.y + 8, widths[i], 36), nudges[i], i == 2 ? "reset" : null))
            {
                s.VisualOffsetMs = i switch
                {
                    0 => s.VisualOffsetMs - 10,
                    1 => s.VisualOffsetMs - 1,
                    3 => s.VisualOffsetMs + 1,
                    4 => s.VisualOffsetMs + 10,
                    _ => 0,
                };
            }

            x += widths[i] + 8;
        }

        var sr = _ui.Take(46);
        s.VisualOffsetMs = Mathf.RoundToInt(_ui.Slider("offset", new Rect(sr.center.x - 260, sr.y + 8, 520, 30), s.VisualOffsetMs, -200, 200, 1,
            v => Mathf.RoundToInt(v).ToString(CultureInfo.InvariantCulture) + " ms"));
        s.VisualOffsetMs = Mathf.Clamp(s.VisualOffsetMs, -200, 200);
        _ui.Hint(Lang.T("对照演示的节拍：音符偏晚时调高，偏早时调低。仅影响显示。"));
        _ui.EndCard();

        _ui.BeginCard("preview", "play", Lang.T("演示预览"));
        s.ShowPreview = _ui.Switch("preview", "play", Lang.T("关卡外播放演示"), null, s.ShowPreview);
        s.PreviewSound = _ui.Switch("sound", "sound", Lang.T("无音乐时播放提示音"), null, s.PreviewSound);
        s.PreviewVolume = _ui.Slider("volume", "volume", Lang.T("提示音音量"), null, s.PreviewVolume, 0f, 1f, 0.05f,
            v => v.ToString("0.00", CultureInfo.InvariantCulture));
        if (s.AutoKeyCount)
        {
            // 自动选键数时演示按几键播另外选（演示谱估出来是 P 档，按难度只会是 4K）；手动选了几键，演示就按几键播
            var preview = Array.IndexOf(KeyLayout.Modes, DemoPlayer.PreviewKeys);
            DemoPlayer.PreviewKeys = KeyLayout.Modes[_ui.Choice("previewKeys", "keyboard", Lang.T("演示键数"), null, Mathf.Max(0, preview), "4K", "6K", "8K")];
        }

        _ui.EndCard();

        ResetFooter(overlay, "calib", s.ResetPreview, null);
    }

    // ---------- 通用 ----------

    private void GeneralPage(Settings s, Overlay overlay)
    {
        PageHeader(Page.General, Lang.T("界面语言、自动更新和 Mod 的运行状态。"));

        // 有新版本、正在下载、装好了：放在最上面（标题栏的新版本提示点进来就能看到），和下面的自动更新开关同一页
        UpdateCard(Main.Mod.Info.HomePage);

        // 语言名用它自己的文字写（不翻译），「跟随游戏」翻译；三个一行
        _ui.BeginCard("language", "globe", Lang.T("界面语言"));
        var names = new string[Languages.All.Count + 1];
        var codes = new string[names.Length];
        names[0] = Lang.T("跟随游戏");
        codes[0] = string.Empty;
        for (var i = 0; i < Languages.All.Count; i++)
        {
            names[i + 1] = Languages.All[i].Name;
            codes[i + 1] = Languages.All[i].Code;
        }

        var current = Math.Max(0, Array.IndexOf(codes, s.Language)); // 设置文件里写了不认识的值：当作跟随游戏
        var w = (_ui.Width - (Ui.Pad * 2) - 16) / 3;
        var rows = (names.Length + 2) / 3;
        var grid = _ui.Take((rows * 52) + 4);
        for (var i = 0; i < names.Length; i++)
        {
            var r = new Rect(grid.x + Ui.Pad + ((i % 3) * (w + 8)), grid.y + 4 + ((i / 3) * 52), w, 44);
            if (LanguageChip("lang" + i, r, names[i], i == 0, i == current))
            {
                s.Language = codes[i];
            }
        }

        _ui.Gap(10);
        _ui.Hint(Lang.T("跟随游戏时使用游戏的语言；没有对应翻译的语言显示英文。"));
        _ui.EndCard();

        _ui.BeginCard("autoUpdate", "download", Lang.T("更新"));
        s.AutoUpdate = _ui.Switch("autoUpdate", "refresh", Lang.T("自动更新"), Lang.T("启动游戏时检查新版本并在后台下载安装，下次启动游戏时生效。"), s.AutoUpdate);
        _ui.EndCard();

        _ui.BeginCard("state", "log", Lang.T("运行状态"));
        var status = overlay.Status;
        var sw = _ui.Width - (Ui.Pad * 2) - 28;
        var sh = WrappedHeight(status, 13, sw, Face.Mono);
        var box = _ui.Take(sh + 26);
        var well = new Rect(box.x + Ui.Pad, box.y, box.width - (Ui.Pad * 2), sh + 24);
        Fill(well, Well, 9);
        Stroke(well, Border, 1, 9);
        Label(new Rect(well.x + 14, well.y + 12, sw, sh), status, 13, Text2, Face.Mono, wrap: true, anchor: TextAnchor.UpperLeft);
        _ui.Gap(14);
        _ui.EndCard();

        ResetFooter(overlay, "general", s.ResetGeneral, Lang.T("界面语言保持不变。"));
    }

    private bool LanguageChip(string id, Rect r, string name, bool follow, bool on)
    {
        var clicked = _ui.Click(r);
        var hover = _ui.Hover(id, r);
        var t = Motion.Ease("general:" + id, on ? 1 : 0, 0.18f);
        Fill(r, Mix(Mix(Surface2, Surface3, hover), new Color(0.145f, 0.133f, 0.122f), t), 9);
        Stroke(r, Mix(Border, Accent, t), 1 + (0.5f * t), 9);
        var x = r.x + 14;
        if (follow)
        {
            Icon(new Vector2(x + 8, r.center.y), 16, "globe", Text3);
            x += 26;
        }

        Label(new Rect(x, r.y, r.xMax - x - 34, r.height), Ellipsize(name, 14, r.xMax - x - 34), 14, Text);
        var pop = Motion.Spring("general:" + id + ":c", on ? 1 : 0, 4.4f, 0.5f);
        if (pop > 0.01f)
        {
            Icon(new Vector2(r.xMax - 20, r.center.y), 16 * pop, "check", Accent);
        }

        return clicked;
    }

    // ---------- 关于 ----------

    /// <summary>
    /// 通用页顶部的更新卡片（2026-11-22 从关于页搬过来，和自动更新开关放一起）：自动更新装好了（重启生效）、正在下载、失败了（重试 / 下载页），
    /// 或者只知道有新版本（自动更新关着、测试版）时照旧给 UMM 一键更新和下载页。
    /// </summary>
    private void UpdateCard(string? home)
    {
        var state = AutoUpdater.State;
        if (state == UpdateState.Installed)
        {
            _ui.BeginCard("update", fill: new Color(0.11f, 0.16f, 0.13f), border: With(Ok, 0.35f));
            _ui.Row(null, Lang.T("已安装新版本 {0}", AutoUpdater.Version), Lang.T("重启游戏后生效。旧版文件备份于 Mod 文件夹的 .update 目录。"), 0, 36);
            _ui.EndCard();
            return;
        }

        if (state == UpdateState.Downloading)
        {
            _ui.BeginCard("update", fill: new Color(0.17f, 0.155f, 0.11f), border: With(Warn, 0.35f));
            _ui.Row(null, Lang.T("正在下载新版本 {0}", AutoUpdater.Version), Lang.T("下载并安装完成后，重启游戏即可生效。"), 0, 36);
            _ui.EndCard();
            return;
        }

        if (UpdateNotice.Newer is not { } newer)
        {
            return;
        }

        _ui.BeginCard("update", fill: new Color(0.17f, 0.155f, 0.11f), border: With(Warn, 0.35f));
        var failed = state == UpdateState.Failed;
        var text = failed
            ? Lang.T("自动更新失败（{0}）。可重试，或从交流 QQ 群的群文件、夸克网盘下载。", Lang.T(AutoUpdater.Error))
            : Lang.T("可在 UMM 安装器的 Mods 页右键本 Mod，选择 Update 一键更新；也可从交流 QQ 群的群文件下载。");
        var retry = Lang.T("重试");
        var label = Lang.T("打开下载页");
        var rw = failed ? Ui.ButtonWidth(retry, "refresh") : 0;
        var dw = string.IsNullOrEmpty(home) ? 0 : Ui.ButtonWidth(label, "download");
        var gap = rw > 0 && dw > 0 ? 8 : 0;
        var r = _ui.Row(null, Lang.T("有新版本 {0}", newer), text, rw + gap + dw, 36);
        if (rw > 0 && _ui.Button("retry", new Rect(r.x, r.y, rw, r.height), retry, "refresh", ButtonKind.Primary) && Main.Overlay != null)
        {
            AutoUpdater.Start(Main.Overlay, now: true);
        }

        if (dw > 0 && _ui.Button("download", new Rect(r.xMax - dw, r.y, dw, r.height), label, "download", failed ? ButtonKind.Normal : ButtonKind.Primary))
        {
            Browser.Open(home + "/releases/latest");
        }

        _ui.EndCard();
    }

    private void AboutPage()
    {
        PageHeader(Page.About, Lang.T("仅提供视觉提示，不修改判定。"));
        var info = Main.Mod.Info;

        _ui.BeginCard("identity");
        var id = _ui.Take(92);
        Logo(new Rect(id.x + 24, id.y + 22, 50, 46), 0.6f);
        var name = Lang.T("全节奏提示 RhythmHint");
        Label(new Rect(id.x + 96, id.y + 18, id.width - 120, 30), name, 21, Text, Face.Display, true);
        var line = Lang.T("版本 {0}。仅提供视觉提示，不修改判定；运行日志见 UMM 的 Log.txt。", info.Version);
        Label(new Rect(id.x + 96, id.y + 50, id.width - 120, 40), line, 13, Text3, wrap: true, anchor: TextAnchor.UpperLeft);
        _ui.EndCard();

        // 作者和仓库地址取自 Info.json（RhythmHint.Mod.csproj 生成），群号写在这里
        _ui.BeginCard("links");
        LinkRow("author", "user", Purple, Lang.T("作者"), info.Author, Array.Empty<(string, string)>());
        if (LinkRow("qq", "chat", Accent, Lang.T("交流 QQ 群"), QqGroup, (CopyLabel(QqGroup, Lang.T("复制")), CopyIcon(QqGroup))) == 0)
        {
            Copy(QqGroup);
        }

        if (!string.IsNullOrEmpty(info.HomePage))
        {
            switch (LinkRow("repo", "repo", Blue, Lang.T("GitHub 仓库"), info.HomePage,
                (Lang.T("在浏览器中打开"), "external"), (CopyLabel(info.HomePage, Lang.T("复制链接")), CopyIcon(info.HomePage))))
            {
                case 0: Browser.Open(info.HomePage); break;
                case 1: Copy(info.HomePage); break;
            }
        }

        _ui.EndCard();
    }

    /// <summary>关于页的一行：彩色小图标块、名字、内容（等宽字），右边几个按钮。返回被点的按钮，没点为 -1。</summary>
    private int LinkRow(string id, string icon, Color color, string title, string value, params (string Label, string Icon)[] buttons)
    {
        var widths = Array.ConvertAll(buttons, b => Ui.ButtonWidth(b.Label, b.Icon));
        var total = 0f;
        foreach (var bw in widths)
        {
            total += bw + 8;
        }

        var r = _ui.Row(null, string.Empty, null, Mathf.Max(0, total - 8), 36);
        var row = _ui.LastRow;
        var tile = new Rect(row.x + Ui.Pad, row.center.y - 17, 34, 34);
        Fill(tile, With(color, 0.15f), 9);
        Icon(tile.center, 17, icon, Mix(color, Color.white, 0.3f));
        var textW = r.x - tile.xMax - 30;
        Label(new Rect(tile.xMax + 14, row.center.y - 21, textW, 22), title, 15, Text, bold: true);
        Label(new Rect(tile.xMax + 14, row.center.y + 1, textW, 20), Ellipsize(value, 13, textW, Face.Mono), 13, Text2, Face.Mono);
        var clicked = -1;
        var x = r.x;
        for (var i = 0; i < buttons.Length; i++)
        {
            if (_ui.Button(id + i, new Rect(x, r.y, widths[i], 36), buttons[i].Label, buttons[i].Icon))
            {
                clicked = i;
            }

            x += widths[i] + 8;
        }

        return clicked;
    }

    private void Copy(string text)
    {
        GUIUtility.systemCopyBuffer = text;
        _copied = text;
        _copiedUntil = Time.unscaledTime + 2f;
    }

    private bool JustCopied(string text) => _copied == text && Time.unscaledTime < _copiedUntil;

    /// <summary>复制按钮的文字：刚复制过这一项的 2 秒内显示「已复制」。</summary>
    private string CopyLabel(string text, string label) => JustCopied(text) ? Lang.T("已复制") : label;

    private string CopyIcon(string text) => JustCopied(text) ? "check" : "copy";
}
