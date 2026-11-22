using System;
using System.Linq;
using RhythmHint.Core;
using UnityEngine;
using static RhythmHint.WindowSkin;

namespace RhythmHint;

/// <summary>
/// 屏幕键盘：主键区、小键盘、其他按键（F1–F12、方向键区、鼠标中键和侧键）三页，全部用鼠标点。
/// 不放输入框、也不录按键：在窗口里按键盘，游戏也会收到（Unity 输入和异步输入两条路都拦不住，2026-11-19 用户选了只做屏幕键盘）。
/// 按键名存进设置，要能被 <see cref="KeyCodes.Parse"/> 认出来。
/// </summary>
internal sealed class KeyboardPanel
{
    private const float Unit = 40;
    private const float Gap = 5;

    /// <summary>主键区：每行开头的缩进和按键。</summary>
    private static readonly (float Indent, string[] Keys)[] MainRows =
    {
        (0, new[] { "`", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=" }),
        (22, new[] { "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "[", "]", "\\" }),
        (33, new[] { "A", "S", "D", "F", "G", "H", "J", "K", "L", ";", "'" }),
        (55, new[] { "Z", "X", "C", "V", "B", "N", "M", ",", ".", "/" }),
        (0, new[] { "LeftShift", "LeftControl", "LeftAlt", "Space", "RightAlt", "RightControl", "RightShift" }),
        (0, new[] { "Tab", "Return", "LeftArrow", "UpArrow", "DownArrow", "RightArrow" }),
    };

    /// <summary>其他按键：功能键、编辑键、鼠标（左右键要用来点界面，不放）。</summary>
    private static readonly (float Indent, string[] Keys)[] OtherRows =
    {
        (0, new[] { "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12" }),
        (0, new[] { "Insert", "Home", "PageUp", "Delete", "End", "PageDown", "CapsLock", "Backspace" }),
        (0, new[] { "Mouse2", "Mouse3", "Mouse4" }),
    };

    /// <summary>小键盘：按键、列、行、占几列、占几行（加号和回车两行高，0 两列宽）。屏幕上只写键面上的字。</summary>
    private static readonly (string Key, string Face, int Col, int Row, int Cols, int Rows)[] Numpad =
    {
        ("KeypadDivide", "/", 1, 0, 1, 1), ("KeypadMultiply", "*", 2, 0, 1, 1), ("KeypadMinus", "-", 3, 0, 1, 1),
        ("Keypad7", "7", 0, 1, 1, 1), ("Keypad8", "8", 1, 1, 1, 1), ("Keypad9", "9", 2, 1, 1, 1), ("KeypadPlus", "+", 3, 1, 1, 2),
        ("Keypad4", "4", 0, 2, 1, 1), ("Keypad5", "5", 1, 2, 1, 1), ("Keypad6", "6", 2, 2, 1, 1),
        ("Keypad1", "1", 0, 3, 1, 1), ("Keypad2", "2", 1, 3, 1, 1), ("Keypad3", "3", 2, 3, 1, 1), ("KeypadEnter", "Enter", 3, 3, 1, 2),
        ("Keypad0", "0", 0, 4, 2, 1), ("KeypadPeriod", ".", 2, 4, 1, 1),
    };

    private int _tab;

    /// <summary>翻到某个键所在的那一页（打开键盘时按当前的键选页）。</summary>
    public void ShowPageOf(string key)
    {
        _tab = Array.Exists(Numpad, k => KeyBindings.Same(k.Key, key)) ? 1
            : Array.Exists(OtherRows, r => Array.Exists(r.Keys, k => KeyBindings.Same(k, key))) ? 2
            : 0;
    }

    /// <summary>画在卡片里（左右留白），返回被点的键；没点为 null。state 给出每个键的状态。</summary>
    public string? Draw(Ui ui, string id, Func<string, KeyState> state)
    {
        var tabs = new[] { Lang.T("主键区"), Lang.T("小键盘"), Lang.T("其他按键") };
        var tabRect = ui.Take(36);
        _tab = ui.Segmented(id + ":tab", new Rect(tabRect.x + Ui.Pad, tabRect.y, Ui.SegmentedWidth(tabs), 34), tabs, _tab);
        ui.Gap(10);

        var height = _tab switch
        {
            1 => (5 * (Unit + 8 + Gap)) - Gap,
            2 => (3 * (Unit + Gap)) - Gap,
            _ => (6 * (Unit + Gap)) - Gap,
        };
        var well = new Rect(ui.X + Ui.Pad, ui.Y, ui.Width - (Ui.Pad * 2), height + 24);
        Fill(well, Well, 10);
        Stroke(well, Border, 1, 10);
        ui.Gap(well.height + 14);

        var origin = new Vector2(well.x + 12, well.y + 12);
        string? picked = null;
        switch (_tab)
        {
            case 1:
                // 小键盘稍大一点，四列放在键盘区正中
                var cell = Unit + 8;
                var left = well.center.x - (((4 * cell) + (3 * Gap)) / 2);
                foreach (var (key, face, col, row, cols, rows) in Numpad)
                {
                    var r = new Rect(left + (col * (cell + Gap)), origin.y + (row * (cell + Gap)),
                        (cols * cell) + ((cols - 1) * Gap), (rows * cell) + ((rows - 1) * Gap));
                    if (ui.Keycap(id + key, r, face, state(key), face.Length > 1 ? 13 : 16))
                    {
                        picked = key;
                    }
                }

                break;
            case 2:
                picked = Rows(ui, id, origin, OtherRows, state);
                break;
            default:
                picked = Rows(ui, id, origin, MainRows, state);
                break;
        }

        return picked;
    }

    private static string? Rows(Ui ui, string id, Vector2 origin, (float Indent, string[] Keys)[] rows, Func<string, KeyState> state)
    {
        string? picked = null;
        var y = origin.y;
        foreach (var (indent, keys) in rows)
        {
            var x = origin.x + indent;
            foreach (var key in keys)
            {
                var shown = KeyBindings.Display(key);
                var width = key == "Space" ? 170 : shown.Length <= 1 ? Unit : shown.Length <= 3 ? 48 : 72;
                if (ui.Keycap(id + key, new Rect(x, y, width, Unit), shown, state(key), shown.Length > 3 ? 12 : 14))
                {
                    picked = key;
                }

                x += width + Gap;
            }

            y += Unit + Gap;
        }

        return picked;
    }
}

/// <summary>
/// 「按键框」的键位：4K、6K、8K 各一套（2026-11-22），每套是默认的（D F J K、S D F J K L、A S D F J K L ;），或「自定义」用屏幕键盘
/// 给每个框各选一个键（选中第几个框再点键盘，点完自动跳到下一个）。已经是打开设置窗口快捷键的键不能选（不然打歌时按到会开关设置窗口）。
/// </summary>
internal sealed class KeyPicker
{
    private static readonly Settings Defaults = new();

    private readonly KeyboardPanel _keyboard = new();
    private bool _custom;   // 点了「自定义」：当前键位正好是默认时也显示自定义
    private int _slot = -1; // 正在指定的按键框，-1 表示没选、不显示屏幕键盘
    private int _keys;      // 正在设哪一套（4、6、8），0 = 还没选过：按这一关 / 演示现在的键数

    /// <summary>收起自定义（换页、恢复默认值时）。当前键位不是默认时仍会显示自定义。</summary>
    public void Close()
    {
        _custom = false;
        _slot = -1;
    }

    public void Draw(Ui ui, Settings s)
    {
        // 这一帧画什么在开头定好：点击改的是字段，下一帧才生效，同一帧里控件数量不变
        var count = _keys > 0 ? _keys : DemoPlayer.DemoKeys(s);
        // 按键页上面就是「使用键数」，这一行叫「要修改的键位」，免得看成在这里切游戏用几键
        var tab = ui.Choice("keySet", "keyboard", Lang.T("要修改的键位"), null, Mathf.Max(0, Array.IndexOf(KeyLayout.Modes, count)), "4K", "6K", "8K");
        if (KeyLayout.Modes[tab] != count)
        {
            Close();
            _keys = KeyLayout.Modes[tab];
            if (s.AutoKeyCount)
            {
                DemoPlayer.PreviewKeys = _keys; // 演示跟着换，看得到这一套键位画在面板上的样子
            }
        }

        var labels = s.KeyLabelsFor(count);
        var defaults = Defaults.KeyLabelsFor(count);
        var custom = _custom || labels != defaults;
        var slot = custom ? _slot : -1;
        var keys = KeyBindings.Split(labels, count);
        var hint = !custom ? null
            : slot >= 0 ? Lang.T("为第 {0} 个按键框选择按键；已被占用的键会互换。", slot + 1)
            : Lang.T("点击按键框可重新指定按键。");
        var choice = ui.Choice("keys", "keyboard", Lang.T("键位"), hint, custom ? 1 : 0, defaults, Lang.T("自定义"));
        if (choice == 0 && custom)
        {
            s.SetKeyLabels(count, defaults);
            Close();
        }
        else if (choice == 1 && !custom)
        {
            _custom = true;
            _slot = 0; // 直接从第 1 个键开始点
            _keyboard.ShowPageOf(keys[0]);
        }

        if (!custom)
        {
            return;
        }

        // 按键框：正在选的那个橙底、一闪一闪；两只手中间多空一点。8 个放一行时框窄一点
        var slotsRow = ui.Take(64);
        const float gap = 12;
        const float handGap = 28;
        var slotW = Mathf.Min(92, (slotsRow.width - (Ui.Pad * 2) - ((keys.Length - 2) * gap) - handGap) / keys.Length);
        var left = slotsRow.center.x - (((keys.Length * slotW) + ((keys.Length - 2) * gap) + handGap) / 2);
        for (var i = 0; i < keys.Length; i++)
        {
            var r = new Rect(left + (i * (slotW + gap)) + (i >= keys.Length / 2 ? handGap - gap : 0), slotsRow.y + 6, slotW, 48);
            if (i == slot)
            {
                var pulse = 0.5f + (0.5f * Mathf.Sin(Time.unscaledTime * 5f));
                Stroke(new Rect(r.x - 4, r.y - 4, r.width + 8, r.height + 5), With(Accent, 0.12f + (0.18f * pulse)), 4, 11);
            }

            var shown = KeyBindings.Display(keys[i]);
            if (ui.Keycap("slot" + i, r, shown.Length > 0 ? shown : "—", i == slot ? KeyState.Selected : KeyState.Normal, shown.Length > 4 ? 13 : 17))
            {
                _slot = i == slot ? -1 : i;
                if (_slot >= 0)
                {
                    _keyboard.ShowPageOf(keys[i]);
                }
            }
        }

        ui.Gap(10);
        if (slot < 0)
        {
            return;
        }

        var hotkey = s.SettingsHotkey;
        var picked = _keyboard.Draw(ui, "kb", key =>
            KeyBindings.Same(key, keys[slot]) ? KeyState.Selected
            : KeyBindings.Same(key, hotkey) ? KeyState.Blocked
            : Array.Exists(keys, k => KeyBindings.Same(k, key)) ? KeyState.InUse
            : KeyState.Normal);
        ui.Hint(Lang.T("橙色字的键已分给其他按键框；灰色的键是打开设置窗口的快捷键，不能选。"));
        if (picked != null)
        {
            s.SetKeyLabels(count, KeyBindings.Assign(labels, slot, picked, count));
            _custom = true;
            _slot = slot + 1 < keys.Length ? slot + 1 : -1; // 都点完就收起键盘
            if (_slot >= 0)
            {
                _keyboard.ShowPageOf(keys[_slot]);
            }
        }
    }
}

/// <summary>打开 / 关闭设置窗口的快捷键：几个常用的，或「自定义」用屏幕键盘选任意键（已分给按键框的不能选）。</summary>
internal sealed class HotkeyPicker
{
    private static readonly string[] Presets = { "F8", "F9", "F7", "F6", "Home", "Insert" };

    private readonly KeyboardPanel _keyboard = new();
    private bool _custom;

    public void Close() => _custom = false;

    public void Draw(Ui ui, Settings s)
    {
        var custom = _custom || Array.FindIndex(Presets, p => KeyBindings.Same(p, s.SettingsHotkey)) < 0;
        ui.Row("keyboard", Lang.T("打开 / 关闭设置窗口"), null, 0, 0);
        var row = ui.Take(44);
        var x = row.x + Ui.Pad;
        foreach (var preset in Presets)
        {
            var w = Mathf.Max(58, WindowSkin.Measure(preset, 14, Face.Mono, true).x + 24);
            if (ui.Keycap("hk" + preset, new Rect(x, row.y, w, 40), preset, !custom && KeyBindings.Same(preset, s.SettingsHotkey) ? KeyState.Selected : KeyState.Normal))
            {
                s.SettingsHotkey = preset;
                _custom = false;
            }

            x += w + 8;
        }

        var label = custom ? Lang.T("自定义：{0}", KeyBindings.Display(s.SettingsHotkey)) : Lang.T("自定义");
        var cw = WindowSkin.Measure(label, 14, Face.Mono, true).x + 28;
        if (ui.Keycap("hkcustom", new Rect(x + 8, row.y, cw, 40), label, custom ? KeyState.Selected : KeyState.Normal) && !custom)
        {
            _custom = true;
            _keyboard.ShowPageOf(s.SettingsHotkey);
        }

        ui.Gap(14);
        if (!custom)
        {
            return;
        }

        // 4K、6K、8K 哪一套里的键都不能当快捷键（哪一关都可能用到）
        var lanes = Array.ConvertAll(KeyLayout.Modes, k => KeyBindings.Split(s.KeyLabelsFor(k), k)).SelectMany(k => k).ToArray();
        var picked = _keyboard.Draw(ui, "hkb", key =>
            KeyBindings.Same(key, s.SettingsHotkey) ? KeyState.Selected
            : Array.Exists(lanes, k => KeyBindings.Same(k, key)) ? KeyState.Blocked
            : KeyState.Normal);
        ui.Hint(Lang.T("灰色的键已分给按键框，不能用作快捷键。"));
        if (picked != null)
        {
            s.SettingsHotkey = picked;
        }
    }
}
