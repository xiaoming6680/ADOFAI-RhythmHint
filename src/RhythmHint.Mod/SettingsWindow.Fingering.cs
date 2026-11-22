using System.Globalization;
using RhythmHint.Core;
using UnityEngine;
using static RhythmHint.WindowSkin;

namespace RhythmHint;

/// <summary>设置窗口：排键和专属手序两页。</summary>
internal sealed partial class SettingsWindow
{
    /// <summary>多押里相邻两块最多隔多久（毫秒），只读 FingeringOptions 的默认值，不在设置里调。</summary>
    private static readonly int MaxChordGapMs = Mathf.RoundToInt((float)(new FingeringOptions().ChordMaxGap * 1000));

    private bool _advanced; // 「高级参数」展开了没有

    /// <summary>间隔（毫秒）相当于多少 BPM 的直线：180° 直线每块砖一拍，BPM = 60000 / 毫秒。</summary>
    private static int StraightBpm(int ms) => Mathf.RoundToInt(60000f / Mathf.Max(1, ms));

    // ---------- 排键 ----------

    private void RulesPage(Settings s, Overlay overlay)
    {
        PageHeader(Page.Rules, Lang.T("自动排键只看本关的砖块间隔，不针对任何谱面特化；修改后立即重新排键。"));
        KeysCard(s);
        StyleCard(s);

        // 惯用手与多押：两个选择 + 键位图（主手橙框、多押用的键蓝光），说明里的手和键跟着换（左撇子是整套左右镜像）
        var (right, left) = (Lang.T("右手"), Lang.T("左手"));
        var keys = KeyBindings.Split(s.KeyLabels);
        _ui.BeginCard("hands", "keyboard", Lang.T("惯用手与多押"));
        s.LeftHanded = _ui.Choice("hand", "hand", Lang.T("惯用手"), null, s.LeftHanded, right, left);
        s.ChordsOnOtherHand = _ui.Choice("chordHand", "chord", Lang.T("多押用手"), null, s.ChordsOnOtherHand, Lang.T("同惯用手"), Lang.T("另一只手"));
        var chordLeft = s.LeftHanded != s.ChordsOnOtherHand;
        var map = _ui.Take(124);
        SettingsArt.HandMap(new Rect(map.x, map.y + 12, map.width, 104), keys, s.LeftHanded, chordLeft, left, right, Lang.T("主手"));
        Legend(Lang.T("主手：慢速单点、停顿后起手"), Lang.T("多押优先使用"));
        var main = s.LeftHanded ? left : right;
        var chordKeys = chordLeft ? Keys(keys, 0, 1) : Keys(keys, 2, 3);
        _ui.Hint(Lang.T("只看本关的砖块间隔：慢时{0}食指单点，稍快时左右食指交替，极快时同手轮指；多押优先{1}（{2}），三押由另一只手补一个键。",
            main, chordLeft ? left : right, chordKeys));
        _ui.EndCard();

        // 节奏分界：色带尺跟着主手重置间隔动
        _ui.BeginCard("rhythm", "gauge", Lang.T("节奏分界"));
        var ruler = _ui.Take(118);
        var alternate = Lang.T("左右交替");
        SettingsArt.Ruler(new Rect(ruler.x + Ui.Pad, ruler.y + 10, ruler.width - (Ui.Pad * 2), 104), s.ResetWindowMs,
            s.UseRoll ? Lang.T("同手轮指") : alternate, alternate, Lang.T("{0}食指单点", main), Lang.T("相邻两块砖的间隔"));
        s.ResetWindowMs = _ui.SliderInt("reset", "clock", Lang.T("主手重置间隔"),
            Lang.T("间隔更长时{0}食指单点；当前相当于 {1} BPM 的直线。", main, StraightBpm(s.ResetWindowMs)), s.ResetWindowMs, 100, 300, 5, v => Ms(v));
        _ui.EndCard();

        _ui.BeginCard("roll", "roll", Lang.T("轮指"));
        s.UseRoll = _ui.Switch("roll", "roll", Lang.T("极快时同手轮指"), null, s.UseRoll);
        s.OuterRoll = _ui.Switch("outer", "swap", Lang.T("外侧手指先按"), null, s.OuterRoll);
        if (s.UseRoll)
        {
            RollOrder(s, keys);
        }
        else
        {
            _ui.Hint(Lang.T("轮指关闭时只用 {0}、{1}。", KeyBindings.Display(keys[1]), KeyBindings.Display(keys[2])));
        }

        _ui.EndCard();

        AdvancedCard(s);

        var label = Lang.T("重新排键");
        var row = _ui.Take(36);
        if (_ui.Button("rebuild", new Rect(row.x, row.y, Ui.ButtonWidth(label, "refresh"), 36), label, "refresh"))
        {
            overlay.RequestRebuild();
        }

        _ui.Gap(10);
        _ui.Feedback(overlay.Feedback, Overlay.RulesArea, 0);
        _ui.Gap(6);
        ResetFooter(overlay, "rules", s.ResetFingering, Lang.T("惯用手和多押用手保持不变。"));
    }

