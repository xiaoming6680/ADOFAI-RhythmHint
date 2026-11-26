using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RhythmHint.Core;

/// <summary>一块砖在轨道上的位置（编辑器画轨道形状用）。</summary>
/// <param name="Tile">砖号（scrFloor.seqID），和 TileHit.TileIndex 对得上。</param>
/// <param name="Entry">进方向（指向上一块砖），数学角、弧度（见 PathAngles）；不知道为 NaN。</param>
/// <param name="Exit">出方向（指向下一块砖）；不知道为 NaN。</param>
public readonly record struct PathPoint(int Tile, double X, double Y, double Time, bool Hit, double Entry = double.NaN, double Exit = double.NaN);

/// <summary>编辑器能放的音乐：文件（相对编辑器目录）、时间换算、波形。</summary>
public sealed class SessionAudio
{
    /// <summary>相对编辑器目录的文件，比如 recent/968-…-20261118-093000-123.ogg（<see cref="SessionLibrary.AudioFile"/>）。</summary>
    public string File { get; set; } = string.Empty;

    /// <summary>
    /// 同一首歌另存成的脚本（<see cref="AudioScript"/>，比如 recent/968-…-123.ogg.js），编辑器整首解码后播；
    /// 没有为空（官方关卡的 WAV、太大的歌曲），编辑器直接用 &lt;audio&gt; 播 <see cref="File"/>。
    /// </summary>
    public string Script { get; set; } = string.Empty;

    /// <summary>音乐文件里的位置 = 砖块时间 + Offset；二者都在原始歌曲时间轴上。</summary>
    public double Offset { get; set; }

    /// <summary>关卡本身的播放倍率（100% = 1）；另乘编辑器里选择的练习速度。</summary>
    public double Pitch { get; set; } = 1;

    /// <summary>
    /// 游戏里的画面校准（秒，scrConductor.calibration_v）：游戏画面用的时间 = 音乐位置 − 偏移 − 画面校准，
    /// 玩家靠它补声音输出的延迟。编辑器照样补，游戏里调好的同步编辑器里也对。
    /// </summary>
    public double Calibration { get; set; }

    /// <summary>波形：每秒多少个点、每个点 0–255 的音量峰值；没有为空。</summary>
    public int PeaksPerSecond { get; set; }

    public byte[] Peaks { get; set; } = Array.Empty<byte>();
}

/// <summary>
/// 同一关换一种键数时编辑器要的东西（2026-11-22 用户：编辑器里能切换 4K、6K、8K）：这个键数的手序（专属手序或自动排键）、
/// 保存用的文件名、键位、两种自动排法。轨道、音乐、节拍线这些和键数无关的放在 <see cref="EditorSession"/> 上，几种键数共用。
/// </summary>
public sealed class SessionVariant
{
    /// <summary>这个键数在编辑器历史里的 Id（<see cref="SessionLibrary.IdOf"/>，6K、8K 带「-6k」「-8k」），草稿按它存。</summary>
    public string Id { get; set; } = string.Empty;

    public CustomFingering Fingering { get; set; } = new();

    public IReadOnlyList<bool> FromAuto { get; set; } = Array.Empty<bool>();

    public string UsedFile { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public IReadOnlyList<string> KeyLabels { get; set; } = Array.Empty<string>();

    public IReadOnlyList<PlannedNote> Standard { get; set; } = Array.Empty<PlannedNote>();

    public IReadOnlyList<PlannedNote> Alternate { get; set; } = Array.Empty<PlannedNote>();

    /// <summary>双手稳拍（试验性）；空则不写，编辑器不显示这一栏。</summary>
    public IReadOnlyList<PlannedNote> Steady { get; set; } = Array.Empty<PlannedNote>();
}

/// <summary>
/// 「编辑本关手序」交给编辑器的一份数据：Mod 写成编辑器目录下的 session.js（file:// 打开的网页不能读别的文件，
/// 但能用 &lt;script&gt; 载入同目录的脚本），编辑器打开时自动载入。除了本关的手序，还带上改手序时用来参考的东西：
/// 两种自动排法、节拍线、轨道形状、音乐和波形。
/// </summary>
public sealed class EditorSession
{
    public const string Format = "rhythmhint-editor-session";

    /// <summary>这一关在编辑器历史里的 Id（<see cref="SessionLibrary.IdOf"/>）；编辑器按它记住改到一半的进度。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>当前这一关的手序（砖块是本关的，音符是游戏里正在用的：专属手序或自动排键）。</summary>
    public CustomFingering Fingering { get; set; } = new();

