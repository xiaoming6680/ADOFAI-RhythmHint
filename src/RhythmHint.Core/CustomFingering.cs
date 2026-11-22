using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RhythmHint.Core;

/// <summary>
/// 玩家给某首歌做的专属手序，一首歌一份文件，可以分享给别人。
/// 记下做手序时这一关的砖块（节奏）和每个音符按哪几个键；关卡更新以后靠节奏对齐找回对应的砖（见 <see cref="FingeringMatcher"/>），
/// 不要求文件和关卡完全一致。文件里不放音乐（版权）。
/// </summary>
public sealed class CustomFingering
{
    public const string Format = "rhythmhint-fingering";
    public const int SchemaVersion = 1;

    /// <summary>超过这个大小就不是正常的手序文件（四千个音符约 150 KB），不读。</summary>
    public const int MaxFileBytes = 20 * 1024 * 1024;

    /// <summary>键数最多这么多（以后的六键、八键都在里面）。</summary>
    public const int MaxKeys = 16;

    private const int MaxHits = FingeringReader.MaxHits;
    private const int MaxNameLength = 300;
    private const int MaxCommentLength = 4000;

    /// <summary>建议的文件名（不含 .json）最多这么多个 UTF-16 字符。</summary>
    private const int MaxFileNameLength = 80;

    /// <summary>第 1 版定下的顶层字段；别的都是以后的版本加的（<see cref="Extra"/>）。</summary>
    private static readonly HashSet<string> KnownFields = new(StringComparer.Ordinal)
    {
        "format", "schemaVersion", "keys", "song", "artist", "charter", "author", "comment", "leftHanded", "charts", "updatedAt", "hits", "notes",
    };