    /// <summary>
    /// 键数（2026-11-22 用户：按谱面难度动态选择，玩家可以关掉、改难度区间或者直接选几键）：自动（试验性）或 4K、6K、8K。
    /// 自动时可以改从哪一档起用 6K、8K，演示按几键播另外选（演示谱的难度估出来是 P 档，按难度只会是 4K）。
    /// </summary>
    private void KeysCard(Settings s)
    {
        _ui.BeginCard("keyCount", "keyboard", Lang.T("键数"));
        var selected = s.AutoKeyCount ? 0 : System.Array.IndexOf(KeyLayout.Modes, s.KeyCountFor(0).Keys) + 1;
        var picked = _ui.Choice("keyCount", "keyboard", Lang.T("用几键"), s.AutoKeyCount ? Lang.T("按估算难度自动选（试验性）") : null, selected,
            Lang.T("自动"), "4K", "6K", "8K");
        if (picked != selected)
        {
            s.AutoKeyCount = picked == 0;
            if (picked > 0)
            {
                s.KeyCount = KeyLayout.Modes[picked - 1];
            }
        }

        if (s.AutoKeyCount)
        {
            s.SixKeysFromTier = _ui.SliderInt("sixFrom", "gauge", Lang.T("从这一档起用 6K"), null, s.SixKeysFromTier, 1, 61, 1, TierName);
            s.EightKeysFromTier = _ui.SliderInt("eightFrom", "gauge", Lang.T("从这一档起用 8K"), null, s.EightKeysFromTier, s.SixKeysFromTier, 61, 1, TierName);
            var preview = System.Array.IndexOf(KeyLayout.Modes, DemoPlayer.PreviewKeys);
            DemoPlayer.PreviewKeys = KeyLayout.Modes[_ui.Choice("previewKeys", "play", Lang.T("演示按几键播"), null, Mathf.Max(0, preview), "4K", "6K", "8K")];
            _ui.Hint(Lang.T("比 6K 的起点低的用 4K；估不出难度时用 4K。用上的专属手序是几键就按几键显示。"), 6);
        }

        _ui.Hint(Lang.T("6K、8K 先照 4K 排在中间四个键上，快到一根手指来不及再按时才用上无名指、小指。各键数的按键在「外观」页的按键框里设。"));
        _ui.EndCard();
    }

    /// <summary>档位名（P1…U20），61 是不用这种键数。</summary>
    private static string TierName(int tier) => tier >= KeyCountChooser.Never ? Lang.T("不用") : DifficultyTiers.Name(tier);

