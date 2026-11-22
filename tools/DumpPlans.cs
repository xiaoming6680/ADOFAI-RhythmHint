#:project ../src/RhythmHint.Reference/RhythmHint.Reference.csproj
#:property TargetFramework=net10.0
#:property TreatWarningsAsErrors=false
#:property PublishAot=false

// 改排键规则前后对比用：把本机真实谱（不进仓库）在各种排键设置下的结果写成摘要，改完再跑一遍比较。
// 读 RHYTHMHINT_WORKSHOP（创意工坊 977950 文件夹，递归找 .adofai）和 RHYTHMHINT_TUF（TUF 校准谱文件夹的 levels/）。
// 每关、每种设置一行：关卡、设置、提示个数、所有提示（砖号、时间、轨道、长按）的 SHA-256。
// 用法：
//   dotnet run tools/DumpPlans.cs -- <输出.tsv> [--keys 4|6|8]
// 比较：两份输出逐行相同就是逐音一样；不同的行指出是哪一关、哪种设置。

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using RhythmHint.Core;
using RhythmHint.Reference;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
if (args.Length < 1)
{
    Console.Error.WriteLine("用法：dotnet run tools/DumpPlans.cs -- <输出.tsv> [--keys 4|6|8]");
    return 2;
}

var keys = 4;
for (var i = 1; i < args.Length; i++)
{
    if (args[i] == "--keys" && i + 1 < args.Length)
    {
        keys = int.Parse(args[++i], CultureInfo.InvariantCulture);
    }
}

var levels = new List<(string Name, string Path)>();
var workshop = Environment.GetEnvironmentVariable("RHYTHMHINT_WORKSHOP");
if (!string.IsNullOrEmpty(workshop) && Directory.Exists(workshop))
{
    levels.AddRange(Directory.EnumerateFiles(workshop, "*.adofai", SearchOption.AllDirectories)
        .Select(p => ("workshop/" + System.IO.Path.GetRelativePath(workshop, p).Replace('\\', '/'), p)));
}

var tuf = Environment.GetEnvironmentVariable("RHYTHMHINT_TUF");
if (!string.IsNullOrEmpty(tuf) && Directory.Exists(System.IO.Path.Combine(tuf, "levels")))
{
    levels.AddRange(Directory.EnumerateFiles(System.IO.Path.Combine(tuf, "levels"), "*.adofai")
        .Select(p => ("tuf/" + System.IO.Path.GetFileName(p), p)));
}

if (levels.Count == 0)
{
    Console.Error.WriteLine("没有谱面：设置 RHYTHMHINT_WORKSHOP 或 RHYTHMHINT_TUF。");
    return 1;
}

levels.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

// 排键开关的 16 种组合 × 两种排法，再加几组改了时间参数的
var variants = new List<(string Name, FingeringOptions Options)>();
foreach (var style in Enum.GetValues<FingeringStyle>())
{
    for (var mask = 0; mask < 16; mask++)
    {
        variants.Add(($"{style}-{mask}", new FingeringOptions
        {
            Style = style,
            LeftHanded = (mask & 1) != 0,
            ChordsOnOtherHand = (mask & 2) != 0,
            UseRoll = (mask & 4) == 0,
            OuterRoll = (mask & 8) == 0,
        }));
    }

    variants.Add(($"{style}-R165", new FingeringOptions { Style = style, ResetWindow = 0.165 }));
    variants.Add(($"{style}-R300", new FingeringOptions { Style = style, ResetWindow = 0.3, ReviewShortMinGap = 0.035 }));
    variants.Add(($"{style}-R60", new FingeringOptions { Style = style, ResetWindow = 0.06, JackWindow = 0.1 }));
}

using var output = new StreamWriter(args[0], false, new UTF8Encoding(false));
int read = 0, failed = 0;
foreach (var (name, path) in levels)
{
    AdofaiLevel level;
    try
    {
        level = AdofaiLevelReader.Parse(File.ReadAllText(path));
    }
    catch (Exception e) when (e is FormatException or JsonException or IOException or InvalidCastException or ArgumentException)
    {
        // 读不了的谱两次运行一样读不了，跳过不影响比较
        failed++;
        continue;
    }

    var hits = level.Hits();
    read++;
    foreach (var (variant, options) in variants)
    {
        options.Keys = keys;
        var notes = NotePlanner.Plan(hits, options);
        var text = new StringBuilder();
        foreach (var n in notes)
        {
            text.Append(n.TileIndex).Append(':').Append(n.LastTileIndex).Append(':').Append(n.Time.ToString("R"));
            foreach (var kv in n.Lanes)
            {
                text.Append(' ').Append(kv.Key).Append('=').Append(kv.Value.ToString("R"));
            }

            text.Append(';');
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
        output.WriteLine($"{name}\t{variant}\t{notes.Count}\t{hash}");
    }
}

Console.WriteLine($"读了 {read} 关（读不了 {failed} 关），每关 {variants.Count} 种设置，{keys} 键 → {args[0]}");
return 0;
