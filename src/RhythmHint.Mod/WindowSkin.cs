using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace RhythmHint;

/// <summary>
/// 设置窗口的外观：配色、字体、文字、圆角矩形、阴影和图标（IMGUI，只能在 OnGUI 里用）。
/// 配色和手序编辑器（src/RhythmHint.Editor/editor.css）同一套：深灰底、细边框、橙色强调。
/// 所有颜色都乘上 <see cref="Alpha"/>（开关窗口、换页时整体淡入淡出）；GUI.DrawTexture 带颜色参数时不看 GUI.color，所以自己乘。
/// </summary>
internal static class WindowSkin
{
    public static readonly Color Backdrop = Hex(0x141519);
    public static readonly Color Surface = Hex(0x1b1d22);
    public static readonly Color Surface2 = Hex(0x22252b);
    public static readonly Color Surface3 = Hex(0x2a2e35);
    public static readonly Color Well = Hex(0x15171b);
    public static readonly Color Border = Hex(0x2b2e35);
    public static readonly Color Border2 = Hex(0x383c45);
    public static readonly Color Text = Hex(0xe9ebef);
    public static readonly Color Text2 = Hex(0xb3b8c2);
    public static readonly Color Text3 = Hex(0x7d838e);
    public static readonly Color Accent = Hex(0xf57829);
    public static readonly Color Accent2 = Hex(0xff9550);
    public static readonly Color Blue = Hex(0x3d9ee0);
    public static readonly Color Purple = Hex(0xb273f2);
    public static readonly Color Warn = Hex(0xf2c14e);
    public static readonly Color Danger = Hex(0xef5b5b);
    public static readonly Color Ok = Hex(0x4cc38a);

    /// <summary>橙色底上的字（深棕，比纯黑柔和）。</summary>
    public static readonly Color Ink = Hex(0x1a0d03);

    private static readonly GUIContent Content = new();
    private static readonly Dictionary<string, Texture2D?> Icons = new();

    // 量过的字（宽高、换行高度）：IMGUI 一帧要排好几遍（布局、绘制、鼠标事件），同样的字不重复排版。换字体时清空
    private static readonly Dictionary<(string, int, Face, bool), Vector2> Sizes = new();
    private static readonly Dictionary<(string, int, int, Face, bool), float> Heights = new();
    private static GUIStyle _text = null!;
    private static GUIStyle _shadow = null!;
    private static Texture2D _glow = null!;
    private static bool _ready;

    private static Font? _body;
    private static Font? _display;
    private static Font? _mono;
    private static string? _fontLanguage;
    private static string[]? _installedFonts;

    /// <summary>每种界面语言的三套字体（换回来时直接用，不重建、不留下没人用的字体）。</summary>
    private static readonly Dictionary<string, (Font? Body, Font? Display, Font? Mono)> FontSets = new();

    /// <summary>字体：正文、标题（Segoe UI Variable Display）、等宽（数值、键帽）。</summary>
    public enum Face
    {
        Body,
        Display,
        Mono,
    }

    // 窗口里的字号（1080p 像素）只有这几档，别的字号就近归到这几档（一样近时取大的）：
    // 系统字体第一次排某个字号的字很慢，档位少，开窗口前要预先排好的字形就少（见 Preload）
    private static readonly int[] BodySizes = { 12, 13, 14, 15 };
    private static readonly int[] BodyBoldSizes = { 11, 13, 14, 15 };
    private static readonly int[] DisplaySizes = { 18, 25, 52 }; // 标题字都是粗体
    private static readonly int[] MonoSizes = { 12, 13 };
    private static readonly int[] MonoBoldSizes = { 12, 13, 15, 18 };

    private const string IconPrefix = "RhythmHint.SettingsIcons.";

    /// <summary>预热时除了界面文字以外还要排好的字：数字、英文、键名和窗口里用到的符号。</summary>
    private static readonly string Extra = BuildAscii() + "×—…→←↑↓·−%°：，。、；（）「」！？";

    /// <summary>整体透明度，乘到每次绘制的颜色上。</summary>
    public static float Alpha { get; set; } = 1f;

    /// <summary>按 1080p 的设计尺寸缩放到当前屏幕。</summary>
    public static float ScreenScale => Math.Max(0.5f, Screen.height / 1080f);