    /// <summary>排法：两张卡片；选了交替优先，它自己的参数在下面展开（左边一道黄条，看得出是它专属的）。</summary>
    private void StyleCard(Settings s)
    {
        _ui.BeginCard("style", "hand", Lang.T("排法"));
        var standard = (Title: Lang.T("标准"), Tag: Lang.T("默认"), Desc: Lang.T("按间隔分为单点、左右交替与同手轮指，适用于大多数关卡。"));
        var alternate = (Title: Lang.T("交替优先"), Tag: Lang.T("试验性"), Desc: Lang.T("快速的短串改用左右食指交替，多押前后少换手。"));
        var w = (_ui.Width - (Ui.Pad * 2) - 12) / 2;
        var h = Mathf.Max(Ui.OptionHeight(w, standard.Desc, 0), Ui.OptionHeight(w, alternate.Desc, 0));
        var row = _ui.Take(h + 8);
        var isAlternate = s.Style == FingeringStyle.ReviewPreferenceV1;
        if (_ui.Option("standard", new Rect(row.x + Ui.Pad, row.y + 4, w, h), !isAlternate, standard.Title, standard.Tag, Accent2, standard.Desc, 0, null))
        {
            s.Style = FingeringStyle.TimeReset;
        }

        if (_ui.Option("alternate", new Rect(row.x + Ui.Pad + w + 12, row.y + 4, w, h), isAlternate, alternate.Title, alternate.Tag, Warn, alternate.Desc, 0, null))
        {
            s.Style = FingeringStyle.ReviewPreferenceV1;
        }

        _ui.Gap(12);
        if (_ui.BeginFold("alternate", s.Style == FingeringStyle.ReviewPreferenceV1))
        {
            var (x, width) = (_ui.X, _ui.Width);
            _ui.X += Ui.Pad;
            _ui.Width -= Ui.Pad * 2;
            _ui.BeginCard("alternateGroup", fill: Well, bar: Warn);
            _ui.Gap(8);
            _ui.Hint(Lang.T("以下参数只对交替优先排法起作用；含长按的关卡与标准排法相同。"), 4);
            var half = Mathf.Max(36f, s.ResetWindowMs / 2f);
            s.ReviewShortMinGapMs = _ui.SliderInt("minGap", null, Lang.T("短串最短间隔"), null, s.ReviewShortMinGapMs, 35, Mathf.FloorToInt(half), 1, v => Ms(v));
            s.ReviewShortMaxGapMs = _ui.Slider("maxGap", null, Lang.T("短串最长间隔"), null, s.ReviewShortMaxGapMs, s.ReviewShortMinGapMs,
                Mathf.Max(s.ReviewShortMinGapMs, half), 0.5f, v => v.ToString("0.0", CultureInfo.InvariantCulture) + " ms");
            s.ReviewShortMinNotes = _ui.SliderInt("minNotes", null, Lang.T("短串最少音符数"), null, s.ReviewShortMinNotes, 2, 8, 1, v => v.ToString(CultureInfo.InvariantCulture));
            s.ReviewShortMaxNotes = _ui.SliderInt("maxNotes", null, Lang.T("短串最多音符数"), null, s.ReviewShortMaxNotes, s.ReviewShortMinNotes, 16, 1,
                v => v.ToString(CultureInfo.InvariantCulture));
            s.ReviewTimingToleranceMs = _ui.Slider("tolerance", null, Lang.T("节奏允许误差"), null, s.ReviewTimingToleranceMs, 0f, 10f, 0.5f,
                v => v.ToString("0.0", CultureInfo.InvariantCulture) + " ms");
            _ui.Hint(Lang.T("间隔和音符数都在范围内、节奏均匀的连打改用左右食指交替；最长间隔不超过主手重置间隔的一半，更快或更长的仍按轮指。"), 10);
            _ui.EndCard();
            _ui.X = x;
            _ui.Width = width;
        }

        _ui.EndFold();
        _ui.EndCard();
    }

    /// <summary>键位图下面的图例：橙框是主手，蓝光是多押优先用的键。</summary>
    private void Legend(string mainText, string chordText)
    {
        var r = _ui.Take(30);
        var mw = Measure(mainText, 12).x;
        var cw = Measure(chordText, 12).x;
        var x = r.center.x - ((mw + cw + 64) / 2);
        Stroke(new Rect(x, r.y + 9, 11, 11), Accent, 1.5f, 3);
        Label(new Rect(x + 18, r.y, mw + 2, 28), mainText, 12, Text3);
        x += mw + 40;
        Stroke(new Rect(x, r.y + 9, 11, 11), Blue, 1.5f, 3);
        Label(new Rect(x + 18, r.y, cw + 2, 28), chordText, 12, Text3);
        _ui.Gap(4);
    }

    private static string Keys(string[] keys, int a, int b) => KeyBindings.Display(keys[a]) + " " + KeyBindings.Display(keys[b]);