    /// <summary>Windows 的保留设备名：后面带扩展名也不能当文件名（NUL.json 等于 NUL）。</summary>
    private static readonly Regex ReservedName = new(@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])\s*(\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>曲名、曲师、谱师：显示用，也用来认「同一首歌」。</summary>
    public string Song { get; set; } = string.Empty;

    public string Artist { get; set; } = string.Empty;

    public string Charter { get; set; } = string.Empty;

    /// <summary>这份手序的作者（编辑器里填）。</summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>作者写的说明。</summary>
    public string Comment { get; set; } = string.Empty;

    /// <summary>文件里没写 keys 时当几键（第 1 版定下的，不随 Mod 支持的键数变）。</summary>
    public const int DefaultKeys = 4;

    /// <summary>
    /// 几键（4、6、8，文件里可以是 1–16）。左边一半是左手，每只手从中间往外是食指、中指、无名指、小指（<see cref="KeyLayout"/>）。
    /// 键数和这一关显示的不同时怎么套用见 <see cref="FingeringMatcher"/>。
    /// </summary>
    public int Keys { get; set; } = DefaultKeys;

    /// <summary>这份手序按哪只手为主手排的；玩家的惯用手不同时整体镜像。</summary>
    public bool LeftHanded { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 这份手序认得的谱面指纹（<see cref="ChartPrint"/>）：做手序时那一版，以及作者后来为更新版重新保存时的各版。
    /// 进关卡时指纹在这里就直接用，不用对齐。
    /// </summary>
    public List<string> Charts { get; } = new();

    /// <summary>做手序时这一关要按的砖，按时间排好。</summary>
    public List<TileHit> Hits { get; } = new();

    /// <summary>每个音符按哪几个键。音符按顺序、不重不漏地覆盖 <see cref="Hits"/>；时间取自它的第一块砖。</summary>
    public List<PlannedNote> Notes { get; } = new();

    /// <summary>
    /// 不认识的顶层字段（以后的版本加的、别的工具写的）：读的时候原样留下，写的时候跟在已知字段后面、hits 前面写回去，
    /// 用旧版 Mod 重新导出、编辑器再保存也不丢（2026-11-19 评审 A 高1：1.3.x 会丢）。和已知字段同名的不写。
    /// </summary>
    public JObject Extra { get; } = new();

    /// <summary>读的时候太大没留下的不认识字段（写日志用）：这些字段保存时会丢掉，手序本身照样能用。</summary>
    public List<string> DroppedFields { get; } = new();

    /// <summary>是不是第 1 版定下的顶层字段。</summary>
    public static bool IsKnownField(string name) => KnownFields.Contains(name);

    /// <summary>
    /// 为本关重新生成一份手序（编辑本关手序）时，从正在用的那份文件带上玩家写的东西：作者、说明、认得的谱面指纹（旧版的也留着）、
    /// 不认识的顶层字段（编辑器原样读、原样写回）。
    /// </summary>
    public void TakeDetailsFrom(CustomFingering used)
    {
        Author = used.Author;
        Comment = used.Comment;
        foreach (var chart in used.Charts)
        {
            if (!Charts.Contains(chart))
            {
                Charts.Add(chart);
            }
        }

        foreach (var p in used.Extra.Properties())
        {
            Extra[p.Name] = p.Value.DeepClone();
        }
    }

    /// <summary>从当前关卡的砖块和排好的音符生成一份手序（导出给编辑器、测试用）。</summary>
    /// <param name="keys">音符是几键排的（检查轨道前要先定好，6K 的轨道 4、5 在 4 键文件里不合法）。</param>
    public static CustomFingering From(IReadOnlyList<TileHit> hits, IReadOnlyList<PlannedNote> notes, bool leftHanded, int keys = DefaultKeys)
    {
        var result = new CustomFingering { LeftHanded = leftHanded, Keys = keys };
        result.Hits.AddRange(hits);
        foreach (var note in notes)
        {
            result.Notes.Add(note.Clone());
        }

        Check(result);
        result.Charts.Add(ChartPrint(hits));
        return result;
    }

    /// <summary>
    /// 谱面指纹：每块要按的砖「和前一块的间隔（毫秒）、转角（度）、有没有长按」连起来取哈希。
    /// 只用相邻两块的时间差，和砖号、文件名、曲名都无关：换文件夹、换电脑不变；谱面改了任何一块要按的砖就变。
    /// 浮点误差让整体平移后不一定不变（真实谱整体平移 0.4 秒，866 张里 82 张变了，2026-11-19 复审），对不上时还有节奏对齐。
    /// </summary>
    public static string ChartPrint(IReadOnlyList<TileHit> hits)
    {
        var sb = new StringBuilder(hits.Count * 12);
        for (var i = 0; i < hits.Count; i++)
        {
            var gap = i == 0 ? 0 : Math.Round((hits[i].Time - hits[i - 1].Time) * 1000);
            var angle = double.IsNaN(hits[i].Angle) ? "-" : Math.Round(hits[i].Angle).ToString(CultureInfo.InvariantCulture);
            sb.Append(gap.ToString(CultureInfo.InvariantCulture)).Append(',').Append(angle).Append(',').Append(hits[i].HoldSeconds > 0 ? '1' : '0').Append(';');
        }

        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        return hits.Count.ToString(CultureInfo.InvariantCulture) + "-" + BitConverter.ToString(bytes, 0, 12).Replace("-", string.Empty).ToLowerInvariant();
    }

    /// <summary>
    /// 建议的文件名：「曲师 - 曲名.json」，在 Windows 上一定能用（编辑器 model.js 的 suggestedFileName 是同一规则）：
    /// 去掉富文本标签和首尾空白；去掉控制字符（U+0000–U+001F、U+007F–U+009F）；\ / : * ? " &lt; &gt; | 换成 _；
    /// 去掉首尾空白和结尾的点，空了就叫 fingering；第一个点之前（不算结尾的空格）是 CON、PRN、AUX、NUL、COM1–9、LPT1–9 时前面加 _；
    /// 超过 80 个 UTF-16 字符截到 80，第 80 个是代理对的前一半时截到 79（不把 emoji 切成两半）。
    /// 不是 4 键的在后面加「 6K」「 8K」（同一首歌的 4K、6K 手序默认名不撞，2026-11-22）。
    /// 复现（2026-11-19 评审 A 低12）：原来按 Path.GetInvalidFileNameChars（Mac 上只有 / 和 \0），CON 照用，截断会切开 emoji。
    /// </summary>
    public string SuggestedFileName()
    {
        var song = PlainText(Song);
        var artist = PlainText(Artist);
        var name = artist.Length > 0 && song.Length > 0 ? $"{artist} - {song}" : song.Length > 0 ? song : "fingering";
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (char.IsControl(c))
            {
                continue;
            }

            sb.Append(c is '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|' ? '_' : c);
        }

        var safe = sb.ToString().Trim().TrimEnd('.');
        if (safe.Length == 0)
        {
            safe = "fingering";
        }

        if (ReservedName.IsMatch(safe))
        {
            safe = "_" + safe;
        }

        if (safe.Length > MaxFileNameLength)
        {
            safe = safe.Substring(0, char.IsHighSurrogate(safe[MaxFileNameLength - 1]) ? MaxFileNameLength - 1 : MaxFileNameLength);
        }

        return safe + (Keys != DefaultKeys ? $" {Keys}K" : string.Empty) + ".json";
    }

    /// <summary>去掉游戏曲名里的富文本标签（&lt;color=…&gt; 之类）和首尾空白。</summary>
    public static string PlainText(string text) => Regex.Replace(text ?? string.Empty, "<[^>]*>", string.Empty).Trim();

    /// <summary>认「同一首歌」用：去掉标签、空白，不分大小写。</summary>
    public static string SongKey(string song, string artist) =>
        Regex.Replace(PlainText(artist) + "|" + PlainText(song), @"\s+", string.Empty).ToLowerInvariant();

    public string ToJson()
    {
        Check(this);
        var sw = new StringWriter(CultureInfo.InvariantCulture);
        using (var w = new JsonTextWriter(sw) { Formatting = Formatting.None })
        {
            w.WriteStartObject();
            WriteHeader(w);
            WriteHitsAndNotes(w, Hits, Notes);
            w.WriteEndObject();
        }

        return sw.ToString();
    }

    /// <summary>格式、版本、作者等字段（编辑器会话也用这一段）。</summary>
    internal void WriteHeader(JsonWriter w)
    {
        w.WritePropertyName("format");
        w.WriteValue(Format);
        w.WritePropertyName("schemaVersion");
        w.WriteValue(SchemaVersion);
        w.WritePropertyName("keys");
        w.WriteValue(Keys);
        Text(w, "song", Song);
        Text(w, "artist", Artist);
        Text(w, "charter", Charter);
        Text(w, "author", Author);
        Text(w, "comment", Comment);
        w.WritePropertyName("leftHanded");
        w.WriteValue(LeftHanded);
        w.WritePropertyName("charts");
        w.WriteStartArray();
        foreach (var chart in Charts)
        {
            w.WriteValue(chart);
        }

        w.WriteEndArray();
        w.WritePropertyName("updatedAt");
        w.WriteValue(UpdatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        foreach (var p in Extra.Properties())
        {
            if (!IsKnownField(p.Name))
            {
                w.WritePropertyName(p.Name);
                p.Value.WriteTo(w);
            }
        }
    }

    /// <summary>hits：[砖号, 时间, 长按, 转角或 null]；notes：[第一块砖号, 最后一块砖号, [[轨道, 长按], …]]。一项一行，方便看 diff。</summary>
    internal static void WriteHitsAndNotes(JsonWriter w, IReadOnlyList<TileHit> hits, IReadOnlyList<PlannedNote> notes)
    {
        w.WritePropertyName("hits");
        w.WriteStartArray();
        foreach (var h in hits)
        {
            w.WriteWhitespace("\n");
            w.WriteStartArray();
            w.WriteValue(h.TileIndex);
            w.WriteValue(Round(h.Time));
            w.WriteValue(Round(h.HoldSeconds));
            if (double.IsNaN(h.Angle))
            {
                w.WriteNull();
            }
            else
            {
                w.WriteValue(Math.Round(h.Angle, 3));
            }

            w.WriteEndArray();
        }

        w.WriteEndArray();
        WriteNotes(w, "notes", notes);
    }

    internal static void WriteNotes(JsonWriter w, string property, IReadOnlyList<PlannedNote> notes)
    {
        w.WritePropertyName(property);
        w.WriteStartArray();
        foreach (var n in notes)
        {
            w.WriteWhitespace("\n");
            w.WriteStartArray();
            w.WriteValue(n.TileIndex);
            w.WriteValue(n.LastTileIndex);
            w.WriteStartArray();
            foreach (var kv in n.Lanes)
            {
                w.WriteStartArray();
                w.WriteValue(kv.Key);
                w.WriteValue(Round(kv.Value));
                w.WriteEndArray();
            }

            w.WriteEndArray();
            w.WriteEndArray();
        }

        w.WriteEndArray();
    }

    /// <summary>
    /// 读一份手序文件。格式不对时抛 <see cref="FormatException"/>，消息说明哪里不对（写日志用）。
    /// 不把像日期的文字自动转成日期（作者、说明恰好写成日期时间时原来整份拒绝）；怎么读见 <see cref="FingeringReader"/>。
    /// </summary>
    public static CustomFingering Parse(string json)
    {
        try
        {
            return Read(FingeringReader.Load(json));
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException or InvalidCastException)
        {
            // Newtonsoft 强转类型不对的值时抛这几种（数组当文字、超出 long 的整数）：一样只算这份文件格式不对，
            // 不能漏到外面（FingeringSource 只跳过 FormatException 这类，漏出去会让整个 Mod 停用）
            throw new FormatException("字段类型不对或数字太大：" + ex.Message, ex);
        }
    }

    private static CustomFingering Read(FingeringReader.RawFile raw)
    {
        var result = ReadHeader(raw.Header);
        ReadHits(raw, result);
        ReadNotes(raw, result);
        Check(result);
        foreach (var chart in raw.Charts)
        {
            result.Charts.Add(chart); // 读的时候已经去掉了重复的、不是文字的、超过 100 个字的
        }

        // 旧文件没有 charts：至少认得它自己记下的那一版
        var own = ChartPrint(result.Hits);
        if (!result.Charts.Contains(own))
        {
            result.Charts.Add(own);
        }

        foreach (var p in raw.Header.Properties())
        {
            if (!IsKnownField(p.Name))
            {
                result.Extra[p.Name] = p.Value.DeepClone();
            }
        }

        result.DroppedFields.AddRange(raw.Dropped);
        return result;
    }

    private static CustomFingering ReadHeader(JObject root)
    {
        if ((string?)root["format"] != Format)
        {
            throw new FormatException("不是 RhythmHint 专属手序文件（format 不是 " + Format + "）");
        }

        var version = Int(root["schemaVersion"], "schemaVersion");
        if (version > SchemaVersion)
        {
            throw new FormatException($"文件版本 {version} 比这个 Mod 支持的 {SchemaVersion} 新，请更新 Mod");
        }

        if (version < 1)
        {
            throw new FormatException("schemaVersion 无效");
        }

        var result = new CustomFingering
        {
            Keys = root["keys"] == null ? DefaultKeys : Int(root["keys"], "keys"),
            Song = Str(root, "song", MaxNameLength),
            Artist = Str(root, "artist", MaxNameLength),
            Charter = Str(root, "charter", MaxNameLength),
            Author = Str(root, "author", MaxNameLength),
            Comment = Str(root, "comment", MaxCommentLength),
            LeftHanded = root["leftHanded"]?.Type == JTokenType.Boolean && (bool)root["leftHanded"]!,
            UpdatedAt = DateTimeOffset.TryParse((string?)root["updatedAt"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
                ? at : DateTimeOffset.MinValue,
        };
        if (result.Keys < 1 || result.Keys > MaxKeys)
        {
            throw new FormatException($"keys 必须是 1–{MaxKeys}");
        }

        return result;
    }

    private static void ReadHits(FingeringReader.RawFile raw, CustomFingering result)
    {
        if (raw.Hits == null || raw.HitCount == 0 || raw.HitCount > MaxHits)
        {
            throw new FormatException($"hits 必须是 1–{MaxHits} 项的数组");
        }

        foreach (var h in raw.Hits)
        {
            if (!h.IsArray || h.Count < 3)
            {
                throw new FormatException("hits 的每一项必须是 [砖号, 时间, 长按, 转角]");
            }

            var angle = h.Count > 3 && !h.D.IsNull ? Num(h.D, "转角") : double.NaN;
            result.Hits.Add(new TileHit(Int(h.A, "砖号"), Num(h.B, "时间"), Num(h.C, "长按"), angle));
        }
    }

    private static void ReadNotes(FingeringReader.RawFile raw, CustomFingering result)
    {
        if (raw.Notes == null || raw.NoteCount == 0)
        {
            throw new FormatException("notes 必须是非空数组");
        }

        if (raw.NoteCount > MaxHits)
        {
            throw new FormatException($"notes 超过 {MaxHits} 项"); // 每个音符至少一块砖，不可能对得上 hits
        }

        var timeOf = new Dictionary<int, double>(result.Hits.Count);
        foreach (var h in result.Hits)
        {
            if (timeOf.ContainsKey(h.TileIndex))
            {
                throw new FormatException($"hits 里砖号 {h.TileIndex} 重复");
            }

            timeOf[h.TileIndex] = h.Time;
        }

        foreach (var n in raw.Notes)
        {
            if (!n.IsArray || n.Count != 3 || n.Lanes == null)
            {
                throw new FormatException("notes 的每一项必须是 [第一块砖号, 最后一块砖号, [[轨道, 长按], …]]");
            }

            var first = Int(n.First, "砖号");
            if (!timeOf.TryGetValue(first, out var time))
            {
                throw new FormatException($"notes 里的砖号 {first} 不在 hits 里");
            }

            var note = new PlannedNote(first, time) { LastTileIndex = Int(n.Last, "砖号") };
            foreach (var pair in n.Lanes)
            {
                if (!pair.IsArray || pair.Count != 2)
                {
                    throw new FormatException("轨道必须写成 [轨道, 长按]");
                }

                var lane = Int(pair.A, "轨道");
                if (lane < 0 || lane >= result.Keys || note.Lanes.ContainsKey(lane))
                {
                    throw new FormatException($"砖 {first} 的轨道 {lane} 超出范围或重复");
                }

                note.Lanes[lane] = Num(pair.B, "长按");
            }

            result.Notes.Add(note);
        }
    }

    /// <summary>砖块按时间排好、不重复；音符按顺序、不重不漏地覆盖全部砖块；每个音符 1 到键数个键。</summary>
    private static void Check(CustomFingering f)
    {
        if (f.Hits.Count == 0 || f.Notes.Count == 0)
        {
            throw new FormatException("手序没有砖块或音符");
        }

        var lastTile = int.MinValue;
        var lastTime = double.NegativeInfinity;
        foreach (var h in f.Hits)
        {
            if (h.TileIndex <= lastTile || h.Time < lastTime || !IsFinite(h.Time) || !IsFinite(h.HoldSeconds) || h.HoldSeconds < 0
                || (!double.IsNaN(h.Angle) && !IsFinite(h.Angle)))
            {
                throw new FormatException($"砖 {h.TileIndex} 的砖号、时间或长按无效（要按砖号和时间排好、不能重复）");
            }

            lastTile = h.TileIndex;
            lastTime = h.Time;
        }

        var next = 0;
        foreach (var n in f.Notes)
        {
            if (next >= f.Hits.Count || f.Hits[next].TileIndex != n.TileIndex || n.LastTileIndex < n.TileIndex)
            {
                throw new FormatException($"音符（砖 {n.TileIndex}）没有接着上一个音符，手序必须按顺序覆盖全部砖块");
            }

            while (next < f.Hits.Count && f.Hits[next].TileIndex <= n.LastTileIndex)
            {
                next++;
            }

            if (f.Hits[next - 1].TileIndex != n.LastTileIndex)
            {
                throw new FormatException($"音符（砖 {n.TileIndex}）的最后一块砖 {n.LastTileIndex} 不在 hits 里");
            }

            if (n.Lanes.Count < 1 || n.Lanes.Count > f.Keys)
            {
                throw new FormatException($"音符（砖 {n.TileIndex}）要按 1–{f.Keys} 个键");
            }

            foreach (var kv in n.Lanes)
            {
                if (kv.Key < 0 || kv.Key >= f.Keys || !IsFinite(kv.Value) || kv.Value < 0)
                {
                    throw new FormatException($"音符（砖 {n.TileIndex}）的轨道或长按无效");
                }
            }
        }

        if (next != f.Hits.Count)
        {
            throw new FormatException($"手序只覆盖了 {next} / {f.Hits.Count} 块砖");
        }
    }

    private static void Text(JsonWriter w, string name, string value)
    {
        w.WritePropertyName(name);
        w.WriteValue(value ?? string.Empty);
    }

    private static double Round(double value) => Math.Round(value, 6);

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static int Int(JToken? token, string what) => FingeringReader.Int(FingeringReader.Scalar.Of(token), what);

    private static int Int(FingeringReader.Scalar value, string what) => FingeringReader.Int(value, what);

    private static double Num(FingeringReader.Scalar value, string what) => FingeringReader.Num(value, what);

    private static string Str(JObject root, string name, int max)
    {
        var token = root[name];
        if (token == null || token.Type == JTokenType.Null)
        {
            return string.Empty;
        }

        if (token.Type != JTokenType.String)
        {
            throw new FormatException(name + " 必须是文字");
        }

        var value = (string)token!;
        return value.Length > max ? value.Substring(0, max) : value;
    }
}
