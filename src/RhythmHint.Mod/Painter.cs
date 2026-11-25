using RhythmHint.Core;
using UnityEngine;

namespace RhythmHint;

/// <summary>IMGUI 画图的小工具：圆角矩形、圆、描边、渐变、文字。只能在 OnGUI 里调用。</summary>
internal static class Painter
{
    private static readonly GUIContent Content = new();
    private static GUIStyle? _text;
    private static Texture2D? _fadeUp;
    private static Texture2D? _glow;

    public static readonly Color Orange = new(0.96f, 0.47f, 0.16f);
    public static readonly Color Blue = new(0.24f, 0.62f, 0.88f);

    /// <summary>三押、四押（用户要一个新颜色）：色相和橙、蓝都离得远，深色底上和它们一样亮。</summary>
    public static readonly Color Purple = new(0.70f, 0.45f, 0.95f);
    public static readonly Color PanelColor = new(0.07f, 0.07f, 0.08f);
    public static readonly Color BpmBoxColor = new(0.11f, 0.30f, 0.58f);

    /// <summary>音符颜色：单押橙、双押蓝、三押和四押紫。</summary>
    public static Color NoteColor(PlannedNote note) => note.IsMultiChord ? Purple : note.IsChord ? Blue : Orange;

    /// <summary>整体透明度（入场 / 出场动画用），乘到每次绘制的颜色上。</summary>
    public static float Alpha { get; set; } = 1f;

    public static void Rect(Rect r, Color c, float radius = 0f)
    {
        GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, Fade(c), 0f, radius);
    }

    public static void Border(Rect r, Color c, float width, float radius = 0f)
    {
        GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, Fade(c), width, radius);
    }

    /// <summary>下面实、往上渐渐透明的矩形（打击闪光用）。</summary>
    public static void FadeUp(Rect r, Color c)
    {
        GUI.DrawTexture(r, FadeUpTexture(), ScaleMode.StretchToFill, true, 0f, Fade(c), 0f, 0f);
    }

    /// <summary>圆形柔光：中心最亮、往外按平方曲线淡出（圆形判定圈的命中光晕）。</summary>
    public static void Glow(Vector2 center, float radius, Color c)
    {
        var r = new Rect(center.x - radius, center.y - radius, radius * 2, radius * 2);
        GUI.DrawTexture(r, GlowTexture(), ScaleMode.StretchToFill, true, 0f, Fade(c), 0f, 0f);
    }

    public static void Circle(Vector2 center, float radius, Color c)
    {
        Rect(new Rect(center.x - radius, center.y - radius, radius * 2, radius * 2), c, radius);
    }

    public static void Ring(Vector2 center, float radius, Color c, float width)
    {
        Border(new Rect(center.x - radius, center.y - radius, radius * 2, radius * 2), c, width, radius);
    }

    public static void Text(Rect r, string text, int size, Color c, TextAnchor anchor = TextAnchor.MiddleCenter, bool bold = false)
    {
        var style = TextStyle(size, bold);
        style.alignment = anchor;
        style.normal.textColor = Fade(c);
        GUI.Label(r, text, style);
    }

    /// <summary>
    /// 一行字，太宽时缩小字号放进 r：按键框里的 LShift、Space 这类长名字（居中），
    /// 演示说明换成英文、韩文后比中文长得多（靠右）。
    /// </summary>
    public static void FitText(Rect r, string text, int size, Color c, bool bold = false, TextAnchor anchor = TextAnchor.MiddleCenter)
    {
        Content.text = text;
        var width = TextStyle(size, bold).CalcSize(Content).x;
        var room = r.width * 0.9f;
        if (width > room)
        {
            size = Mathf.FloorToInt(size * room / width);
        }

        Text(r, text, size, c, anchor, bold);
    }

    /// <summary>一行字多宽（像素）。</summary>
    public static float TextWidth(string text, int size, bool bold = false)
    {
        Content.text = text;
        return TextStyle(size, bold).CalcSize(Content).x;
    }

    public static Color WithAlpha(this Color c, float a) => new(c.r, c.g, c.b, a);

    private static Color Fade(Color c) => new(c.r, c.g, c.b, c.a * Alpha);

    private static GUIStyle TextStyle(int size, bool bold)
    {
        _text ??= new GUIStyle(GUI.skin.label) { wordWrap = false, clipping = TextClipping.Overflow };
        _text.fontSize = Mathf.Max(1, size);
        _text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        return _text;
    }

    /// <summary>64×64 的圆形渐变贴图：圆心不透明，到边缘按平方曲线透明。</summary>
    private static Texture2D GlowTexture()
    {
        if (_glow != null)
        {
            return _glow;
        }

        const int size = 64;
        const float half = (size - 1) / 2f;
        _glow = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var a = 1f - Mathf.Clamp01(new Vector2(x - half, y - half).magnitude / half);
                _glow.SetPixel(x, y, new Color(1, 1, 1, a * a));
            }
        }

        _glow.Apply();
        return _glow;
    }

    /// <summary>1×64 的竖向渐变贴图：底部不透明，往上按平方曲线淡出。Unity 贴图第 0 行在底部。</summary>
    private static Texture2D FadeUpTexture()
    {
        if (_fadeUp != null)
        {
            return _fadeUp;
        }

        const int h = 64;
        _fadeUp = new Texture2D(1, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (var y = 0; y < h; y++)
        {
            var a = 1f - (y / (float)(h - 1));
            _fadeUp.SetPixel(0, y, new Color(1, 1, 1, a * a));
        }

        _fadeUp.Apply();
        return _fadeUp;
    }
}