    public static bool Repaint => Event.current.type == EventType.Repaint;

    /// <summary>第一次用时准备样式和贴图（要在 OnGUI 里，GUI.skin 才可用）；界面语言变了换一套中日韩字体的先后。</summary>
    public static void Ensure()
    {
        if (!_ready)
        {
            _ready = true;
            _text = new GUIStyle(GUI.skin.label)
            {
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                richText = false,
                clipping = TextClipping.Overflow,
            };
            _shadow = new GUIStyle { border = new RectOffset(ShadowEdge, ShadowEdge, ShadowEdge, ShadowEdge) };
            _shadow.normal.background = ShadowTexture();
            _glow = GlowTexture();
        }

        if (_fontLanguage != Lang.Current)
        {
            UseLanguage(Lang.Current); // 换了界面语言：换字体并预热（会卡一下，换语言很少见）
            WarmGlyphs(GUI.skin.font);
        }

        // 窗口里的字有限（随设置变的说明、数值），缓存一般几百条；万一涨太多就清掉重来
        if (Sizes.Count > 4000 || Heights.Count > 4000)
        {
            Sizes.Clear();
            Heights.Clear();
        }
    }

    // ---------- 预先准备 ----------

    /// <summary>
    /// 提前准备（游戏启动后第一帧调用）：读好全部图标、建好当前语言的字体，把界面上会出现的字按用到的几档字号先排进字体贴图。
    /// 系统字体第一次排这些字很慢（2026-11-19 日志：第一次打开概览页 354 毫秒，第一次换到外观、排键页 130–140 毫秒，换语言 0.3–0.4 秒；
    /// 用户：每次点选项都卡一下），趁游戏还在载入时做掉。
    /// </summary>
    public static void Preload()
    {
        Font.textureRebuilt += LogRebuild;
        var started = Stopwatch.StartNew();
        var icons = PreloadIcons();
        var iconMs = started.ElapsedMilliseconds;
        UseLanguage(Lang.Current);
        var glyphs = WarmGlyphs(null);
        Main.Mod.Logger.Log($"设置窗口：预先读好 {icons} 个图标（{iconMs} 毫秒），排好 {glyphs} 个字的字形，共用了 {started.ElapsedMilliseconds} 毫秒");
    }

    /// <summary>打开窗口时调用：字形万一被别的字挤出字体贴图（比如曲名里的新字让贴图重建），一次补回来，不用等到换页时一页一页地卡。</summary>
    public static void Rewarm()
    {
        if (_fontLanguage == Lang.Current)
        {
            WarmGlyphs(null);
        }
    }

    private static int PreloadIcons()
    {
        var count = 0;
        foreach (var resource in typeof(WindowSkin).Assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(IconPrefix, StringComparison.Ordinal) || !resource.EndsWith(".png", StringComparison.Ordinal))
            {
                continue;
            }

            var name = resource.Substring(IconPrefix.Length, resource.Length - IconPrefix.Length - 4);
            if (!Icons.ContainsKey(name))
            {
                Icons[name] = EmbeddedImage.Load(resource);
                count++;
            }
        }

