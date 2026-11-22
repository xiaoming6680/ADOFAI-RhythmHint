using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RhythmHint.Core;

/// <summary>
/// 游戏装在浏览器不让网页写的文件夹里时（Steam 默认装在 C:\Program Files (x86)），编辑器存不进 Mods/RhythmHint/Fingerings：
/// 浏览器弹「由于此文件夹包含系统文件，无法将其打开」（2026-11-19 玩家截图）。这时编辑器改存到「文档\RhythmHint\Fingerings」，
/// Mod 每秒把那里的手序搬进游戏的手序文件夹。手序仍以游戏的文件夹为准，搬走而不是复制：游戏里删掉的不会再被复制回来。
/// </summary>
public static class FingeringInbox
{
    /// <summary>
    /// Windows 上浏览器连同里面的子文件夹都不让网页选的文件夹（Chromium 的 File System Access 黑名单：
    /// 程序目录、系统目录、AppData、ProgramData）。取不到的跳过。
    /// </summary>
    public static IEnumerable<string> WindowsBlockedRoots()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        yield return Environment.GetEnvironmentVariable("ProgramW6432") ?? string.Empty;
        yield return Environment.GetEnvironmentVariable("ProgramFiles") ?? string.Empty;
        yield return Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? string.Empty;
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    }

    /// <summary><paramref name="folder"/> 是不是在 <paramref name="blockedRoots"/> 之一里面（或者就是它）。不分大小写。</summary>
    public static bool BrowserBlocks(string folder, IEnumerable<string> blockedRoots)
    {
        var path = Normalize(folder);
        if (path == null)
        {
            return false;
        }

        foreach (var root in blockedRoots)
        {
            var r = Normalize(root);
            if (r != null && path.StartsWith(r, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>同名的另一首谱的手序最多编到几号（「名字 (99).json」），再多就不存了。</summary>
    public const int MaxNumbered = 99;

    /// <summary>
    /// 把 <paramref name="inbox"/> 里的 .json 搬进 <paramref name="target"/>，文件名按 <see cref="SaveName"/> 选（不覆盖别的谱的手序）。
    /// 搬不动的（浏览器正在写、被占用）留到下次。返回搬过去后的文件名。没有要搬的就不建 <paramref name="target"/>。
    /// </summary>
    public static List<string> MoveInto(string inbox, string target) => MoveInto(inbox, target, out _);

    /// <param name="inbox">中转文件夹。</param>
    /// <param name="target">游戏的手序文件夹。</param>
    /// <param name="problem">
    /// 列不出中转文件夹（刚好被删、没有权限）或建不了手序文件夹时的原因，这一次什么都没搬，下一秒再试；没问题为空。
    /// 复现（2026-11-19 第二轮评审）：原来这两步在逐个文件的 try 外面，异常漏到 Overlay 的兜底，整个 Mod 停用。
    /// </param>
    public static List<string> MoveInto(string inbox, string target, out string problem)
    {
        var moved = new List<string>();
        problem = string.Empty;
        if (!Directory.Exists(inbox))
        {
            return moved;
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(inbox, "*.json");
            if (files.Length > 0)
            {
                Directory.CreateDirectory(target);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = ex.Message;
            return moved;
        }

        foreach (var source in files)
        {
            try
            {
                var name = SaveName(target, Path.GetFileName(source), File.ReadAllText(source, Encoding.UTF8));
                File.Copy(source, Path.Combine(target, name), overwrite: true);
                File.Delete(source);
                moved.Add(name);
            }
            catch (IOException)
            {
                // 浏览器还在写或者文件被占用：下一秒再搬
            }
            catch (UnauthorizedAccessException)
            {
                // 没有权限：留在原处，下一秒再试（一直不行时编辑器那边的文件也还在）
            }
        }

        return moved;
    }

    /// <summary>
    /// 存进 <paramref name="folder"/> 用哪个文件名：<paramref name="name"/> 没人用、或用着的是同一关的手序（认得同一份谱面指纹、键数也一样，玩家重新保存）就用它；
    /// 用着的是另一首谱的（同一首歌不同谱师、别人分享的）、同一关别的键数的或读不出来的，依次试「名字 (2).json」「名字 (3).json」……，不覆盖别人的手序。
    /// 复现（2026-11-19 评审）：建议名只有「曲师 - 曲名」，原来同名直接覆盖。编辑器直接存进文件夹时按同样的规则（model.js）。
    /// </summary>
    /// <param name="text">要存的手序文件内容；它读不出来时不覆盖任何已有的文件。</param>
    public static string SaveName(string folder, string name, string text)
    {
        var mine = LevelOf(text);
        for (var k = 1; k <= MaxNumbered; k++)
        {
            var candidate = Numbered(name, k);
            var path = Path.Combine(folder, candidate);
            if (!File.Exists(path) || SameLevel(path, mine))
            {
                return candidate;
            }
        }

        throw new IOException($"同名的手序已有 {MaxNumbered} 份，{name} 没有存进去");
    }

    /// <summary>第 k 个候选文件名：Song.json、Song (2).json、Song (3).json……</summary>
    public static string Numbered(string name, int k)
    {
        if (k <= 1)
        {
            return name;
        }

        var stem = name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 5) : name;
        return $"{stem} ({k}).json";
    }

    /// <summary>手序认得的谱面指纹和键数。</summary>
    private static (HashSet<string> Charts, int Keys) LevelOf(string text)
    {
        try
        {
            var file = CustomFingering.Parse(text);
            return (new HashSet<string>(file.Charts, StringComparer.Ordinal), file.Keys);
        }
        catch (FormatException)
        {
            return (new HashSet<string>(), 0); // 读不出来的手序：和谁都不算同一关
        }
    }

    private static bool SameLevel(string path, (HashSet<string> Charts, int Keys) mine)
    {
        if (mine.Charts.Count == 0 || new FileInfo(path).Length > CustomFingering.MaxFileBytes)
        {
            return false;
        }

        var other = LevelOf(File.ReadAllText(path, Encoding.UTF8));
        return other.Keys == mine.Keys && other.Charts.Overlaps(mine.Charts);
    }

    private static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null; // 不是合法路径：当作不在黑名单里
        }
    }
}
