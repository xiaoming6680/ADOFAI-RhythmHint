using System;
using System.Collections.Generic;
using RhythmHint.Core;
using UnityEngine;
using static RhythmHint.WindowSkin;

namespace RhythmHint;

/// <summary>按钮的样子：普通、主按钮（橙色）、幽灵（只有字，悬停时出底色）、危险（红色，确认用）。</summary>
internal enum ButtonKind
{
    Normal,
    Primary,
    Ghost,
    Danger,
}

/// <summary>屏幕键盘上一个键的状态。</summary>
internal enum KeyState
{
    Normal,

    /// <summary>正在选的这个框现在用的键（橙底）。</summary>
    Selected,

    /// <summary>分给了别的框（橙字），点了会互换。</summary>
    InUse,

    /// <summary>不能选（灰字），比如已经是打开设置窗口的快捷键。</summary>
    Blocked,
}

/// <summary>
/// 设置窗口的排版和控件。手写竖排游标（不用 GUILayout）：每个控件都拿到确定的矩形，动画、折叠、平滑滚动都好做，
/// 也不会出现 GUILayout 布局和绘制两遍控件数对不上的问题。
/// 卡片背景要先画、却要等里面画完才知道多高：用上一帧量到的高度（变化时晚一帧，有动画盖着看不出来）。
/// 坐标是当前 GUI 组里的；只认 <see cref="Clip"/> 以内的点击（滚出可见区的控件点不到）。
/// </summary>
internal sealed class Ui
{
    /// <summary>卡片内左右留白。</summary>
    public const float Pad = 18;

    /// <summary>卡片之间的空隙。</summary>
    public const float CardGap = 16;

    /// <summary>滑块（含加减按钮和数值框）的总宽。</summary>
    public const float SliderWidth = 380;

    private const float RowMin = 54;

    private readonly Dictionary<string, float> _measured = new();
    private readonly Stack<Block> _blocks = new();
    private readonly Stack<Group> _groups = new();

    /// <summary>当前这一栏的左边、宽度和游标（往下排）。</summary>
    public float X { get; set; }

    public float Y { get; set; }

    public float Width { get; set; }

    /// <summary>当前坐标系里看得见、点得到的区域。</summary>
    public Rect Clip { get; set; }

    /// <summary>关掉时控件只画不响应（窗口淡出中、引导盖在上面时）。</summary>
    public bool Interactive { get; set; } = true;

    /// <summary>动画和缓存高度的名字前缀（每页一个，不同页的同名控件互不影响）。</summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>最近一行（<see cref="Row"/>）的整行矩形：开关行点整行都能切换。</summary>
    public Rect LastRow { get; private set; }

    private string Key(string id) => Scope + id;

    // ---------- 鼠标 ----------

    public bool Over(Rect r)
    {
        var m = Event.current.mousePosition;
        return Interactive && Clip.Contains(m) && r.Contains(m);
    }

    /// <summary>悬停程度 0–1（淡入淡出）。按住别的控件拖动时不算悬停。</summary>
    public float Hover(string id, Rect r) =>
        Motion.Ease(Key("hv:" + id), Over(r) && GUIUtility.hotControl == 0 ? 1 : 0, 0.12f);

