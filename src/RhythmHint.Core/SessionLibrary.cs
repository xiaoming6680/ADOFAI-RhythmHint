using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RhythmHint.Core;

/// <summary>编辑器「历史」里的一关：最近一次在游戏里打开它时导出的会话。</summary>
public sealed class RecentSession
{
    /// <summary>谱面指纹（<see cref="CustomFingering.ChartPrint"/>），也是文件名。</summary>
    public string Id { get; set; } = string.Empty;

    public string Song { get; set; } = string.Empty;

    public string Artist { get; set; } = string.Empty;

    public string Charter { get; set; } = string.Empty;

    public int Notes { get; set; }

    /// <summary>recent 目录里的会话脚本（&lt;id&gt;.js）。</summary>
    public string Session { get; set; } = string.Empty;

    /// <summary>几键（2026-11-22 加的：老的列表里没有，读成 4）。</summary>
    public int Keys { get; set; } = CustomFingering.DefaultKeys;

    /// <summary>recent 目录里的音乐文件名；没有为空。</summary>
    public string Audio { get; set; } = string.Empty;

    /// <summary>保存用的手序文件名。</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>导出的时间（Unix 毫秒）。</summary>
    public long Exported { get; set; }
}

/// <summary>
/// 编辑器的「历史」（2026-11-18 用户：很多人一次改不完整张谱就把网页关了，不开游戏很难再找到网页和之前的东西）。
/// 每次「编辑本关手序」除了写编辑器目录的 session.js，再把这一关的会话和音乐另存到 recent/，最多留 <see cref="Keep"/> 关，
/// recent/index.js 列出它们（file:// 打开的网页读不了文件夹，只能用 &lt;script&gt; 载入）。同一份谱面只留最新的一份。
/// 改了一半的音符由编辑器自己存在浏览器里，和这里的会话按 Id 对上。
/// </summary>
public static class SessionLibrary
{
    public const string FolderName = "recent";

    public const string IndexFormat = "rhythmhint-editor-recent";

    /// <summary>最多留几关。</summary>
    public const int Keep = 10;

    private const string IndexFile = "index.js";
    private const string IndexPrefix = "window.RHYTHMHINT_RECENT = ";
    private static readonly Regex IdPattern = new("^[0-9]+-[0-9a-f]+(-[0-9]+k)?$");
    private static readonly Regex ExtensionPattern = new("^\\.[0-9a-z]{1,8}$");
    private static readonly Encoding Utf8 = new UTF8Encoding(false);

    /// <summary>
    /// 这一关在历史里的 Id：谱面指纹（只由数字、连字符和十六进制组成，可以直接当文件名）；不是 4 键的后面加「-6k」「-8k」
    /// （同一张谱的 4K、6K 各留一份，编辑器的草稿也按它分开存，2026-11-22）。4 键的和以前一样，老草稿照样接得上。
    /// </summary>
    public static string IdOf(CustomFingering fingering) =>
        CustomFingering.ChartPrint(fingering.Hits) + (fingering.Keys != CustomFingering.DefaultKeys ? $"-{fingering.Keys}k" : string.Empty);

    /// <summary>
    /// 这一关的音乐放在哪（相对编辑器目录，比如 recent/968-…-20261118-093000-123.ogg）。名字带时间：
    /// 浏览器还占着上一次的音乐文件时也不撞，旧文件等 <see cref="Save"/> 整理时再删。
    /// 顺手删掉以前版本放在编辑器目录根上的 session-audio*（删不掉就留着下次再删）。
    /// </summary>
    /// <param name="editorFolder">编辑器目录。</param>
    /// <param name="id">这一关的 Id（<see cref="IdOf"/>）。</param>
    /// <param name="extension">扩展名，比如 .ogg。</param>
    public static string AudioFile(string editorFolder, string id, string extension)
    {
        CheckId(id);
        DeleteQuietly(Directory.GetFiles(editorFolder, "session-audio*"));
        var folder = Path.Combine(editorFolder, FolderName);
        Directory.CreateDirectory(folder);
        var ext = extension.ToLowerInvariant();
        if (!ExtensionPattern.IsMatch(ext))
        {
            ext = ".audio"; // 只有正常的扩展名进文件名；浏览器按内容认格式
        }

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var name = id + "-" + stamp + ext;
        for (var i = 2; File.Exists(Path.Combine(folder, name)); i++)
        {
            name = id + "-" + stamp + "-" + i + ext;
        }

        return FolderName + "/" + name;
    }

