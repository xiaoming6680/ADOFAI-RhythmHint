using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RhythmHint.Core;
using UnityEngine;

namespace RhythmHint;

/// <summary>给一关挑出来的手序：要画的音符、哪些是自动排键补的、用了哪份文件、给玩家看的说明。</summary>
internal sealed class FingeringResult
{
    public FingeringResult(List<PlannedNote> notes, List<bool> fromAuto, string usedFile, CustomFingering? used, Message status, Message caption)
        : this(notes, fromAuto, usedFile, used, status, caption, string.Empty, new List<(string, int, int)>(), false)
    {
    }

    public FingeringResult(List<PlannedNote> notes, List<bool> fromAuto, string usedFile, CustomFingering? used, Message status, Message caption,
        string chart, List<(string Name, int MatchedHits, int TotalHits)> candidates, bool chosenByPlayer)
    {
        Chart = chart;
        Candidates = candidates;
        ChosenByPlayer = chosenByPlayer;
        Notes = notes;
        FromAuto = fromAuto;
        UsedFile = usedFile;
        Used = used;
        Status = status;
        Caption = caption;
    }

    public List<PlannedNote> Notes { get; }

    /// <summary>和 Notes 一一对应；没用专属手序时为空。</summary>
    public List<bool> FromAuto { get; }

    /// <summary>用上的手序文件名；没有为空。</summary>
    public string UsedFile { get; }

    public CustomFingering? Used { get; }

    /// <summary>设置窗口里显示的说明。</summary>
    public Message Status { get; }

    /// <summary>开局在面板上提醒一下（对不上、分不清）；不用提醒为空。</summary>
    public Message Caption { get; }

    /// <summary>本关的谱面指纹（玩家选了以后按它记住）。</summary>
    public string Chart { get; }

    /// <summary>像这一关的手序（文件名、匹配砖数和总砖数），给玩家选。</summary>
    public List<(string Name, int MatchedHits, int TotalHits)> Candidates { get; }

    /// <summary>这一关用哪份（或都不用）是玩家自己选的。</summary>
    public bool ChosenByPlayer { get; }

    /// <summary>设置给这一关定的键数（自动选键数时用上的专属手序可能改掉它，最后按 <see cref="Keys"/>）。</summary>
    public KeyCountChoice KeyChoice { get; set; } = new(4, KeyCountReason.Manual);

    /// <summary><see cref="Notes"/> 是几键的：用上的专属手序在自动模式下是几键就几键，否则是设置定的。</summary>
    public int Keys { get; set; } = 4;
}

/// <summary>
/// 专属手序文件夹（Mods/RhythmHint/Fingerings）：玩家把手序文件放进来就不用管了，进到对应的歌时自动使用（认歌见 FingeringMatcher）。
/// 每秒看一次文件夹有没有变化（编辑器保存后关卡里马上换上），打歌时每 5 秒一次；读过的文件按修改时间缓存，同一关重开不重新对齐。
/// </summary>
internal sealed class FingeringSource
{
    /// <summary>平时多久看一次手序文件夹（秒）：编辑器保存后关卡里马上换上。</summary>
    private const float PollSeconds = 1;

    /// <summary>
    /// 打歌时（关卡显示中、没暂停、没开设置窗口）多久看一次（秒）。看一次除了列目录，还可能要把中转文件夹的手序搬进来、
    /// 把改动同步到备份（复制文件），都在主线程上，打歌时少做几次、少卡几次；打歌中途改手序很少见（编辑器保存一般在暂停或菜单里），
    /// 晚几秒换上不要紧。暂停、回到菜单后按上次看的时间算，满 1 秒马上再看。
    /// </summary>
    private const float PlayingPollSeconds = 5;