    /// <summary>轮指顺序：四个键帽中间画箭头（外侧先按 K J D F，食指先按 J K F D；左撇子左右镜像；写玩家自己的键位）。</summary>
    private void RollOrder(Settings s, string[] keys)
    {
        var lanes = s.OuterRoll
            ? (s.LeftHanded ? new[] { 0, 1, 3, 2 } : new[] { 3, 2, 0, 1 })
            : (s.LeftHanded ? new[] { 1, 0, 2, 3 } : new[] { 2, 3, 1, 0 });
        const float cap = 38;
        const float arrow = 26;
        var width = (4 * cap) + (3 * arrow);
        var r = _ui.Row("right", Lang.T("轮指顺序"), null, width, cap);
        for (var i = 0; i < 4; i++)
        {
            var x = r.x + (i * (cap + arrow));
            var shown = KeyBindings.Display(keys[lanes[i]]);
            SettingsArt.Cap(new Rect(x, r.y, cap, cap), shown, i == 0, shown.Length > 3 ? 11 : 14);
            if (i < 3)
            {
                Icon(new Vector2(x + cap + (arrow / 2), r.center.y - 1), 14, "right", Text3);
            }
        }
    }

    /// <summary>不常调的三个参数收在「高级参数」里，点标题展开（高度和透明度一起动）。</summary>
    private void AdvancedCard(Settings s)
    {
        _ui.BeginCard("advanced");
        var head = _ui.Take(56);
        if (_ui.Click(head))
        {
            _advanced = !_advanced;
        }

        var hover = _ui.Hover("advanced", head);
        Fill(new Rect(head.x + 1, head.y + 1, head.width - 2, head.height - 2), With(Color.white, 0.02f * hover), 11);
        Icon(new Vector2(head.x + Ui.Pad + 8, head.center.y), 17, "sliders", Text3);
        var title = Lang.T("高级参数");
        var tw = Measure(title, 14, Face.Body, true).x;
        Label(new Rect(head.x + Ui.Pad + 26, head.y, tw + 4, head.height), title, 14, Text2, bold: true);
        var summary = Lang.T("多押换手范围、同键连按、多押转角");
        var room = head.width - (Ui.Pad * 2) - 26 - tw - 16 - 30;
        Label(new Rect(head.x + Ui.Pad + 26 + tw + 14, head.y, room, head.height), Ellipsize(summary, 13, room), 13, Text3);
        var turn = Motion.Ease("rules:advanced:chev", _advanced ? 180 : 0, 0.25f);
        Icon(new Vector2(head.xMax - Ui.Pad - 9, head.center.y), 18, "chev", Mix(Text3, Text, hover), turn);

        if (_ui.BeginFold("advanced", _advanced))
        {
            s.ChordFocusMs = _ui.SliderInt("focus", "chord", Lang.T("多押换手范围"), s.Style == FingeringStyle.ReviewPreferenceV1
                    ? Lang.T("与多押相隔在此范围内的单押改用另一只手；交替优先排法下超出主手重置间隔的部分不起作用；0 为不换手。")
                    : Lang.T("与多押相隔在此范围内的单押改用另一只手；0 为不换手。"),
                s.ChordFocusMs, 0, 800, 10, v => Ms(v));
            s.JackWindowMs = _ui.SliderInt("jack", "repeat", Lang.T("同键连按最小间隔"),
                Lang.T("当前相当于 {0} BPM 的直线，更快时不连按同一个键。", StraightBpm(s.JackWindowMs)), s.JackWindowMs, 50, 300, 5, v => Ms(v));
            s.ChordAngleDeg = _ui.SliderInt("angle", "angle", Lang.T("多押转角上限"),
                Lang.T("相邻两块砖转角不超过此值、且相隔不超过 {0} 毫秒时合为多押；连续 5 块以上视为连打。", MaxChordGapMs), s.ChordAngleDeg, 0, 60, 1,
                v => v.ToString(CultureInfo.InvariantCulture) + "°");
        }

        _ui.EndFold();
        _ui.EndCard();
    }

    // ---------- 专属手序 ----------

    private void CustomPage(Settings s, Overlay overlay)
    {
        PageHeader(Page.Custom, Lang.T("为某一首歌单独指定每个音符的按键，进入这首歌时自动使用。"));
        var fingering = overlay.Current.Snapshot?.Fingering;
        FingeringStatusCard(s, overlay, fingering);
        EditorCard(overlay);
        HowToCard();
    }

