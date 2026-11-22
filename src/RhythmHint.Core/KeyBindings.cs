using System;

namespace RhythmHint.Core;

/// <summary>
/// 设置里的键位：几个按键名用空格隔开，如 "D F J K"、"A S ; '"、"LeftShift Z X Space"（6K、8K 各存一份，见 Settings.KeyLabelsFor）。
/// 按键名是 Unity 的 KeyCode 名字或符号本身（Mod 用它点亮按键框），没指定的位置写 <see cref="Unbound"/>。
/// </summary>
public static class KeyBindings
{
    /// <summary>没指定按键的位置（Unity 的 KeyCode.None）。</summary>
    public const string Unbound = "None";

    /// <summary>拆成 <paramref name="count"/> 个按键名：不足的补 <see cref="Unbound"/>，多出来的忽略。</summary>
    public static string[] Split(string? labels, int count = 4)
    {
        var parts = (labels ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var keys = new string[count];
        for (var i = 0; i < keys.Length; i++)
        {
            keys[i] = i < parts.Length ? parts[i] : Unbound;
        }

        return keys;
    }

    /// <summary>
    /// 把第 <paramref name="lane"/> 个键换成 <paramref name="key"/>，返回新的键位文字。
    /// <paramref name="key"/> 已经在别的位置时两个位置互换，键不会重复。
    /// </summary>
    public static string Assign(string? labels, int lane, string key, int count = 4)
    {
        if (lane < 0 || lane >= count)
        {
            throw new ArgumentOutOfRangeException(nameof(lane), lane, $"按键框只有 {count} 个。");
        }

        if (string.IsNullOrEmpty(key) || key.IndexOf(' ') >= 0)
        {
            throw new ArgumentException("按键名不能为空，也不能含空格。", nameof(key));
        }

        var keys = Split(labels, count);
        var other = Array.FindIndex(keys, k => Same(k, key));
        if (other >= 0 && other != lane)
        {
            keys[other] = keys[lane];
        }

        keys[lane] = key;
        return string.Join(" ", keys);
    }

    /// <summary>两个按键名是不是同一个键（字母不分大小写，"d" 和 "D" 一样）。</summary>
    public static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 按键框里显示的字：长名字缩写（LeftShift → LShift，UpArrow → ↑，Keypad4 → Num4），没指定的显示空白，单个字母大写。
    /// 鼠标侧键按系统的叫法从 1 数：Unity 的 Mouse3 是第 4 个键（MB4）。
    /// </summary>
    public static string Display(string key) => key switch
    {
        _ when key.StartsWith("Keypad", StringComparison.Ordinal) && key.Length == 7 && char.IsDigit(key[6]) => "Num" + key[6],
        "KeypadPeriod" => "Num.",
        "KeypadDivide" => "Num/",
        "KeypadMultiply" => "Num*",
        "KeypadMinus" => "Num-",
        "KeypadPlus" => "Num+",
        "KeypadEnter" => "NumEnter",
        "KeypadEquals" => "Num=",
        "PageUp" => "PgUp",
        "PageDown" => "PgDn",
        "Insert" => "Ins",
        "Delete" => "Del",
        "Backspace" => "Bksp",
        "CapsLock" => "Caps",
        "Mouse2" => "MMB",
        "Mouse3" => "MB4",
        "Mouse4" => "MB5",
        "Mouse5" => "MB6",
        "Mouse6" => "MB7",
        Unbound => string.Empty,
        "LeftShift" => "LShift",
        "RightShift" => "RShift",
        "LeftControl" => "LCtrl",
        "RightControl" => "RCtrl",
        "LeftAlt" => "LAlt",
        "RightAlt" => "RAlt",
        "Return" => "Enter",
        "UpArrow" => "↑",
        "DownArrow" => "↓",
        "LeftArrow" => "←",
        "RightArrow" => "→",
        _ => key.Length == 1 ? key.ToUpperInvariant() : key,
    };
}