    /// <summary>和音符一一对应：是不是自动排键补上的（专属手序对不上的地方）。空表示整首都是自动排键或都是专属手序。</summary>
    public IReadOnlyList<bool> FromAuto { get; set; } = Array.Empty<bool>();

    /// <summary>游戏里正在用的专属手序文件名；没有为空。保存时默认覆盖它。</summary>
    public string UsedFile { get; set; } = string.Empty;

    /// <summary>保存用的文件名（UsedFile 或按曲名起的名字）。</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 编辑器保存到哪里（完整路径，编辑器提示玩家选这个文件夹）：通常是游戏的专属手序文件夹；
    /// 游戏装在浏览器不让写的地方时是文档里的中转文件夹（<see cref="FingeringInbox"/>）。
    /// </summary>
    public string SaveFolder { get; set; } = string.Empty;

    /// <summary>选文件夹时从哪打开（浏览器认的名字，比如 documents）；空为浏览器默认。</summary>
    public string SaveStartIn { get; set; } = string.Empty;

    /// <summary>编辑器的界面语言（和 Mod 一样的代码）。</summary>
    public string Language { get; set; } = Languages.Source;

    /// <summary>导出这份会话的 Mod 版本（比如 1.3.0-dev），编辑器「关于」里显示；离线工具写的为空。</summary>
    public string ModVersion { get; set; } = string.Empty;

    /// <summary>游戏里四轨音符的样式：圆形（和太鼓条一样的球）还是横条。编辑器照着画，和游戏里一模一样。</summary>
    public bool RoundNotes { get; set; }

    public IReadOnlyList<string> KeyLabels { get; set; } = new[] { "D", "F", "J", "K" };

    /// <summary>两种自动排法（标准、交替优先）各排一遍，做对照、整段套用。</summary>
    public IReadOnlyList<PlannedNote> Standard { get; set; } = Array.Empty<PlannedNote>();

    public IReadOnlyList<PlannedNote> Alternate { get; set; } = Array.Empty<PlannedNote>();

    public IReadOnlyList<PlannedNote> Steady { get; set; } = Array.Empty<PlannedNote>();

    public IReadOnlyList<BeatLine> Beats { get; set; } = Array.Empty<BeatLine>();

    /// <summary>BPM 变化点：时间、BPM。</summary>
    public IReadOnlyList<(double Time, double Bpm)> Tempo { get; set; } = Array.Empty<(double, double)>();

    public IReadOnlyList<PathPoint> Path { get; set; } = Array.Empty<PathPoint>();

    public SessionAudio? Audio { get; set; }

    /// <summary>让轨道和镜头动起来的关卡事件和相关设置（TrackEffects.Extract）；没有为 null。</summary>
    public JObject? Effects { get; set; }

    /// <summary>编辑器的打拍声：这一关游戏里的打击声（WAV）。读不到为空，编辑器用合成的声音代替。</summary>
    public byte[] HitSound { get; set; } = Array.Empty<byte>();

    /// <summary>同一关的其他键数（编辑器里切换用）；上面的是游戏里正在显示的键数。旧版编辑器不认这个字段，照旧只打开上面那个。</summary>
    public IReadOnlyList<SessionVariant> Variants { get; set; } = Array.Empty<SessionVariant>();