    /// <summary>本关用的是哪份手序 + 开关；分不清是哪份、或者玩家选过时列出候选（选择按谱面指纹记住）。</summary>
    private void FingeringStatusCard(Settings s, Overlay overlay, FingeringResult? fingering)
    {
        var label = Lang.T("使用专属手序");
        var lw = Measure(label, 13).x;
        var controlW = lw + 14 + 44;
        var status = overlay.FingeringStatus;
        var live = fingering != null && fingering.UsedFile.Length > 0;

        _ui.BeginCard("fingering");
        var textX = _ui.X + Ui.Pad + 54;
        var textW = _ui.Width - Ui.Pad - 54 - Ui.Pad - controlW - 16;
        var subH = WrappedHeight(status, 13, textW);
        var row = _ui.Take(Mathf.Max(76, subH + 50));
        var tile = new Rect(row.x + Ui.Pad, row.center.y - 20, 40, 40);
        var t = Motion.Ease("custom:live", live ? 1 : 0, 0.3f);
        Fill(tile, Mix(Surface3, With(Ok, 0.14f), t), 10);
        Icon(tile.center, 20, "file", Mix(Text2, Ok, t));
        var top = row.center.y - ((22 + subH) / 2);
        Label(new Rect(textX, top, textW, 22), Lang.T("本关手序"), 15, Text, bold: true);
        Label(new Rect(textX, top + 23, textW, subH), status, 13, Text3, wrap: true, anchor: TextAnchor.UpperLeft);
        var sw = new Rect(row.xMax - Ui.Pad - 44, row.center.y - 12, 44, 24);
        Label(new Rect(sw.x - 14 - lw, row.y, lw + 2, row.height), label, 13, Text2);
        s.UseCustomFingering = _ui.SwitchControl("useCustom", sw, new Rect(sw.x - 14 - lw, sw.y - 6, lw + 58, 36), s.UseCustomFingering);

        if (fingering != null && (fingering.Candidates.Count > 0 || fingering.ChosenByPlayer))
        {
            Fill(new Rect(_ui.X + 1, _ui.Y, _ui.Width - 2, 1), With(Color.white, 0.045f));
            _ui.Gap(12);
            for (var i = 0; i < fingering.Candidates.Count; i++)
            {
                var c = fingering.Candidates[i];
                if (Candidate("cand" + i, c.Name, Lang.T("对上 {0}/{1}", c.MatchedHits, c.TotalHits), fingering.UsedFile == c.Name))
                {
                    overlay.ChooseFingering(c.Name);
                }
            }

            if (Candidate("none", Lang.T("都不用"), null, fingering.ChosenByPlayer && fingering.UsedFile.Length == 0))
            {
                overlay.ChooseFingering(string.Empty);
            }

            if (fingering.ChosenByPlayer && Candidate("auto", Lang.T("重新自动识别"), null, false, "refresh"))
            {
                overlay.ChooseFingering(null);
            }

            _ui.Gap(8);
        }

        _ui.EndCard();
    }

    /// <summary>候选手序的一行：单选圆点、名字、右边对上了多少。</summary>
    private bool Candidate(string id, string name, string? meta, bool selected, string? icon = null)
    {
        var r = _ui.Take(46);
        var item = new Rect(r.x + Ui.Pad, r.y, r.width - (Ui.Pad * 2), 40);
        var clicked = _ui.Click(item);
        var hover = _ui.Hover(id, item);
        var t = Motion.Ease("custom:" + id, selected ? 1 : 0, 0.18f);
        Fill(item, Mix(Surface2, Surface3, hover), 9);
        Stroke(item, Mix(Border, With(Accent, 0.6f), t), 1, 9);
        var dot = new Vector2(item.x + 22, item.center.y);
        if (icon != null)
        {
            Icon(dot, 16, icon, Text2);
        }
        else
        {
            WindowSkin.Stroke(new Rect(dot.x - 8, dot.y - 8, 16, 16), Mix(Border2, Accent, t), 2, 8);
            Circle(dot, 4 * t, Accent);
        }

        var metaW = meta == null ? 0 : Measure(meta, 12, Face.Mono).x;
        Label(new Rect(item.x + 42, item.y, item.width - 60 - metaW, item.height), Ellipsize(name, 14, item.width - 60 - metaW), 14, Text);
        if (meta != null)
        {
            Label(new Rect(item.xMax - 14 - metaW, item.y, metaW + 2, item.height), meta, 12, Text3, Face.Mono);
        }

        return clicked;
    }