    /// <summary>
    /// 点击：在 r 里按下、又在 r 里松开才算（按下后移出去松开就取消，和系统按钮一样）。pressed 是正按着。
    /// 每个控件每次绘制都要调用一次，控件编号才对得上。
    /// </summary>
    public bool Click(Rect r, out bool pressed)
    {
        var id = GUIUtility.GetControlID(FocusType.Passive);
        var e = Event.current;
        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (e.button == 0 && Over(r))
                {
                    GUIUtility.hotControl = id;
                    e.Use();
                }

                break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl == id)
                {
                    e.Use();
                }

                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl == id)
                {
                    GUIUtility.hotControl = 0;
                    e.Use();
                    pressed = false;
                    return r.Contains(e.mousePosition);
                }

                break;
        }

        pressed = GUIUtility.hotControl == id;
        return false;
    }

    public bool Click(Rect r) => Click(r, out _);

    // ---------- 排版 ----------

    public Rect Take(float height)
    {
        var r = new Rect(X, Y, Width, height);
        Y += height;
        return r;
    }

    public void Gap(float height) => Y += height;

    /// <summary>页标题：彩色图标块、大标题、一句说明。</summary>
    public void PageHeader(string icon, string title, string desc)
    {
        var textX = X + 62;
        var textW = Width - 62;
        var descH = WrappedHeight(desc, 14, textW);
        var r = Take(Mathf.Max(48, 34 + descH));
        var tile = new Rect(X, r.y + 1, 46, 46);
        Fill(tile, With(Accent, 0.14f), 12);
        Icon(tile.center, 24, icon, Accent2);
        Label(new Rect(textX, r.y - 2, textW, 32), title, 25, Text, Face.Display, true);
        Label(new Rect(textX, r.y + 32, textW, descH), desc, 14, Text3, wrap: true);
        Gap(20);
    }

    /// <summary>
    /// 开始一张卡片（圆角底、细边框），可带图标和标题。和 <see cref="EndCard"/> 配对。
    /// note 是标题旁边的一句灰色说明，放不下时换到标题下面。
    /// </summary>
    public void BeginCard(string id, string? icon = null, string? title = null, Color? fill = null, Color? border = null, Color? bar = null,
        string? note = null)
    {
        var key = Key("card:" + id);
        var top = Y;
        _measured.TryGetValue(key, out var height);
        var r = new Rect(X, top, Width, height);
        Fill(r, fill ?? Surface, 12);
        Stroke(r, border ?? Border, 1, 12);
        if (bar is { } color)
        {
            // 左边一道彩色竖条：附属于上面某个选项的一组参数
            Fill(new Rect(r.x, r.y, 3, r.height), color, new Vector4(12, 0, 0, 12));
        }

        _blocks.Push(new Block(key, top));
        if (title == null)
        {
            Gap(4);
            return;
        }

        var head = Take(42);
        var textX = head.x + Pad;
        if (icon != null)
        {
            Icon(new Vector2(head.x + Pad + 8, head.y + 25), 17, icon, Text3);
            textX += 26;
        }

        Label(new Rect(textX, head.y + 13, head.width - (textX - head.x) - Pad, 24), title, 14, Text2, bold: true);
        if (string.IsNullOrEmpty(note))
        {
            return;
        }

        var noteX = textX + Measure(title, 14, Face.Body, true).x + 12;
        var room = head.xMax - Pad - noteX;
        if (Measure(note!, 13).x <= room)
        {
            Label(new Rect(noteX, head.y + 13, room, 24), note!, 13, Text3);
            return;
        }

        var width = head.xMax - Pad - textX;
        var below = Take(WrappedHeight(note!, 13, width) + 6);
        Label(new Rect(textX, below.y - 2, width, below.height), note!, 13, Text3, wrap: true, anchor: TextAnchor.UpperLeft);
    }

    public void EndCard()
    {
        Gap(4);
        var b = _blocks.Pop();
        _measured[b.Key] = Y - b.Top;
        Gap(CardGap);
    }

    /// <summary>
    /// 卡片里的一行：左边图标、名字和一句说明（可没有），右边留给控件，返回控件的矩形（右对齐、上下居中）。
    /// 名字和说明在左边那一栏里换行，英文、韩文长一些也放得下。
    /// </summary>
    public Rect Row(string? icon, string title, string? sub, float controlWidth, float controlHeight, string? tag = null, Color? tagColor = null)
    {
        var textX = X + Pad + (icon != null ? 34 : 0);
        var textW = Width - (textX - X) - Pad - controlWidth - (controlWidth > 0 ? 18 : 0);
        var tagW = string.IsNullOrEmpty(tag) ? 0 : Measure(tag!, 11, Face.Body, true).x + 24;
        var titleW = textW - tagW;
        var titleH = Mathf.Max(22, WrappedHeight(title, 15, titleW));
        var subH = string.IsNullOrEmpty(sub) ? 0 : WrappedHeight(sub!, 13, textW);
        var content = titleH + (subH > 0 ? subH + 2 : 0);
        var r = Take(Mathf.Max(RowMin, Mathf.Max(content, controlHeight) + 20));
        LastRow = r;
        if (_blocks.Count > 0)
        {
            var block = _blocks.Peek();
            if (block.Rows > 0)
            {
                Fill(new Rect(r.x + 1, r.y, r.width - 2, 1), With(Color.white, 0.045f));
            }

            block.Rows++;
        }

        var top = r.y + ((r.height - content) / 2);
        if (icon != null)
        {
            Icon(new Vector2(X + Pad + 10, top + 11), 19, icon, Text3);
        }

        Label(new Rect(textX, top, titleW, titleH), title, 15, Text, wrap: true, anchor: TextAnchor.UpperLeft);
        if (tagW > 0)
        {
            // 名字后面的小标签（试验性）：跟在名字第一行的末尾
            var shown = Mathf.Min(Measure(title, 15).x, titleW);
            Tag(new Vector2(textX + shown + 10, top + 1), tag!, tagColor ?? Warn);
        }

        if (subH > 0)
        {
            Label(new Rect(textX, top + titleH + 2, textW, subH), sub!, 13, Text3, wrap: true, anchor: TextAnchor.UpperLeft);
        }

        return new Rect(r.xMax - Pad - controlWidth, r.y + ((r.height - controlHeight) / 2), controlWidth, controlHeight);
    }

    /// <summary>卡片里的一段说明（灰色小字，左右留白）。</summary>
    public void Hint(string text, float bottom = 14)
    {
        if (text.Length == 0)
        {
            return;
        }

        var w = Width - (Pad * 2);
        var h = WrappedHeight(text, 13, w);
        Label(new Rect(X + Pad, Y, w, h), text, 13, Text3, wrap: true, anchor: TextAnchor.UpperLeft);
        Gap(h + bottom);
    }

    /// <summary>
    /// 可折叠的一段（高级参数、交替优先的参数）：展开 / 收起时高度和透明度一起动，超出部分裁掉。
    /// 返回要不要画里面的内容（完全收起时不画，也就点不到）；不管返回什么都要调用 <see cref="EndFold"/>。
    /// </summary>
    public bool BeginFold(string id, bool open)
    {
        var key = Key("fold:" + id);
        var t = Motion.Ease(key + ":t", open ? 1 : 0, 0.26f);
        _measured.TryGetValue(key, out var height);
        var visible = new Rect(X, Y, Width, Mathf.Round(height * t));
        _groups.Push(new Group(key, X, Y + visible.height, Clip, Alpha, visible.position, t));
        GUI.BeginGroup(visible);
        Clip = Intersect(new Rect(Clip.x - visible.x, Clip.y - visible.y, Clip.width, Clip.height), new Rect(0, 0, visible.width, visible.height));
        X -= visible.x;
        Y = 0;
        Alpha *= Mathf.Clamp01((t * 1.4f) - 0.2f);
        return open || t > 0.001f;
    }

    /// <summary>每次画窗口前调用：清掉上一次（比如出过错）没配对留下的折叠段、滚动区和卡片记录。GUI 自己的裁剪栈 Unity 每个事件会重置。</summary>
    public void BeginFrame()
    {
        _groups.Clear();
        _blocks.Clear();
    }

    /// <summary>
    /// 画到一半出错时调用：把还没关的折叠段、滚动区（GUI.BeginGroup）全部关上，清掉卡片栈，坐标和透明度回到最外层。
    /// </summary>
    public void Unwind()
    {
        while (_groups.Count > 0)
        {
            var g = _groups.Pop();
            GUI.EndGroup();
            X = g.X;
            Y = g.After;
            Clip = g.Clip;
            Alpha = g.Alpha;
        }

        _blocks.Clear();
    }

    public void EndFold()
    {
        var g = _groups.Pop();
        _measured[g.Key] = Y;
        GUI.EndGroup();
        X = g.X;
        Y = g.After;
        Clip = g.Clip;
        Alpha = g.Alpha;
    }

    /// <summary>
    /// 开始一块能滚动的区域（滚轮平滑滚动、右边细滚动条可拖）。里面从 (padX, padTop) 开始排，宽度扣掉两边留白。
    /// 和 <see cref="EndScroll"/> 配对。<paramref name="reset"/> 为真时回到顶部（换页）。
    /// </summary>
    public void BeginScroll(string id, Rect viewport, float padX, float padTop, bool reset)
    {
        var key = "scroll:" + id;
        _measured.TryGetValue(key, out var content);
        _measured.TryGetValue(key + ":target", out var target);
        var max = Mathf.Max(0, content - viewport.height);
        var e = Event.current;
        if (e.type == EventType.ScrollWheel && Over(viewport))
        {
            target += e.delta.y * 18;
            e.Use();
        }

        target = reset ? 0 : Mathf.Clamp(target, 0, max);
        _measured[key + ":target"] = target;
        if (reset)
        {
            Motion.Set(key, 0);
        }

        var shown = Motion.Ease(key, target, 0.14f);
        _groups.Push(new Group(key, X, Y, Clip, Alpha, viewport.position, shown) { Width = Width, Viewport = viewport, Max = max });
        GUI.BeginGroup(viewport);
        Clip = Intersect(new Rect(Clip.x - viewport.x, Clip.y - viewport.y, Clip.width, Clip.height), new Rect(0, 0, viewport.width, viewport.height));
        X = padX;
        Width = viewport.width - (padX * 2);
        Y = padTop - shown;
    }

    public void EndScroll(float padBottom)
    {
        var g = _groups.Pop();
        var shown = g.T;
        _measured[g.Key] = Y + shown + padBottom;
        DrawScrollbar(g);
        GUI.EndGroup();
        X = g.X;
        Y = g.After;
        Width = g.Width;
        Clip = g.Clip;
    }

    /// <summary>右边的细滚动条：内容比可见区高时出现，悬停或拖动时变粗变亮，可以拖。</summary>
    private void DrawScrollbar(Group g)
    {
        // 先取编号再决定画不画：内容高度跨过可见区时滚动条出现或消失，后面控件（引导的按钮、拖动标题栏）的编号不跟着错位
        var id = GUIUtility.GetControlID(FocusType.Passive);
        if (g.Max <= 0)
        {
            if (GUIUtility.hotControl == id)
            {
                GUIUtility.hotControl = 0;
            }

            return;
        }

        var view = g.Viewport.size;
        var content = view.y + g.Max;
        var thumbH = Mathf.Max(40, view.y * view.y / content);
        var track = new Rect(view.x - 12, 6, 10, view.y - 12);
        var thumb = new Rect(track.x, track.y + ((track.height - thumbH) * (g.T / g.Max)), track.width, thumbH);
        var e = Event.current;
        var key = g.Key + ":target";
        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (e.button == 0 && Over(track))
                {
                    GUIUtility.hotControl = id;
                    _measured[g.Key + ":grab"] = thumb.Contains(e.mousePosition) ? e.mousePosition.y - thumb.y : thumbH / 2;
                    e.Use();
                }

                break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl == id)
                {
                    var grab = _measured.TryGetValue(g.Key + ":grab", out var held) ? held : thumbH / 2;
                    var y = e.mousePosition.y - grab - track.y;
                    _measured[key] = Mathf.Clamp(y / Mathf.Max(1, track.height - thumbH) * g.Max, 0, g.Max);
                    Motion.Set(g.Key, _measured[key]);
                    e.Use();
                }

                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl == id)
                {
                    GUIUtility.hotControl = 0;
                    e.Use();
                }

                break;
        }

        var active = GUIUtility.hotControl == id ? 1 : Motion.Ease(g.Key + ":hv", Over(track) ? 1 : 0, 0.12f);
        var w = Mathf.Lerp(4, 7, active);
        Fill(new Rect(thumb.xMax - w - 1, thumb.y, w, thumb.height), With(Color.white, Mathf.Lerp(0.14f, 0.28f, active)), w / 2);
    }

    // ---------- 控件 ----------

    /// <summary>一行：名字和开关。点整行都能切换（和以前一样，点名字也行）。</summary>
    public bool Switch(string id, string? icon, string title, string? sub, bool value, string? tag = null)
    {
        var r = Row(icon, title, sub, 44, 24, tag);
        return SwitchControl(id, r, LastRow, value);
    }

    /// <summary>开关本身：橙色为开，圆点弹到右边；按住时圆点变宽。hit 是能点的范围。</summary>
    public bool SwitchControl(string id, Rect r, Rect hit, bool value)
    {
        if (Click(hit, out var pressed))
        {
            value = !value;
        }

        var t = Motion.Spring(Key("sw:" + id), value ? 1 : 0, 4.6f, 0.62f);
        var hover = Hover("sw:" + id, hit);
        var on = Mathf.Clamp01(t);
        var track = Mix(Mix(Border2, Accent, on), Color.white, hover * 0.07f);
        Fill(r, track, r.height / 2);
        var knobW = 18 + (pressed ? 5 : 0);
        var x = Mathf.LerpUnclamped(r.x + 3, r.xMax - 3 - knobW, t);
        Fill(new Rect(x, r.y + 4, knobW, 18), With(Color.black, 0.25f), 9);
        Fill(new Rect(x, r.y + 3, knobW, 18), Color.white, 9);
        return value;
    }

    /// <summary>分段选择的总宽。</summary>
    public static float SegmentedWidth(string[] labels)
    {
        var w = 6f;
        foreach (var label in labels)
        {
            w += Measure(label, 14, Face.Body, true).x + 28;
        }

        return w + (2 * (labels.Length - 1));
    }

    /// <summary>分段选择：选中的那段是亮一档的底、橙色字和细橙边，换选项时这块底弹簧滑过去。</summary>
    public int Segmented(string id, Rect r, string[] labels, int selected)
    {
        Fill(r, Surface2, 9);
        Stroke(r, Border, 1, 9);
        var x = r.x + 3;
        var items = new Rect[labels.Length];
        for (var i = 0; i < labels.Length; i++)
        {
            var w = Measure(labels[i], 14, Face.Body, true).x + 28;
            items[i] = new Rect(x, r.y + 3, w, r.height - 6);
            x += w + 2;
            if (Click(items[i]))
            {
                selected = i;
            }
        }

        if (selected >= 0 && selected < items.Length)
        {
            var target = items[selected];
            var px = Motion.Spring(Key("seg:" + id + ":x"), target.x, 4.2f, 0.78f);
            var pw = Motion.Spring(Key("seg:" + id + ":w"), target.width, 4.2f, 0.78f);
            var pill = new Rect(px, target.y, pw, target.height);
            Fill(new Rect(pill.x, pill.y + 1, pill.width, pill.height), With(Color.black, 0.25f), 7);
            Fill(pill, Surface3, 7);
            Stroke(pill, With(Accent, 0.55f), 1, 7);
        }

        for (var i = 0; i < labels.Length; i++)
        {
            var hover = Hover("seg:" + id + i, items[i]);
            var color = i == selected ? Accent2 : Mix(Text2, Text, hover);
            Label(items[i], labels[i], 14, color, bold: i == selected, anchor: TextAnchor.MiddleCenter);
        }

        return selected;
    }

    /// <summary>一行：名字和分段选择。</summary>
    public int Choice(string id, string? icon, string title, string? sub, int selected, params string[] options)
    {
        var r = Row(icon, title, sub, SegmentedWidth(options), 36);
        return Segmented(id, r, options, selected);
    }

    /// <summary>两个选项的分段选择（前一个对应 false）。</summary>
    public bool Choice(string id, string? icon, string title, string? sub, bool value, string no, string yes) =>
        Choice(id, icon, title, sub, value ? 1 : 0, no, yes) == 1;

    /// <summary>一行：名字和滑块。</summary>
    public float Slider(string id, string? icon, string title, string? sub, float value, float min, float max, float step, Func<float, string> format)
    {
        var r = Row(icon, title, sub, SliderWidth, 30);
        return Slider(id, r, value, min, max, step, format);
    }

    /// <summary>
    /// 整数滑块（毫秒、音符数、角度）。故意不和 <see cref="Slider(string, string?, string, string?, float, float, float, float, Func{float, string})"/> 同名：
    /// 同名重载时这里转调小数版，整数参数会被编译器挑回这个重载自己，无限递归把游戏撑崩（2026-11-19 引导翻到排键页时崩溃）。
    /// </summary>
    public int SliderInt(string id, string? icon, string title, string? sub, int value, int min, int max, int step, Func<int, string> format) =>
        Mathf.RoundToInt(Slider(id, icon, title, sub, (float)value, min, max, step, (float v) => format(Mathf.RoundToInt(v))));

    /// <summary>
    /// 滑块：两边加减按钮（每次一格），中间可拖的条，右边数值框。值按 step 取整。
    /// 点加减时圆点滑过去，拖动时跟手。
    /// </summary>
    public float Slider(string id, Rect r, float value, float min, float max, float step, Func<float, string> format)
    {
        var cy = r.center.y;
        var minus = new Rect(r.x, cy - 13, 26, 26);
        var box = new Rect(r.xMax - 72, cy - 14, 72, 28);
        var plus = new Rect(box.x - 8 - 26, cy - 13, 26, 26);
        var track = new Rect(minus.xMax + 10, cy - 11, plus.x - 10 - (minus.xMax + 10), 22);
        var range = max - min;
        // 玩家这一帧动了这个滑块才取整、写回；没动就原样返回。打开页面、后台预排（SettingsWindow.Prewarm）都会走到这里，
        // 原来会把手改的、旧版本留下的不在刻度上的值悄悄改掉（2026-11-19 游戏实测：Settings.xml 里的 131 没开排键页就成了 130）
        var original = value;
        var touched = false;
        if (StepButton(id + "-", minus, "minus"))
        {
            value -= step;
            touched = true;
        }

        if (StepButton(id + "+", plus, "plus"))
        {
            value += step;
            touched = true;
        }

        var cid = GUIUtility.GetControlID(FocusType.Passive);
        var e = Event.current;
        switch (e.GetTypeForControl(cid))
        {
            case EventType.MouseDown:
                if (e.button == 0 && range > 0 && Over(new Rect(track.x - 8, track.y, track.width + 16, track.height)))
                {
                    GUIUtility.hotControl = cid;
                    value = min + (Mathf.Clamp01((e.mousePosition.x - track.x) / track.width) * range);
                    touched = true;
                    e.Use();
                }

                break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl == cid)
                {
                    value = min + (Mathf.Clamp01((e.mousePosition.x - track.x) / track.width) * range);
                    touched = true;
                    e.Use();
                }

                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl == cid)
                {
                    GUIUtility.hotControl = 0;
                    e.Use();
                }

                break;
        }

        value = Mathf.Clamp(value, min, max);
        if (step > 0)
        {
            // 按格取整；小数步长四舍五入到 4 位，免得 0.05 累加成 0.30000001
            value = (float)Math.Round(min + (Mathf.Round((value - min) / step) * step), 4);
            value = Mathf.Clamp(value, min, max);
        }

        var dragging = GUIUtility.hotControl == cid;
        var t = range > 0 ? (value - min) / range : 1;
        var key = Key("sl:" + id);
        if (dragging)
        {
            Motion.Set(key, t);
        }

        var shown = Motion.Ease(key, t, 0.1f);
        var hover = dragging ? 1 : Hover("sl:" + id, track);
        var line = new Rect(track.x, cy - 2, track.width, 4);
        Fill(line, Border2, 2);
        Fill(new Rect(line.x, line.y, line.width * shown, 4), Accent, 2);
        var knob = new Vector2(track.x + (track.width * shown), cy);
        Circle(knob, 8 + (hover * 5), With(Accent, 0.25f * hover));
        Circle(new Vector2(knob.x, knob.y + 1), 8, With(Color.black, 0.35f));
        Circle(knob, 8 + (hover * 0.8f), Color.white);

        Fill(box, Surface2, 7);
        Stroke(box, Border, 1, 7);
        Label(box, format(touched ? value : original), 13, Text, Face.Mono, true, TextAnchor.MiddleCenter);
        return touched ? value : original;
    }

    /// <summary>加减按钮：小图标，悬停出底色。</summary>
    private bool StepButton(string id, Rect r, string icon)
    {
        var clicked = Click(r, out var pressed);
        var hover = Hover("st:" + id, r);
        Fill(r, With(Surface3, hover + (pressed ? 0.4f : 0)), 7);
        Icon(r.center, 15, icon, Mix(Text3, Text, hover));
        return clicked;
    }

    /// <summary>按钮宽度（按文字量）。</summary>
    public static float ButtonWidth(string label, string? icon, bool big = false) =>
        Measure(label, big ? 16 : 14, Face.Body, true).x + (icon != null ? 26 : 0) + (big ? 40 : 28);

    /// <summary>按钮：悬停变亮，按下时下沉 1 像素；主按钮带一圈橙色柔光。</summary>
    public bool Button(string id, Rect r, string label, string? icon = null, ButtonKind kind = ButtonKind.Normal, bool big = false)
    {
        var clicked = Click(r, out var pressed);
        var hover = Hover("bt:" + id, r);
        var body = new Rect(r.x, r.y + (pressed ? 1 : 0), r.width, r.height);
        Color text;
        switch (kind)
        {
            case ButtonKind.Primary:
                Glow(new Rect(body.x - 18, body.y - 6, body.width + 36, body.height + 30), With(Accent, 0.22f + (0.14f * hover)));
                Fill(body, Mix(Accent, Accent2, hover), 9);
                text = Ink;
                break;
            case ButtonKind.Ghost:
                Fill(body, With(Surface2, hover), 8);
                text = Mix(Text3, Text2, hover);
                break;
            case ButtonKind.Danger:
                Fill(body, With(Danger, 0.16f + (0.06f * hover)), 8);
                Stroke(body, With(Danger, 0.6f), 1, 8);
                text = new Color(1f, 0.6f, 0.6f);
                break;
            default:
                Fill(body, Mix(Surface2, Surface3, hover), 8);
                Stroke(body, Border2, 1, 8);
                text = Text;
                break;
        }

        var size = big ? 16 : 14;
        var textW = Measure(label, size, Face.Body, kind == ButtonKind.Primary).x;
        var contentW = textW + (icon != null ? 26 : 0);
        var x = body.center.x - (contentW / 2);
        if (icon != null)
        {
            Icon(new Vector2(x + 9, body.center.y), 17, icon, kind is ButtonKind.Normal ? Text2 : text);
            x += 26;
        }

        Label(new Rect(x, body.y, textW + 4, body.height), label, size, text, bold: kind == ButtonKind.Primary);
        return clicked;
    }

    /// <summary>
    /// 一排按钮（卡片里，左右留白），放不下时换行；靠左或靠右。返回被点的下标，没点为 -1。
    /// </summary>
    public int Buttons(string id, bool right, params (string Label, string? Icon, ButtonKind Kind)[] items)
    {
        var clicked = -1;
        var left = X + Pad;
        var avail = Width - (Pad * 2);
        var rows = new List<List<int>> { new() };
        var used = 0f;
        for (var i = 0; i < items.Length; i++)
        {
            var w = ButtonWidth(items[i].Label, items[i].Icon);
            if (rows[rows.Count - 1].Count > 0 && used + 8 + w > avail)
            {
                rows.Add(new List<int>());
                used = 0;
            }

            used += (rows[rows.Count - 1].Count > 0 ? 8 : 0) + w;
            rows[rows.Count - 1].Add(i);
        }

        foreach (var row in rows)
        {
            var total = 0f;
            foreach (var i in row)
            {
                total += ButtonWidth(items[i].Label, items[i].Icon) + 8;
            }

            var x = right ? left + avail - total + 8 : left;
            var r = Take(36);
            foreach (var i in row)
            {
                var w = ButtonWidth(items[i].Label, items[i].Icon);
                if (Button(id + i, new Rect(x, r.y, w, 36), items[i].Label, items[i].Icon, items[i].Kind))
                {
                    clicked = i;
                }

                x += w + 8;
            }

            Gap(8);
        }

        return clicked;
    }

    /// <summary>
    /// 按钮的结果（<see cref="ActionFeedback"/>）：成功绿色、失败红色，浅色底加左边一道色条；出现时往下展开，几秒后淡出收起。
    /// </summary>
    public void Feedback(ActionFeedback feedback, string area, float inset = Pad)
    {
        var alpha = (float)feedback.Opacity(area, Time.unscaledTime);
        var open = Motion.Ease(Key("fb:" + area), alpha > 0 ? 1 : 0, 0.2f);
        if (open <= 0.001f)
        {
            return;
        }

        var text = Lang.T(feedback.Message);
        var w = Width - (inset * 2);
        var h = WrappedHeight(text, 14, w - 46) + 18;
        var r = new Rect(X + inset, Y, w, h);
        Gap((h + 10) * open);
        var color = feedback.IsError ? Danger : Ok;
        var a = alpha * open;
        Fill(r, With(color, 0.14f * a), 8);
        Fill(new Rect(r.x, r.y, 3, r.height), With(color, a), new Vector4(8, 0, 0, 8));
        Icon(new Vector2(r.x + 22, r.y + 19), 16, feedback.IsError ? "alert" : "check", With(color, a));
        var textColor = Mix(color, Color.white, 0.45f);
        Label(new Rect(r.x + 40, r.y + 9, w - 50, h - 18), text, 14, With(textColor, a), wrap: true, anchor: TextAnchor.UpperLeft);
    }

    /// <summary>选项卡片（音符样式、排法）的高度。</summary>
    public static float OptionHeight(float width, string desc, float artHeight) =>
        12 + (artHeight > 0 ? artHeight + 12 : 0) + 24 + WrappedHeight(desc, 13, width - 24) + 14;

    /// <summary>
    /// 选项卡片：上面一张小图（可没有）、名字、标签、说明。选中时橙色描边加柔光，右上角弹出对勾。返回有没有被点。
    /// </summary>
    public bool Option(string id, Rect r, bool selected, string title, string? tag, Color tagColor, string desc, float artHeight, Action<Rect>? art)
    {
        var clicked = Click(r, out _);
        var hover = Hover("op:" + id, r);
        var sel = Motion.Ease(Key("op:" + id), selected ? 1 : 0, 0.2f);
        var lift = hover * (1 - sel) * 1.5f;
        var card = new Rect(r.x, r.y - lift, r.width, r.height);
        if (sel > 0)
        {
            Stroke(new Rect(card.x - 4, card.y - 4, card.width + 8, card.height + 8), With(Accent, 0.12f * sel), 4, 15);
        }

        Fill(card, Mix(Mix(Surface2, Surface3, hover), new Color(0.145f, 0.133f, 0.122f), sel), 11);
        Stroke(card, Mix(Mix(Border, Border2, hover), Accent, sel), 1 + (sel * 0.6f), 11);
        var y = card.y + 12;
        if (artHeight > 0 && art != null)
        {
            art(new Rect(card.x + 12, y, card.width - 24, artHeight));
            y += artHeight + 12;
        }

        var titleW = Measure(title, 15, Face.Body, true).x;
        Label(new Rect(card.x + 12, y, titleW + 4, 22), title, 15, Text, bold: true);
        if (!string.IsNullOrEmpty(tag))
        {
            Tag(new Vector2(card.x + 12 + titleW + 10, y + 1), tag!, tagColor);
        }

        var descH = WrappedHeight(desc, 13, card.width - 24);
        Label(new Rect(card.x + 12, y + 26, card.width - 24, descH), desc, 13, Text3, wrap: true, anchor: TextAnchor.UpperLeft);

        var pop = Motion.Spring(Key("opc:" + id), selected ? 1 : 0, 4.4f, 0.48f);
        if (pop > 0.01f)
        {
            var c = new Vector2(card.xMax - 20, card.y + 20);
            Circle(c, 11 * pop, Accent);
            Icon(c, 14 * pop, "check", Ink);
        }

        return clicked;
    }

    /// <summary>小标签（默认、试验性）：浅色底、彩色字。返回宽度。</summary>
    public static float Tag(Vector2 at, string text, Color color)
    {
        var w = Measure(text, 11, Face.Body, true).x + 14;
        var r = new Rect(at.x, at.y, w, 20);
        Fill(r, With(color, 0.15f), 10);
        Label(r, text, 11, color, bold: true, anchor: TextAnchor.MiddleCenter);
        return w;
    }

    /// <summary>胶囊形的小块文字（状态、数值）。返回宽度。</summary>
    public static float Chip(Vector2 at, string text, Face face = Face.Body)
    {
        var w = Measure(text, 12, face).x + 18;
        var r = new Rect(at.x, at.y, w, 24);
        Fill(r, Surface3, 12);
        Label(r, text, 12, Text2, face, anchor: TextAnchor.MiddleCenter);
        return w;
    }

    public static float ChipWidth(string text, Face face = Face.Body) => Measure(text, 12, face).x + 18;

    /// <summary>键帽：有厚度的圆角方块（底下一道深色），按下时下沉；状态见 <see cref="KeyState"/>。</summary>
    public bool Keycap(string id, Rect r, string text, KeyState state, int size = 14)
    {
        var clicked = Click(r, out var pressed);
        var hover = state == KeyState.Blocked ? 0 : Hover("kc:" + id, r);
        var sel = Motion.Ease(Key("kc:" + id), state == KeyState.Selected ? 1 : 0, 0.14f);
        var sink = pressed ? 2 : 0;
        Fill(new Rect(r.x, r.y + 3, r.width, r.height - 3), Mix(new Color(0.09f, 0.1f, 0.11f), new Color(0.6f, 0.28f, 0.08f), sel), 7);
        var face = new Rect(r.x, r.y + sink, r.width, r.height - 3);
        Fill(face, Mix(Mix(Surface3, new Color(0.2f, 0.22f, 0.25f), hover), Accent, sel), 7);
        Stroke(face, With(Color.white, 0.05f + (0.05f * hover)), 1, 7);
        var color = state switch
        {
            KeyState.Selected => Ink,
            KeyState.InUse => Accent2,
            KeyState.Blocked => With(Text3, 0.55f),
            _ => Text,
        };
        Label(face, text, size, color, Face.Mono, true, TextAnchor.MiddleCenter);
        return clicked && state != KeyState.Blocked;
    }

    private static Rect Intersect(Rect a, Rect b)
    {
        var x = Mathf.Max(a.x, b.x);
        var y = Mathf.Max(a.y, b.y);
        return new Rect(x, y, Mathf.Max(0, Mathf.Min(a.xMax, b.xMax) - x), Mathf.Max(0, Mathf.Min(a.yMax, b.yMax) - y));
    }

    private sealed class Block
    {
        public Block(string key, float top)
        {
            Key = key;
            Top = top;
        }

        public string Key { get; }

        public float Top { get; }

        public int Rows { get; set; }
    }

    /// <summary>折叠段或滚动区：进去前的游标、可见区和透明度，出来时还原。</summary>
    private sealed class Group
    {
        public Group(string key, float x, float after, Rect clip, float alpha, Vector2 origin, float t)
        {
            Key = key;
            X = x;
            After = after;
            Clip = clip;
            Alpha = alpha;
            Origin = origin;
            T = t;
        }

        public string Key { get; }

        public float X { get; }

        /// <summary>出来以后游标接着排的位置。</summary>
        public float After { get; }

        public Rect Clip { get; }

        public float Alpha { get; }

        public Vector2 Origin { get; }

        /// <summary>折叠段：展开程度；滚动区：当前滚到哪。</summary>
        public float T { get; }

        public float Width { get; set; }

        public Rect Viewport { get; set; }

        public float Max { get; set; }
    }
}