    /// <summary>按文件名（不分大小写）。</summary>
    private readonly Dictionary<string, Entry> _files = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>这次运行在手序文件夹里见过的文件名：备份里只删这些后来不见了的（见 <see cref="FingeringBackup.Mirror"/>）。</summary>
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private string _signature = string.Empty;
    private float _lastPoll = float.NegativeInfinity;
    private string _lastKey = string.Empty;
    private FingeringResult? _last;
    private bool _restoreChecked;
    private bool _backupPending;
    private bool _backupWarned;
    private bool _inboxWarned;

    public static string Folder => Path.Combine(Main.Mod.Path, "Fingerings");

    /// <summary>手序的备份（文档\RhythmHint\FingeringsBackup，见 <see cref="FingeringBackup"/>）；找不到文档文件夹时为 null，不备份。</summary>
    public static string? Backup => _backup.Value;

    private static readonly Lazy<string?> _backup = new(() =>
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return string.IsNullOrEmpty(documents) ? null : Path.Combine(documents, "RhythmHint", "FingeringsBackup");
    });

    /// <summary>
    /// 游戏装在浏览器不让网页写的文件夹里时（Steam 默认的 C:\Program Files (x86)），编辑器存到这里，每秒搬进 <see cref="Folder"/>；
    /// 否则为 null（编辑器直接存进 <see cref="Folder"/>）。见 <see cref="FingeringInbox"/>。
    /// </summary>
    public static string? Inbox => _inbox.Value;

    private static readonly Lazy<string?> _inbox = new(() =>
    {
        if (!FingeringInbox.BrowserBlocks(Folder, FingeringInbox.WindowsBlockedRoots()))
        {
            return null;
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrEmpty(documents))
        {
            Main.Mod.Logger.Warning("编辑器：游戏在浏览器不让网页写的文件夹里，但找不到文档文件夹，编辑器只能另存为文件");
            return null;
        }

        var inbox = Path.Combine(documents, "RhythmHint", "Fingerings");
        Main.Mod.Logger.Log($"编辑器：游戏在浏览器不让网页写的文件夹里，编辑器改存到 {inbox}，Mod 自动搬进手序文件夹");
        return inbox;
    });

    /// <summary>每秒最多看一次（打歌时 5 秒一次）；文件加了、删了、改了就返回 true。</summary>
    /// <param name="playing">正在打歌：关卡显示中、没暂停、没开设置窗口。</param>
    public bool Poll(bool playing)
    {
        var now = Time.unscaledTime;
        if (now - _lastPoll < (playing ? PlayingPollSeconds : PollSeconds))
        {
            return false;
        }

        _lastPoll = now;
        if (!_restoreChecked)
        {
            _restoreChecked = true;
            RestoreFromBackup(); // 要在搬中转文件夹之前：搬的时候会建出手序文件夹，就看不出整个文件夹被删过了
        }

        if (Inbox != null)
        {
            MoveInbox(Inbox);
        }

        var signature = Signature();
        if (signature == _signature)
        {
            if (_backupPending)
            {
                BackUp();
            }

            return false;
        }

        _signature = signature;
        BackUp();
        return true;
    }

    /// <summary>
    /// 手序文件夹整个不见了（UMM 卸载、回退会删掉整个 Mod 文件夹）、或者上次没还原完时，从文档里的备份还原。
    /// 每次启动只试一次：读不了的（杀毒软件在扫、OneDrive 离线）留到下次启动，在那之前备份里的这几份不会被改动。
    /// </summary>
    private static void RestoreFromBackup()
    {
        if (Backup == null)
        {
            return;
        }

        try
        {
            var result = FingeringBackup.Restore(Folder, Backup);
            if (result.Restored.Count > 0)
            {
                Main.Mod.Logger.Log($"专属手序：手序文件夹被删掉过（UMM 卸载、回退会删掉整个 Mod 文件夹），从 {Backup} 还原了 {result.Restored.Count} 份：{string.Join("、", result.Restored)}");
            }

            if (result.Pending.Count > 0)
            {
                Main.Mod.Logger.Warning($"专属手序：备份里有 {result.Pending.Count} 份这次还原不了（{result.Error}），下次启动游戏时再还原，在那之前备份里的这几份不会被改动：{string.Join("、", result.Pending)}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Main.Mod.Logger.Warning($"专属手序：从备份 {Backup} 还原失败，下次启动游戏时再试：{ex.Message}");
        }
    }

    /// <summary>把编辑器存进中转文件夹的手序搬进手序文件夹；列不出中转文件夹、建不了手序文件夹时下一秒再试，一直不行只记一次日志。</summary>
    private void MoveInbox(string inbox)
    {
        var moved = FingeringInbox.MoveInto(inbox, Folder, out var problem);
        foreach (var name in moved)
        {
            Main.Mod.Logger.Log($"专属手序：把编辑器保存的 {name} 搬进手序文件夹");
        }

        if (problem.Length == 0)
        {
            _inboxWarned = false;
        }
        else if (!_inboxWarned)
        {
            _inboxWarned = true;
            Main.Mod.Logger.Warning($"专属手序：没能把 {inbox} 里的手序搬进手序文件夹，稍后再试：{problem}");
        }
    }

    /// <summary>把手序文件夹同步到备份；有文件正被占用时下一秒再试。一直失败只记一次日志。</summary>
    private void BackUp()
    {
        if (Backup == null)
        {
            return;
        }

        try
        {
            _backupPending = !FingeringBackup.Mirror(Folder, Backup, _seen);
            _backupWarned = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _backupPending = true;
            if (!_backupWarned)
            {
                _backupWarned = true;
                Main.Mod.Logger.Warning($"专属手序：备份到 {Backup} 失败，稍后再试：{ex.Message}");
            }
        }
    }

    /// <summary>给这一关挑手序。没开专属手序、没有对得上的文件时用自动排键。</summary>
    /// <param name="autoFor">按几键自动排的音符（对不上的地方用它补）。</param>
    /// <param name="keyChoice">设置给这一关定的键数；自动模式下用上的手序是 4、6、8 键时按手序的。</param>
    public FingeringResult Choose(List<TileHit> hits, Func<int, List<PlannedNote>> autoFor, Settings settings, LevelInfo info, KeyCountChoice keyChoice)
    {
        if (!settings.UseCustomFingering)
        {
            return Compute(hits, autoFor, settings, keyChoice, log: true);
        }

        // 谱面指纹不看双押砖要按几下，游戏设置改了（只按一下 / 照常）也要重新套用：补键、减键跟着变；估算难度变了键数可能跟着变
        var chart = CustomFingering.ChartPrint(hits);
        var key = $"{Signature()}/{settings.FingeringKey}/{chart}/{ChosenFor(settings, chart) ?? "-"}/{FloorHits.TapsKey(hits)}/{keyChoice}";
        if (key == _lastKey && _last != null)
        {
            return _last;
        }

        _last = Compute(hits, autoFor, settings, keyChoice, log: true);
        _lastKey = key;
        return _last;
    }

    /// <summary>
    /// 这一关换成 keys 键时用哪份手序（编辑器里切换键数用，2026-11-22）：和游戏里手动选这个键数时一样挑，
    /// 不动游戏正在用的结果（不进缓存、不写日志）。
    /// </summary>
    public FingeringResult ChooseFor(List<TileHit> hits, Func<int, List<PlannedNote>> autoFor, Settings settings, int keys) =>
        Compute(hits, autoFor, settings, new KeyCountChoice(keys, KeyCountReason.Manual), log: false);

    private FingeringResult Compute(List<TileHit> hits, Func<int, List<PlannedNote>> autoFor, Settings settings, KeyCountChoice keyChoice, bool log)
    {
        var auto = autoFor(keyChoice.Keys);
        if (!settings.UseCustomFingering)
        {
            return new FingeringResult(auto, new List<bool>(), string.Empty, null, Lang.M("专属手序已关闭，使用自动排键。"), Message.Empty)
            {
                KeyChoice = keyChoice,
                Keys = keyChoice.Keys,
            };
        }

        var chart = CustomFingering.ChartPrint(hits);
        var chosen = ChosenFor(settings, chart);
        var candidates = Load(out var broken);
        var choice = FingeringMatcher.Choose(candidates, hits, k => autoFor(k), settings.LeftHanded, chosen, keyChoice.Keys, keyChoice.FollowFile);
        foreach (var merge in log ? choice.Merged : new List<FingeringMerge>())
        {
            Main.Mod.Logger.Log(merge.SameContent
                ? $"专属手序：{merge.Dropped} 和 {merge.Kept} 内容一样，当成一份（用 {merge.Kept}）"
                : $"专属手序：{merge.Dropped} 和 {merge.Kept} 是同一位作者给这一版谱面存的，用较新的 {merge.Kept}");
        }

        var brokenNote = Notes(broken, choice.OtherKeys, keyChoice.Keys);
        var list = choice.Candidates.ConvertAll(c => (c.Candidate.Name, c.MatchedHits, c.TotalHits));
        var byPlayer = choice.Reason == FingeringReason.Chosen;
        FingeringResult result;
        if (choice.Used != null && choice.Match != null)
        {
            var m = choice.Match;
            var status = m.FallbackNotes == 0
                ? Lang.M("正在使用专属手序：{0}{1}", choice.Used.Name, brokenNote)
                : Lang.M("正在使用专属手序：{0}。谱面和手序有 {1} 处对不上（共 {2} 个音符），这些地方使用自动排键。{3}",
                    choice.Used.Name, m.FallbackSections, m.FallbackNotes, brokenNote);
            var caption = m.FallbackNotes == 0 ? Message.Empty
                : Lang.M("专属手序有 {0} 处和这一版谱面对不上，已用自动排键补上", m.FallbackSections);
            result = new FingeringResult(m.Notes, m.FromAuto, choice.Used.Name, choice.Used.File, status, caption, chart, list, byPlayer);
            result.Keys = m.Keys;
            if (log)
            {
                Main.Mod.Logger.Log($"专属手序：{choice.Used.Name}（{choice.Reason}，{choice.Used.File.Keys} 键，按 {m.Keys} 键显示），对上 {m.MatchedHits}/{m.TotalHits} 块砖，自动补 {m.FallbackNotes} 个音符（{m.FallbackSections} 处）");
            }
        }
        else if (choice.Reason == FingeringReason.Unsure)
        {
            result = new FingeringResult(auto, new List<bool>(), string.Empty, null,
                Lang.M("有 {0} 份专属手序像这一关，但不能确定是哪一份，暂用自动排键。请在下面选择要用的手序。{1}", list.Count, brokenNote),
                Lang.M("有专属手序像这一关但不能确定，请在 F8 → 专属手序里选择"), chart, list, false);
            if (log)
            {
                Main.Mod.Logger.Log("专属手序：分不清，候选 " + string.Join("、", list.ConvertAll(c => $"{c.Name} {c.MatchedHits}/{c.TotalHits}")));
            }
        }
        else if (byPlayer)
        {
            result = new FingeringResult(auto, new List<bool>(), string.Empty, null,
                Lang.M("这一关已按你的选择不用专属手序，使用自动排键。{0}", brokenNote), Message.Empty, chart, list, true);
        }
        else
        {
            result = new FingeringResult(auto, new List<bool>(), string.Empty, null,
                Lang.M("本关没有专属手序，使用自动排键。{0}", brokenNote), Message.Empty, chart, list, false);
        }

        result.KeyChoice = keyChoice;
        if (choice.Match == null)
        {
            result.Keys = keyChoice.Keys;
        }

        return result;
    }

    /// <summary>
    /// 状态后面补的说明：读不了的文件；像这一关、但键数比这一关显示的多（或是奇数键）的手序：不套用
    /// （2026-11-19 评审 A：原来悄悄过滤掉，玩家不知道为什么没用上）。
    /// </summary>
    /// <param name="shown">这一关按几键显示。</param>
    private static Message Notes(List<string> broken, List<FingeringCandidate> otherKeys, int shown)
    {
        var keys = otherKeys.Count switch
        {
            0 => Message.Empty,
            1 => Lang.M("\n{0} 为 {1} 键手序，与本关的 {2} 键不符，未使用。", otherKeys[0].Name, otherKeys[0].File.Keys, shown),
            _ => Lang.M("\n有 {0} 份与本关相近的手序键数多于 {1} 键，未使用：{2}", otherKeys.Count, shown,
                NameList(otherKeys.ConvertAll(c => c.Name), 0, otherKeys.Count)),
        };
        if (broken.Count == 0)
        {
            return keys;
        }

        return otherKeys.Count == 0
            ? Lang.M("\n有 {0} 个文件无法读取：{1}（原因见日志）", broken.Count, NameList(broken, 0, broken.Count))
            : Lang.M("\n有 {0} 个文件无法读取：{1}（原因见日志）{2}", broken.Count, NameList(broken, 0, broken.Count), keys);
    }

    /// <summary>
    /// 给玩家看的文件名列表，分隔符跟着界面语言（中文、日文用顿号，英文、韩文用逗号；原来写死顿号，2026-11-19 第二轮评审）。
    /// 做成消息，换界面语言时跟着变、日志里是中文原文；两两对半拼，名字再多嵌套也只有 log₂ 层。
    /// </summary>
    private static object NameList(List<string> names, int from, int count)
    {
        if (count <= 1)
        {
            return count == 1 ? names[from] : string.Empty;
        }

        var half = count / 2;
        return Lang.M("{0}、{1}", NameList(names, from, half), NameList(names, from + half, count - half));
    }

    /// <summary>玩家给这个谱面选过的文件名（空串 = 选了都不用）；没选过为 null。存在 Settings.FingeringChoices，一行「指纹 Tab 文件名」。</summary>
    public static string? ChosenFor(Settings settings, string chart)
    {
        foreach (var line in settings.FingeringChoices.Split('\n'))
        {
            var tab = line.IndexOf('\t');
            if (tab > 0 && line.Substring(0, tab) == chart)
            {
                return line.Substring(tab + 1);
            }
        }

        return null;
    }

    /// <summary>记住玩家给这个谱面的选择；name 为 null 时忘掉（重新自动识别）。</summary>
    public static void Remember(Settings settings, string chart, string? name)
    {
        var lines = new List<string>();
        foreach (var line in settings.FingeringChoices.Split('\n'))
        {
            if (line.Length > 0 && !line.StartsWith(chart + "\t", StringComparison.Ordinal))
            {
                lines.Add(line);
            }
        }

        if (name != null)
        {
            lines.Add(chart + "\t" + name);
        }

        settings.FingeringChoices = string.Join("\n", lines);
    }

    /// <summary>读一份文件（编辑器打开时带上作者、说明）；读不了为 null。</summary>
    public CustomFingering? Get(string name)
    {
        Load(out _);
        return _files.TryGetValue(name, out var entry) ? entry.File : null;
    }

    /// <summary>文件夹里所有 .json 的名字、修改时间、大小，用来判断变没变。</summary>
    private static string Signature()
    {
        var sb = new StringBuilder();
        foreach (var info in JsonFiles())
        {
            sb.Append(info.Name).Append('|').Append(info.LastWriteTimeUtc.Ticks).Append('|').Append(info.Length).Append(';');
        }

        return sb.ToString();
    }

    /// <summary>
    /// 文件夹里的 .json，连同大小和修改时间。用 DirectoryInfo.EnumerateFiles：列目录时就带回大小和时间，不再每个文件单独查一次磁盘
    /// （2026-11-19 第二轮评审：每秒在主线程上看一次，原来 1000 份手序要 24–29 ms，144 Hz 一帧只有 6.9 ms）。
    /// 游戏自带的 Mono 也是这样（mscorlib 里 FileSystemInfo.Create 调 Init，用列目录的结果填好属性、时间和大小）。
    /// 没有文件夹、文件夹刚好被删或被改名时当成空的，下一秒再看。
    /// </summary>
    private static List<FileInfo> JsonFiles()
    {
        var files = new List<FileInfo>();
        if (!Directory.Exists(Folder))
        {
            return files; // 没用过专属手序的玩家没有这个文件夹：先看一眼，不用每秒抛一次异常
        }

        try
        {
            foreach (var info in new DirectoryInfo(Folder).EnumerateFiles("*.json"))
            {
                if (TryStat(info))
                {
                    files.Add(info);
                }
            }
        }
        catch (IOException)
        {
            files.Clear(); // DirectoryNotFoundException 也是 IOException：列到一半文件夹没了就当没有手序
        }

        return files;
    }

    /// <summary>
    /// 确认读得到修改时间和大小（列目录时已经带回来了，一般不再查磁盘；运行时没带回来时这里才去读）。列出来之后文件刚好被删、
    /// 被改名（浏览器保存、玩家整理文件夹）时返回 false，这一份下一秒再看；原来 Length 抛的 FileNotFoundException 会漏到 Overlay 的兜底，整个 Mod 停用。
    /// </summary>
    private static bool TryStat(FileInfo info)
    {
        try
        {
            _ = info.Length;
            return true;
        }
        catch (IOException)
        {
            return false; // FileNotFoundException：刚被删掉，跳过
        }
    }

    private List<FingeringCandidate> Load(out List<string> broken)
    {
        var result = new List<FingeringCandidate>();
        broken = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var info in JsonFiles())
        {
            seen.Add(info.Name);
            if (!_files.TryGetValue(info.Name, out var entry) || entry.Time != info.LastWriteTimeUtc || entry.Size != info.Length)
            {
                entry = Read(info);
                _files[info.Name] = entry;
            }

            if (entry.File != null)
            {
                result.Add(new FingeringCandidate(info.Name, entry.File));
            }
            else
            {
                broken.Add(info.Name);
            }
        }

        foreach (var stale in new List<string>(_files.Keys))
        {
            if (!seen.Contains(stale))
            {
                _files.Remove(stale);
            }
        }

        return result;
    }

    private static Entry Read(FileInfo info)
    {
        if (info.Length > CustomFingering.MaxFileBytes)
        {
            Main.Mod.Logger.Warning($"手序文件 {info.Name} 有 {info.Length / 1024 / 1024} MB，太大，不读");
            return new Entry(info.LastWriteTimeUtc, info.Length, null);
        }

        try
        {
            var file = CustomFingering.Parse(File.ReadAllText(info.FullName, Encoding.UTF8));
            if (!KeyLayout.IsSupported(file.Keys) || file.Keys > 8)
            {
                Main.Mod.Logger.Log($"手序文件 {info.Name} 是 {file.Keys} 键手序，这个版本只能显示 4、6、8 键（奇数键、8 键以上不套用，进到像它的那一关时状态里会说明）");
            }

            if (file.DroppedFields.Count > 0)
            {
                Main.Mod.Logger.Warning($"手序文件 {info.Name} 里的字段 {string.Join("、", file.DroppedFields)} 太大，没有读；手序照常使用，编辑器重新保存时这些字段不会保留");
            }

            return new Entry(info.LastWriteTimeUtc, info.Length, file);
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            // 坏文件只跳过这一份（设置窗口里列出来），不影响别的手序和自动排键
            Main.Mod.Logger.Warning($"手序文件 {info.Name} 无法读取：{ex.Message}");
            return new Entry(info.LastWriteTimeUtc, info.Length, null);
        }
    }

    private readonly record struct Entry(DateTime Time, long Size, CustomFingering? File);
}