    /// <summary>写成 <c>window.RHYTHMHINT_SESSION = {…};</c>。</summary>
    public string ToScript()
    {
        var sw = new StringWriter(CultureInfo.InvariantCulture);
        using (var w = new JsonTextWriter(sw) { Formatting = Formatting.None })
        {
            w.WriteStartObject();
            Prop(w, "format", Format);
            Prop(w, "id", Id);
            Prop(w, "language", Language);
            Prop(w, "modVersion", ModVersion);
            w.WritePropertyName("roundNotes");
            w.WriteValue(RoundNotes);
            Prop(w, "saveFolder", SaveFolder);
            Prop(w, "saveStartIn", SaveStartIn);
            WriteKeyed(w, new SessionVariant
            {
                Fingering = Fingering,
                FromAuto = FromAuto,
                UsedFile = UsedFile,
                FileName = FileName,
                KeyLabels = KeyLabels,
                Standard = Standard,
                Alternate = Alternate,
                Steady = Steady,
            });

            w.WritePropertyName("beats");
            w.WriteStartArray();
            foreach (var b in Beats)
            {
                w.WriteStartArray();
                w.WriteValue(Math.Round(b.Time, 6));
                w.WriteValue(b.IsMeasure ? 1 : 0);
                w.WriteEndArray();
            }

            w.WriteEndArray();

            w.WritePropertyName("tempo");
            w.WriteStartArray();
            foreach (var (time, bpm) in Tempo)
            {
                w.WriteStartArray();
                w.WriteValue(Math.Round(time, 6));
                w.WriteValue(Math.Round(bpm, 3));
                w.WriteEndArray();
            }

            w.WriteEndArray();

            w.WritePropertyName("path");
            w.WriteStartArray();
            foreach (var p in Path)
            {
                w.WriteStartArray();
                w.WriteValue(Math.Round(p.X, 4));
                w.WriteValue(Math.Round(p.Y, 4));
                w.WriteValue(Math.Round(p.Time, 6));
                w.WriteValue(p.Hit ? 1 : 0);
                w.WriteValue(p.Tile);
                if (!double.IsNaN(p.Entry) && !double.IsNaN(p.Exit))
                {
                    // 进、出方向（数学角，弧度，见 PathAngles）：轨道预览按它画每块砖，被挪开的砖也和游戏里一样
                    w.WriteValue(Math.Round(p.Entry, 4));
                    w.WriteValue(Math.Round(p.Exit, 4));
                }

                w.WriteEndArray();
            }

            w.WriteEndArray();

            w.WritePropertyName("audio");
            if (Audio == null)
            {
                w.WriteNull();
            }
            else
            {
                w.WriteStartObject();
                Prop(w, "file", Audio.File);
                Prop(w, "script", Audio.Script);
                w.WritePropertyName("offset");
                w.WriteValue(Math.Round(Audio.Offset, 6));
                w.WritePropertyName("pitch");
                w.WriteValue(Math.Round(Audio.Pitch, 6));
                w.WritePropertyName("calibration");
                w.WriteValue(Math.Round(Audio.Calibration, 6));
                w.WritePropertyName("peaksPerSecond");
                w.WriteValue(Audio.PeaksPerSecond);
                Prop(w, "peaks", Convert.ToBase64String(Audio.Peaks));
                w.WriteEndObject();
            }

            w.WritePropertyName("fx");
            if (Effects == null)
            {
                w.WriteNull();
            }
            else
            {
                Effects.WriteTo(w);
            }

            // 打拍声直接放进脚本（file:// 打开的网页读不了旁边的音频文件来解码）
            w.WritePropertyName("hitSound");
            if (HitSound.Length == 0)
            {
                w.WriteNull();
            }
            else
            {
                w.WriteValue(Convert.ToBase64String(HitSound));
            }

            if (Variants.Count > 0)
            {
                w.WritePropertyName("variants");
                w.WriteStartArray();
                foreach (var v in Variants)
                {
                    w.WriteStartObject();
                    Prop(w, "id", v.Id);
                    WriteKeyed(w, v);
                    w.WriteEndObject();
                }

                w.WriteEndArray();
            }

            w.WriteEndObject();
        }

        return "window.RHYTHMHINT_SESSION = " + sw + ";\n";
    }

    /// <summary>和键数有关的那些字段（会话本身和 <see cref="Variants"/> 里每一种键数写法一样）。</summary>
    private static void WriteKeyed(JsonWriter w, SessionVariant v)
    {
        Prop(w, "usedFile", v.UsedFile);
        Prop(w, "fileName", v.FileName);
        w.WritePropertyName("keyLabels");
        w.WriteStartArray();
        foreach (var label in v.KeyLabels)
        {
            w.WriteValue(label);
        }

        w.WriteEndArray();

        w.WritePropertyName("fingering");
        w.WriteStartObject();
        v.Fingering.WriteHeader(w);
        CustomFingering.WriteHitsAndNotes(w, v.Fingering.Hits, v.Fingering.Notes);
        w.WriteEndObject();

        w.WritePropertyName("fromAuto");
        w.WriteStartArray();
        foreach (var auto in v.FromAuto)
        {
            w.WriteValue(auto ? 1 : 0);
        }

        w.WriteEndArray();

        w.WritePropertyName("references");
        w.WriteStartObject();
        CustomFingering.WriteNotes(w, "standard", v.Standard);
        CustomFingering.WriteNotes(w, "alternate", v.Alternate);
        if (v.Steady.Count > 0) CustomFingering.WriteNotes(w, "steady", v.Steady);
        w.WriteEndObject();
    }

    private static void Prop(JsonWriter w, string name, string value)
    {
        w.WritePropertyName(name);
        w.WriteValue(value ?? string.Empty);
    }
}