    /// <summary>
    /// 写这一关的会话：recent/&lt;id&gt;.js、recent/index.js（这一关排到最前），以及编辑器目录的 session.js
    /// （游戏打开、玩家自己打开编辑器时都先显示这一关）。超出 <see cref="Keep"/> 的旧关卡和没人用的文件删掉，删不掉的下次再删。
    /// </summary>
    /// <param name="editorFolder">编辑器目录。</param>
    /// <param name="session">这一关的会话；Id 为空时按谱面指纹补上。</param>
    /// <param name="now">导出的时间。</param>
    public static void Save(string editorFolder, EditorSession session, DateTimeOffset now)
    {
        if (session.Id.Length == 0)
        {
            session.Id = IdOf(session.Fingering);
        }

        CheckId(session.Id);
        var folder = Path.Combine(editorFolder, FolderName);
        Directory.CreateDirectory(folder);
        var script = session.ToScript();
        var name = session.Id + ".js";
        File.WriteAllText(Path.Combine(folder, name), script, Utf8);

        var audio = session.Audio?.File ?? string.Empty;
        var entry = new RecentSession
        {
            Id = session.Id,
            Song = session.Fingering.Song,
            Artist = session.Fingering.Artist,
            Charter = session.Fingering.Charter,
            Notes = session.Fingering.Notes.Count,
            Keys = session.Fingering.Keys,
            Session = name,
            Audio = audio.StartsWith(FolderName + "/", StringComparison.Ordinal) ? audio.Substring(FolderName.Length + 1) : string.Empty,
            FileName = session.FileName,
            Exported = now.ToUnixTimeMilliseconds(),
        };

        // 列表读不出来（被改坏了）时不整理：认不出哪些文件还有用，宁可留着
        var readable = TryRead(folder, out var items);
        items.RemoveAll(e => e.Id == entry.Id);
        items.Insert(0, entry);
        if (items.Count > Keep)
        {
            items.RemoveRange(Keep, items.Count - Keep);
        }

        File.WriteAllText(Path.Combine(folder, IndexFile), ToScript(items), Utf8);
        File.WriteAllText(Path.Combine(editorFolder, "session.js"), script, Utf8);
        if (readable)
        {
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { IndexFile };
            foreach (var item in items)
            {
                keep.Add(item.Session);
                if (item.Audio.Length > 0)
                {
                    keep.Add(item.Audio);
                    keep.Add(AudioScript.PathFor(item.Audio)); // 同一首歌另存的脚本，有就一起留着
                }
            }

            DeleteQuietly(Directory.GetFiles(folder).Where(f => !keep.Contains(Path.GetFileName(f))));
        }
    }

    /// <summary>历史里的关卡，新的在前；没有或读不出来为空。</summary>
    public static List<RecentSession> Read(string editorFolder)
    {
        TryRead(Path.Combine(editorFolder, FolderName), out var items);
        return items;
    }

    /// <summary>写成 <c>window.RHYTHMHINT_RECENT = {…};</c>。</summary>
    public static string ToScript(IEnumerable<RecentSession> items) => IndexPrefix + ToJson(items).ToString(Formatting.None) + ";\n";