    /// <summary>编辑器入口：示意图、大号橙色「编辑本关手序」、说明；下面打开手序文件夹、桌面快捷方式和结果。</summary>
    private void EditorCard(Overlay overlay)
    {
        _ui.BeginCard("editor");
        var label = Lang.T("编辑本关手序");
        var message = overlay.EditorMessage;
        var artW = 170f;
        var textX = _ui.X + Ui.Pad + artW + 22;
        var textW = _ui.Width - (textX - _ui.X) - Ui.Pad;
        var mh = WrappedHeight(message, 13, textW);
        var r = _ui.Take(Mathf.Max(132, 18 + 46 + 12 + mh + 18));
        SettingsArt.EditorArt(new Rect(r.x + Ui.Pad, r.y + 14, artW, r.height - 28), SettingsArt.KeyLabels(Main.Settings));
        var top = r.center.y - ((46 + 12 + mh) / 2);
        var bw = Ui.ButtonWidth(label, "edit", true);
        if (_ui.Button("editor", new Rect(textX, top, bw, 46), label, "edit", ButtonKind.Primary, true))
        {
            overlay.OpenEditor();
        }

        Label(new Rect(textX, top + 58, textW, mh), message, 13, Text3, wrap: true, anchor: TextAnchor.UpperLeft);

        // 桌面快捷方式是 Windows 的 .lnk，别的系统不显示
        var windows = Application.platform == RuntimePlatform.WindowsPlayer;
        var buttons = windows
            ? new (string, string?, ButtonKind)[] { (Lang.T("打开手序文件夹"), "folder", ButtonKind.Normal), (Lang.T("创建桌面快捷方式"), "shortcut", ButtonKind.Normal) }
            : new (string, string?, ButtonKind)[] { (Lang.T("打开手序文件夹"), "folder", ButtonKind.Normal) };
        Fill(new Rect(_ui.X + 1, _ui.Y, _ui.Width - 2, 1), With(Color.white, 0.045f));
        _ui.Gap(14);
        switch (_ui.Buttons("files", true, buttons))
        {
            case 0:
                overlay.OpenFingeringFolder();
                break;
            case 1:
                overlay.CreateEditorShortcut();
                break;
        }

        _ui.Feedback(overlay.Feedback, Overlay.EditorArea);
        _ui.Gap(6);
        _ui.EndCard();
    }

    /// <summary>使用方法：三步，图标之间一条虚线连着。</summary>
    private void HowToCard()
    {
        var steps = new (string Icon, string Title, string Desc)[]
        {
            ("play", Lang.T("进入关卡"), Lang.T("游玩或在编辑器中试玩时，点击编辑本关手序。")),
            ("edit", Lang.T("在浏览器中修改"), Lang.T("逐个音符改键并保存，保存后立即生效。")),
            ("share", Lang.T("自动使用与分享"), Lang.T("再次进入这首歌时自动使用；也可以把文件发给他人。")),
        };
        _ui.BeginCard("howto", "flag", Lang.T("使用方法"));
        var w = (_ui.Width - (Ui.Pad * 2)) / 3;
        var descH = 0f;
        foreach (var step in steps)
        {
            descH = Mathf.Max(descH, WrappedHeight(step.Desc, 13, w - 18));
        }

        var r = _ui.Take(46 + 26 + descH + 12);
        for (var i = 0; i < steps.Length; i++)
        {
            var x = r.x + Ui.Pad + (i * w);
            var circle = new Vector2(x + 19, r.y + 25);
            if (i < steps.Length - 1)
            {
                for (var d = x + 48; d < x + w - 6; d += 10)
                {
                    Fill(new Rect(d, circle.y, 5, 1), Border2);
                }
            }

            Circle(circle, 19, Surface3);
            WindowSkin.Stroke(new Rect(circle.x - 19, circle.y - 19, 38, 38), Border2, 1, 19);
            Icon(circle, 18, steps[i].Icon, Accent2);
            Label(new Rect(x, r.y + 52, w - 18, 22), (i + 1).ToString(CultureInfo.InvariantCulture) + ". " + steps[i].Title, 14, Text, bold: true);
            Label(new Rect(x, r.y + 76, w - 18, descH), steps[i].Desc, 13, Text3, wrap: true, anchor: TextAnchor.UpperLeft);
        }

        _ui.Gap(4);
        _ui.Hint(Lang.T("手序文件放进手序文件夹后就不用再管：进到对应的歌时自动使用；谱面更新后对不上的地方改用自动排键，并在开局时提醒。"));
        _ui.EndCard();
    }
}