        return count;
    }

    /// <summary>换成某种界面语言的字体（每种语言只建一次）；量过的字作废。</summary>
    private static void UseLanguage(string language)
    {
        _fontLanguage = language;
        if (FontSets.TryGetValue(language, out var set))
        {
            (_body, _display, _mono) = set;
        }
        else
        {
            LoadFonts(language);
            FontSets[language] = (_body, _display, _mono);
        }

        Sizes.Clear();
        Heights.Clear();
    }

    /// <summary>
    /// 把当前语言会显示的字按窗口用到的各档字号、粗细排进字体贴图。已经排好的字很快就跳过。
    /// fallback 是没有系统字体时用的默认字体（只能在 OnGUI 里取到，别处传 null 就跳过它）。返回排了多少个不同的字。
    /// </summary>
    private static int WarmGlyphs(Font? fallback)
    {
        var all = new HashSet<char>(Lang.AllText() + Extra);
        all.RemoveWhere(char.IsControl);
        var text = new string(new List<char>(all).ToArray());
        var body = _body ?? fallback;
        var display = _display ?? body;
        var mono = _mono ?? fallback;
        Request(body, text, BodySizes, FontStyle.Normal);
        Request(body, text, BodyBoldSizes, FontStyle.Bold);
        Request(display, text, new[] { DisplaySizes[0], DisplaySizes[1] }, FontStyle.Bold);
        Request(display, Extra, new[] { DisplaySizes[2] }, FontStyle.Bold); // 画面偏移的大数字
        Request(mono, text, new[] { 13 }, FontStyle.Normal); // 运行状态是等宽字，里面有中文
        Request(mono, Extra, MonoSizes, FontStyle.Normal);
        Request(mono, text, new[] { 15 }, FontStyle.Bold); // 自定义快捷键的键帽写着「自定义：F8」
        Request(mono, Extra, MonoBoldSizes, FontStyle.Bold);
        return text.Length;
    }

    /// <summary>
    /// 按原字号和放大后的字号各排一遍：窗口按屏幕高度放大（GUI.matrix），字可能按放大后的字号排
    /// （2026-11-19 日志：只按原字号预热，第一次进每页仍然 115–343 毫秒）。
    /// </summary>
    private static void Request(Font? font, string text, int[] sizes, FontStyle style)
    {
        if (font == null || !font.dynamic)
        {
            return;
        }

        var scale = ScreenScale;
        foreach (var size in sizes)
        {
            font.RequestCharactersInTexture(text, size, style);
            var scaled = Mathf.RoundToInt(size * scale);
            if (scaled != size)
            {
                font.RequestCharactersInTexture(text, scaled, style);
            }
        }
    }

    private static int _rebuilds;

    /// <summary>字体贴图每重建一次记一条日志（前 30 次）：新字放不下时 Unity 会把整张贴图重排一遍，打开窗口、换页卡顿多半是它。</summary>
    private static void LogRebuild(Font font)
    {
        if (font == _body || font == _display || font == _mono || font == null)
        {
            _rebuilds++;
            if (_rebuilds <= 30)
            {
                Main.Mod.Logger.Log($"设置窗口：字体贴图重建第 {_rebuilds} 次（{font?.name ?? "默认"}）");
            }
        }
    }

    private static string BuildAscii()
    {
        var chars = new char[95];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = (char)(32 + i);
        }

        return new string(chars);
    }

    /// <summary>把字号归到最近的一档（一样近时取大的）。</summary>
    private static int Snap(Face face, bool bold, int size)
    {
        var sizes = face switch
        {
            Face.Display => DisplaySizes,
            Face.Mono => bold ? MonoBoldSizes : MonoSizes,
            _ => bold ? BodyBoldSizes : BodySizes,
        };
        var best = sizes[0];
        foreach (var s in sizes)
        {
            if (Mathf.Abs(s - size) <= Mathf.Abs(best - size))
            {
                best = s;
            }
        }

        return best;
    }

    // ---------- 字体 ----------

    /// <summary>
    /// 用系统字体：标题 Segoe UI Variable Display、正文 Segoe UI，中日韩文字按界面语言排先后
    /// （日文用 Yu Gothic UI、韩文用 Malgun Gothic、繁中用 JhengHei，免得汉字是别国字形）。
    /// 只在 Windows 上用；一个都找不到或者建不出来时退回 IMGUI 默认字体（五种语言都能显示，用户确认过）。
    /// </summary>
    private static void LoadFonts(string language)
    {
        _body = _display = _mono = null;
        if (Application.platform != RuntimePlatform.WindowsPlayer && Application.platform != RuntimePlatform.WindowsEditor)
        {
            return;
        }

        var cjk = language switch
        {
            "ja" => new[] { "Yu Gothic UI", "Meiryo UI", "Microsoft YaHei UI", "Malgun Gothic" },
            "ko" => new[] { "Malgun Gothic", "Microsoft YaHei UI", "Yu Gothic UI" },
            "zh-Hant" => new[] { "Microsoft JhengHei UI", "Microsoft YaHei UI", "Malgun Gothic", "Yu Gothic UI" },
            _ => new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Malgun Gothic", "Yu Gothic UI" },
        };
        var started = Stopwatch.StartNew();
        string[] installed;
        try
        {
            installed = _installedFonts ??= Font.GetOSInstalledFontNames();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            Main.Mod.Logger.Warning($"读不到系统字体列表，设置窗口使用默认字体：{ex.Message}");
            return;
        }

        _body = Create(installed, new[] { "Segoe UI Variable Text", "Segoe UI" }.Concat(cjk));
        _display = Create(installed, new[] { "Segoe UI Variable Display", "Segoe UI Semibold", "Segoe UI" }.Concat(cjk));
        _mono = Create(installed, new[] { "Cascadia Mono", "Consolas" }.Concat(cjk));
        Main.Mod.Logger.Log($"设置窗口：载入系统字体用了 {started.ElapsedMilliseconds} 毫秒（系统里有 {installed.Length} 种字体；"
            + $"正文 {_body?.name ?? "默认"}，标题 {_display?.name ?? "默认"}，等宽 {_mono?.name ?? "默认"}）");
    }

    /// <summary>
    /// 按顺序挑第一个能排字的系统字体，排在它后面的名字一起交给 Unity 当缺字时的备用；都不行返回 null（用默认字体）。
    /// 建好先试排：Unity 6 的 IMGUI 用 TextCore 排字，要先把字体转成 TextCore 的字体资源，有的系统字体转不出来，
    /// 这时 Unity 不报错，沿用上一次排字的设置，画出来的是上一段字
    /// （2026-11-19 玩家截图：等宽字写的版本号、关闭键上的快捷键、快捷键键帽、运行状态全变成了旁边的标题。玩家的 Player.log：等宽字体是 Cascadia Mono，
    /// 每画一次等宽字 Unity 就打两条找不到字体文件的警告，共 154 万次、日志 312 MB，一次重绘 251 毫秒；开发机的等宽字体是 Consolas，没出过）。
    /// </summary>
    private static Font? Create(string[] installed, IEnumerable<string> wanted)
    {
        var names = wanted.Where(n => Array.IndexOf(installed, n) >= 0).ToArray();
        for (var i = 0; i < names.Length; i++)
        {
            var font = Font.CreateDynamicFontFromOSFont(names.Skip(i).ToArray(), 16);
            if (font == null)
            {
                Main.Mod.Logger.Warning($"设置窗口：无法创建字体 {names[i]}，换下一个");
                continue;
            }

            var (one, ten) = TrialWidths(font);
            if (one > 0 && ten > one * 3)
            {
                return font;
            }

            Main.Mod.Logger.Warning($"设置窗口：字体 {names[i]} 排不了字（一个字宽 {one:F1}、十个字宽 {ten:F1}，Unity 转不出它的 TextCore 字体资源），换下一个");
            UnityEngine.Object.Destroy(font);
        }

        return null;
    }

    /// <summary>量一个字和十个字的宽度。能用的字体十个字宽得多；转不出字体资源时两次量到的都是上一次排过的字，一样宽。</summary>
    private static (float One, float Ten) TrialWidths(Font font)
    {
        var style = new GUIStyle { font = font, fontSize = 14, wordWrap = false };
        return (style.CalcSize(new GUIContent("0")).x, style.CalcSize(new GUIContent("0000000000")).x);
    }

    // ---------- 文字 ----------

    private static GUIStyle Style(Face face, int size, bool bold, TextAnchor anchor, bool wrap)
    {
        _text.font = face switch
        {
            Face.Display => _display ?? _body,
            Face.Mono => _mono,
            _ => _body,
        };
        _text.fontSize = Snap(face, bold, size);
        _text.fontStyle = bold || face == Face.Display ? FontStyle.Bold : FontStyle.Normal;
        _text.alignment = anchor;
        _text.wordWrap = wrap;
        return _text;
    }

    /// <summary>写一段字（只在绘制事件里画）。wrap 时在 r 的宽度内换行。</summary>
    public static void Label(Rect r, string text, int size, Color color, Face face = Face.Body, bool bold = false,
        TextAnchor anchor = TextAnchor.MiddleLeft, bool wrap = false)
    {
        if (!Repaint || text.Length == 0)
        {
            return;
        }

        var style = Style(face, size, bold, anchor, wrap);
        style.normal.textColor = Fade(color);
        Content.text = text;
        style.Draw(r, Content, false, false, false, false);
    }

    /// <summary>一行字的宽高。</summary>
    public static Vector2 Measure(string text, int size, Face face = Face.Body, bool bold = false)
    {
        var key = (text, size, face, bold);
        if (!Sizes.TryGetValue(key, out var result))
        {
            Content.text = text;
            result = Style(face, size, bold, TextAnchor.MiddleLeft, false).CalcSize(Content);
            Sizes[key] = result;
        }

        return result;
    }

    /// <summary>在 width 宽度内换行后的高度。</summary>
    public static float WrappedHeight(string text, int size, float width, Face face = Face.Body, bool bold = false)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        // 向下取整：按比画的宽度略窄的宽度量，临界时宁可多算一行，不让字掉出卡片（四舍五入会量宽、少算一行）
        var key = (text, size, Mathf.FloorToInt(width), face, bold);
        if (!Heights.TryGetValue(key, out var result))
        {
            Content.text = text;
            result = Style(face, size, bold, TextAnchor.UpperLeft, true).CalcHeight(Content, key.Item3);
            Heights[key] = result;
        }

        return result;
    }

    /// <summary>放不下时截短并加省略号（导航的说明、文件名这类一行的字）。</summary>
    public static string Ellipsize(string text, int size, float width, Face face = Face.Body, bool bold = false)
    {
        if (Measure(text, size, face, bold).x <= width)
        {
            return text;
        }

        var lo = 0;
        var hi = text.Length;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (Measure(text.Substring(0, mid) + "…", size, face, bold).x <= width)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return text.Substring(0, lo).TrimEnd() + "…";
    }

    // ---------- 形状 ----------

    public static void Fill(Rect r, Color c, float radius = 0)
    {
        if (Repaint && r.width > 0 && r.height > 0)
        {
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, Fade(c), 0f, Radius(r, radius));
        }
    }

    /// <summary>四个角各自的圆角：左上、右上、右下、左下。</summary>
    public static void Fill(Rect r, Color c, Vector4 radii)
    {
        if (Repaint && r.width > 0 && r.height > 0)
        {
            var most = Mathf.Min(r.width, r.height) / 2;
            radii = new Vector4(Mathf.Min(radii.x, most), Mathf.Min(radii.y, most), Mathf.Min(radii.z, most), Mathf.Min(radii.w, most));
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, Fade(c), Vector4.zero, radii);
        }
    }

    /// <summary>描边（细边框、选中框）。</summary>
    public static void Stroke(Rect r, Color c, float width, float radius = 0)
    {
        if (Repaint && r.width > 0 && r.height > 0)
        {
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, Fade(c), width, Radius(r, radius));
        }
    }

    public static void Circle(Vector2 center, float radius, Color c) =>
        Fill(new Rect(center.x - radius, center.y - radius, radius * 2, radius * 2), c, radius);

    /// <summary>柔光：中心最亮、往外淡出（主按钮的光晕、横幅右上角的橙光）。</summary>
    public static void Glow(Rect r, Color c)
    {
        if (Repaint)
        {
            GUI.DrawTexture(r, _glow, ScaleMode.StretchToFill, true, 0f, Fade(c), 0f, 0f);
        }
    }

    /// <summary>软阴影：r 是投影的物体，往外扩 spread 像素淡出（淡出的宽度固定是 <see cref="ShadowEdge"/>，spread 小于它时从物体里面开始淡）。</summary>
    public static void Shadow(Rect r, float spread, float alpha, float offsetY = 0)
    {
        if (!Repaint)
        {
            return;
        }

        var saved = GUI.color;
        GUI.color = new Color(0, 0, 0, alpha * Alpha);
        var area = new Rect(r.x - spread, r.y - spread + offsetY, r.width + (spread * 2), r.height + (spread * 2));
        _shadow.Draw(area, false, false, false, false);
        GUI.color = saved;
    }

    /// <summary>四根彩色竖条的标志（和编辑器、桌面图标一样）。bounce 0–1：竖条按节奏轻轻起伏。</summary>
    public static void Logo(Rect r, float bounce = 0)
    {
        var heights = new[] { 0.5f, 0.82f, 1f, 0.64f };
        var colors = new[] { Accent, Blue, Accent, Purple };
        var bar = r.width / 5.2f;
        var gap = (r.width - (bar * 4)) / 3;
        var now = Time.unscaledTime;
        for (var i = 0; i < 4; i++)
        {
            var wave = bounce > 0 ? 1 - (bounce * 0.28f * (0.5f + (0.5f * Mathf.Sin((now * 3.9f) - (i * 1.6f))))) : 1;
            var h = r.height * heights[i] * wave;
            Fill(new Rect(r.x + (i * (bar + gap)), r.yMax - h, bar, h), colors[i], bar * 0.4f);
        }
    }

    // ---------- 图标 ----------

    /// <summary>画一个线条图标（白色 PNG 染成 c），名字见 SettingsIcons/icons.svg。</summary>
    public static void Icon(Rect r, string name, Color c)
    {
        if (!Repaint)
        {
            return;
        }

        if (!Icons.TryGetValue(name, out var texture))
        {
            texture = EmbeddedImage.Load(IconPrefix + name + ".png");
            if (texture == null)
            {
                Main.Mod.Logger.Warning($"设置窗口图标缺失：{name}");
            }

            Icons[name] = texture;
        }

        if (texture != null)
        {
            GUI.DrawTexture(r, texture, ScaleMode.ScaleToFit, true, 0f, Fade(c), 0f, 0f);
        }
    }

    /// <summary>
    /// 转一个角度画图标（折叠箭头展开时翻过来）。
    /// 不能直接调 RotateAroundPivot：它在没缩放的坐标里找圆心，却把旋转套在窗口的缩放（GUI.matrix）外面，
    /// 窗口按屏幕高度放大后圆心就错了，转 180° 时箭头飞到窗口左上角（2026-11-19 用户截图，1600 高的屏幕放大约 1.48 倍）。
    /// 这里先在单位矩阵下让它算出没缩放坐标里的旋转，再乘到窗口矩阵里面：先转、后缩放，圆心就是图标中心。
    /// </summary>
    public static void Icon(Vector2 center, float size, string name, Color c, float degrees)
    {
        if (!Repaint)
        {
            return;
        }

        var saved = GUI.matrix;
        GUI.matrix = Matrix4x4.identity;
        GUIUtility.RotateAroundPivot(degrees, center);
        GUI.matrix = saved * GUI.matrix;
        Icon(center, size, name, c);
        GUI.matrix = saved;
    }

    /// <summary>以 center 为中心画 size 大小的图标。</summary>
    public static void Icon(Vector2 center, float size, string name, Color c) =>
        Icon(new Rect(center.x - (size / 2), center.y - (size / 2), size, size), name, c);

    // ---------- 颜色 ----------

    public static Color Fade(Color c) => new(c.r, c.g, c.b, c.a * Alpha);

    public static Color With(Color c, float alpha) => new(c.r, c.g, c.b, alpha);

    public static Color Mix(Color a, Color b, float t) => Color.Lerp(a, b, Mathf.Clamp01(t));

    private static Color Hex(int rgb) => new(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, 1f);

    private static float Radius(Rect r, float radius) => Mathf.Min(radius, Mathf.Min(r.width, r.height) / 2);

    // ---------- 贴图 ----------

    /// <summary>阴影贴图九宫格的边宽（像素）：也是阴影淡出的宽度。</summary>
    private const int ShadowEdge = 30;

    /// <summary>64×64 的阴影：中间 4 像素实，往四边按平滑曲线淡出；九宫格拉伸到任意大小，四角是圆的。</summary>
    private static Texture2D ShadowTexture()
    {
        const int size = 64;
        const float half = 32;
        const float core = 2;
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave,
        };
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var d = new Vector2(Mathf.Max(0, Mathf.Abs(x + 0.5f - half) - core), Mathf.Max(0, Mathf.Abs(y + 0.5f - half) - core)).magnitude;
                var a = 1 - Mathf.Clamp01(d / ShadowEdge);
                t.SetPixel(x, y, new Color(1, 1, 1, a * a * a));
            }
        }

        t.Apply();
        return t;
    }

    /// <summary>64×64 的圆形柔光：中心不透明，到边缘按平方曲线透明。</summary>
    private static Texture2D GlowTexture()
    {
        const int size = 64;
        const float half = (size - 1) / 2f;
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave,
        };
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var a = 1f - Mathf.Clamp01(new Vector2(x - half, y - half).magnitude / half);
                t.SetPixel(x, y, new Color(1, 1, 1, a * a));
            }
        }

        t.Apply();
        return t;
    }
}