    /// <summary>列表的 JSON（编辑器读 format 和 items）。</summary>
    public static JObject ToJson(IEnumerable<RecentSession> items) => new()
    {
        ["format"] = IndexFormat,
        ["items"] = new JArray(items.Select(e => new JObject
        {
            ["id"] = e.Id,
            ["song"] = e.Song,
            ["artist"] = e.Artist,
            ["charter"] = e.Charter,
            ["notes"] = e.Notes,
            ["keys"] = e.Keys,
            ["session"] = e.Session,
            ["audio"] = e.Audio,
            ["fileName"] = e.FileName,
            ["exported"] = e.Exported,
        })),
    };

    /// <returns>
    /// 列表文件不存在、或每一项都读得懂为 true；整个读不出来为 false（items 为空）；
    /// 有的项读不懂也为 false，items 里是读得懂的那些（Save 这时不整理文件：认不出读不懂的那项用着哪些文件）。
    /// </returns>
    private static bool TryRead(string folder, out List<RecentSession> items)
    {
        items = new List<RecentSession>();
        var path = Path.Combine(folder, IndexFile);
        if (!File.Exists(path))
        {
            return true;
        }

        JArray list;
        try
        {
            var text = File.ReadAllText(path, Utf8).Trim();
            if (!text.StartsWith(IndexPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            // 不把像日期的文字自动转成日期（曲名恰好写成日期时间时原来被改写成本机格式的日期）
            using var reader = new JsonTextReader(new StringReader(text.Substring(IndexPrefix.Length).TrimEnd(';'))) { DateParseHandling = DateParseHandling.None };
            var json = JObject.Load(reader);
            while (reader.Read())
            {
                // 和 JObject.Parse 一样：后面除了注释还有别的内容，读的时候抛 JsonReaderException
            }

            if (json["format"]?.Type != JTokenType.String || (string?)json["format"] != IndexFormat || json["items"] is not JArray array)
            {
                return false;
            }

            list = array;
        }
        catch (JsonException)
        {
            return false;
        }

        var complete = true;
        foreach (var token in list.OfType<JObject>())
        {
            RecentSession item;
            try
            {
                item = new RecentSession
                {
                    Id = (string?)token["id"] ?? string.Empty,
                    Song = (string?)token["song"] ?? string.Empty,
                    Artist = (string?)token["artist"] ?? string.Empty,
                    Charter = (string?)token["charter"] ?? string.Empty,
                    Notes = (int?)token["notes"] ?? 0,
                    Keys = (int?)token["keys"] ?? CustomFingering.DefaultKeys,
                    Session = (string?)token["session"] ?? string.Empty,
                    Audio = (string?)token["audio"] ?? string.Empty,
                    FileName = (string?)token["fileName"] ?? string.Empty,
                    Exported = (long?)token["exported"] ?? 0,
                };
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidCastException or OverflowException)
            {
                // 这一项的字段类型不对（Newtonsoft 强转数组、对象时抛 ArgumentException，不是数字的文字抛 FormatException……）：
                // 跳过这一项，别的照常。复现（2026-11-19 评审）：原来没接住 ArgumentException，每次「编辑本关手序」都抛、编辑器打不开
                complete = false;
                continue;
            }

            // 只认自己写的样子：文件名都由 Id 拼出来，不会指到 recent 目录外面
            if (IdPattern.IsMatch(item.Id) && item.Session == item.Id + ".js"
                && (item.Audio.Length == 0 || (item.Audio.StartsWith(item.Id + "-", StringComparison.Ordinal) && item.Audio.IndexOfAny(new[] { '/', '\\' }) < 0)))
            {
                items.Add(item);
            }
        }

        items.Sort((a, b) => b.Exported.CompareTo(a.Exported));
        return complete;
    }

    private static void CheckId(string id)
    {
        if (!IdPattern.IsMatch(id))
        {
            throw new ArgumentException("不是谱面指纹：" + id, nameof(id));
        }
    }

    private static void DeleteQuietly(IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 被浏览器占着（还在放这首歌）：留着下次再删
            }
        }
    }
}
