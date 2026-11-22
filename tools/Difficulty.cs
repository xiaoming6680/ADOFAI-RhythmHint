#:project ../src/RhythmHint.Reference/RhythmHint.Reference.csproj
#:property TargetFramework=net10.0
#:property TreatWarningsAsErrors=false
#:property PublishAot=false

// 难度估算的离线校准：拿一批已有 TUF 评级的谱面，看估算和评级差多少。
// 谱面和评级不进仓库（版权 / 第三方数据），放在仓库外的文件夹里：
//   <文件夹>/index.json      { "<id>": { "tier": "G12", "song": ..., ... }, ... }
//   <文件夹>/levels/<id>.adofai
// 用法：
//   dotnet run tools/Difficulty.cs -- export <文件夹> <输出.csv>    导出每关的特征和 TUF 档位，拟合系数用
//   dotnet run tools/Difficulty.cs -- hits <文件夹> <输出.json>      导出每关要按的砖（真实秒数、转角），试新特征用
//   dotnet run tools/Difficulty.cs -- evaluate <文件夹>              用 Core 的估算跑一遍，按档位列出误差
//       （模型就是用这批谱拟合的，这里是训练集误差，偏乐观；真实误差看 difficulty-fit.py 的交叉验证。
//        和 difficulty-fit.py 打印的「全部数据拟合」误差一致，说明 C# 的特征和模型实现没有偏差）
//   dotnet run tools/Difficulty.cs -- level <关卡.adofai>            估算一关并列出特征
//   dotnet run tools/Difficulty.cs -- keys <文件夹>                  按档位列出 6K、8K 会改动多少（定自动选键数的默认分界用）

using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RhythmHint.Core;
using RhythmHint.Reference;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
if (args.Length < 2)
{
    Console.Error.WriteLine("用法：dotnet run tools/Difficulty.cs -- export <文件夹> <输出.json> | evaluate <文件夹> | level <关卡.adofai>");
    return 2;
}

switch (args[0])
{
    case "export":
        return Export(args[1], args.Length > 2 ? args[2] : "hits.json");
    case "hits":
        return ExportHits(args[1], args.Length > 2 ? args[2] : "hits.json");
    case "evaluate":
        return Evaluate(args[1]);
    case "keys":
        return Keys(args[1]);
    case "level":
        return Level(args[1]);
    default:
        Console.Error.WriteLine($"不认识的命令：{args[0]}");
        return 2;
}

static IEnumerable<(string Id, string Tier, JObject Info, AdofaiLevel? Level, string? Error)> Load(string folder)
{
    var index = JObject.Parse(File.ReadAllText(Path.Combine(folder, "index.json")));
    foreach (var (id, token) in index)
    {
        var info = (JObject)token!;
        var tier = (string?)info["tier"] ?? "";
        AdofaiLevel? level = null;
        string? error = null;
        try
        {
            level = AdofaiLevelReader.Parse(File.ReadAllText(Path.Combine(folder, "levels", id + ".adofai")));
        }
        catch (Exception e) when (e is FormatException or JsonException or IOException or InvalidCastException or ArgumentException)
        {
            error = e.GetType().Name + ": " + e.Message;
        }

        yield return (id, tier, info, level, error);
    }
}

static int Export(string folder, string output)
{
    // 每关一行：编号、TUF 档位（1–60）、离线时间准不准、Core 算出的各项特征。拟合系数在仓库外用这个表做
    var lines = new List<string> { "id,tier,actual,exact,hits," + string.Join(",", DifficultyFeatures.Names) };
    int ok = 0, skipped = 0;
    foreach (var (id, tier, _, level, error) in Load(folder))
    {
        if (level == null)
        {
            Console.Error.WriteLine($"#{id} {tier}: 读不了（{error}）");
            skipped++;
            continue;
        }

        var hits = level.Hits();
        var features = DifficultyEstimator.Measure(hits, level.PlaybackRate);
        lines.Add($"{id},{tier},{DifficultyTiers.Parse(tier)},{(level.Unsupported.Count == 0 ? 1 : 0)},{hits.Count},"
            + string.Join(",", features.Values.Select(v => v.ToString("R", CultureInfo.InvariantCulture))));
        ok++;
    }

    File.WriteAllLines(output, lines);
    Console.WriteLine($"导出 {ok} 关，读不了 {skipped} 关 → {output}");
    return 0;
}

static int ExportHits(string folder, string output)
{
    // 每关要按的砖（按播放倍率换算成真实秒数）和转角，给分析脚本试新特征用
    var list = new JArray();
    foreach (var (id, tier, _, level, _) in Load(folder))
    {
        if (level == null)
        {
            continue;
        }

        var hits = level.Hits();
        list.Add(new JObject
        {
            ["id"] = id,
            ["tier"] = DifficultyTiers.Parse(tier),
            ["exact"] = level.Unsupported.Count == 0,
            ["t"] = new JArray(hits.Select(h => Math.Round(h.Time / level.PlaybackRate, 5))),
            ["a"] = new JArray(hits.Select(h => double.IsNaN(h.Angle) ? -1 : Math.Round(h.Angle, 3))),
            ["h"] = new JArray(hits.Select(h => Math.Round(h.HoldSeconds / level.PlaybackRate, 4))),
        });
    }

    File.WriteAllText(output, list.ToString(Formatting.None));
    Console.WriteLine($"导出 {list.Count} 关 → {output}");
    return 0;
}

static int Evaluate(string folder)
{
    var rows = new List<(string Id, string Tier, int Actual, double Score)>();
    var skipped = new Dictionary<string, int>();
    foreach (var (id, tier, _, level, error) in Load(folder))
    {
        var actual = DifficultyTiers.Parse(tier);
        if (level == null || actual == 0)
        {
            skipped[error == null ? "档位" : "读不了"] = skipped.TryGetValue(error == null ? "档位" : "读不了", out var n) ? n + 1 : 1;
            continue;
        }

        if (level.Unsupported.Count > 0)
        {
            skipped["时间不准"] = skipped.TryGetValue("时间不准", out var n) ? n + 1 : 1;
            continue;
        }

        var estimate = DifficultyEstimator.Estimate(level.Hits(), level.PlaybackRate);
        rows.Add((id, tier, actual, estimate.Score));
    }

    if (rows.Count == 0)
    {
        Console.Error.WriteLine("没有能用的关卡。");
        return 1;
    }

    var errors = rows.Select(r => Math.Round(r.Score) - r.Actual).ToList();
    Console.WriteLine($"能用 {rows.Count} 关；跳过 {string.Join("，", skipped.Select(kv => $"{kv.Key} {kv.Value}"))}");
    Console.WriteLine("（训练集误差，偏乐观；应和 difficulty-fit.py 的「全部数据拟合」一致）");
    Console.WriteLine($"平均差 {errors.Average(e => Math.Abs(e)):F2} 档；偏差 {errors.Average():+0.00;-0.00} 档");
    foreach (var within in new[] { 0, 1, 2, 3, 5, 8 })
    {
        Console.WriteLine($"  差 ≤ {within} 档：{errors.Count(e => Math.Abs(e) <= within) * 100.0 / errors.Count:F0}%");
    }

    Console.WriteLine("各大档（实际 → 平均估算，平均差）：");
    foreach (var group in rows.GroupBy(r => r.Tier[0]).OrderBy(g => "PGU".IndexOf(g.Key)))
    {
        Console.WriteLine($"  {group.Key}: {group.Count()} 关，平均差 {group.Average(r => Math.Abs(Math.Round(r.Score) - r.Actual)):F2}");
    }

    return 0;
}

// 按档位看 6K、8K 和 4K 差多少：每关按实际播放速度排键，数「同一根手指隔不到 R 又按」占所有按键的比例（6K、8K 多出来的手指就是为了减少它），
// 以及 6K、8K 改了几成音符的轨道。按真实 TUF 档位和估算档位各列一张表，每 5 档一行
static int Keys(string folder)
{
    var rows = new List<(int Actual, int Estimated, double Quick4, double Quick6, double Quick8, double Changed6, double Changed8, bool Exact)>();
    foreach (var (_, tier, _, level, _) in Load(folder))
    {
        if (level == null || DifficultyTiers.Parse(tier) == 0)
        {
            continue;
        }

        var rate = level.PlaybackRate;
        var hits = level.Hits().ConvertAll(h => h with { Time = h.Time / rate, HoldSeconds = h.HoldSeconds / rate });
        if (hits.Count < 2)
        {
            continue;
        }

        var options = new FingeringOptions();
        var four = NotePlanner.Plan(hits, options);
        var six = NotePlanner.Plan(hits, new FingeringOptions { Keys = 6 });
        var eight = NotePlanner.Plan(hits, new FingeringOptions { Keys = 8 });
        rows.Add((DifficultyTiers.Parse(tier), DifficultyEstimator.Estimate(level.Hits(), rate).Tier,
            QuickRepeats(four, options.ResetWindow), QuickRepeats(six, options.ResetWindow), QuickRepeats(eight, options.ResetWindow),
            Changed(four, six, KeyLayout.Six), Changed(four, eight, KeyLayout.Eight), level.Unsupported.Count == 0));
    }

    Console.WriteLine($"{rows.Count} 关（时间准的 {rows.Count(r => r.Exact)} 关）。同指 R 内 = 同一根手指隔不到 130 ms 又按，占全部按键；改动 = 轨道和 4K（放到中间四键）不一样的音符");
    foreach (var (name, key) in new (string, Func<(int Actual, int Estimated, double, double, double, double, double, bool), int>)[] { ("真实档位", r => r.Actual), ("估算档位", r => r.Estimated) })
    {
        Console.WriteLine();
        Console.WriteLine($"| {name} | 关数 | 4K 同指 R 内 | 6K 同指 R 内 | 8K 同指 R 内 | 6K 改动 | 8K 改动 | 6K 改动 ≥ 5% 的关 | 8K 改动 ≥ 5% 的关 |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var group in rows.GroupBy(r => (key(r) - 1) / 5).OrderBy(g => g.Key))
        {
            var g = group.ToList();
            var from = DifficultyTiers.Name((group.Key * 5) + 1);
            var to = DifficultyTiers.Name((group.Key * 5) + 5);
            Console.WriteLine($"| {from}–{to} | {g.Count} | {g.Average(r => r.Quick4):P1} | {g.Average(r => r.Quick6):P1} | {g.Average(r => r.Quick8):P1} | "
                + $"{g.Average(r => r.Changed6):P1} | {g.Average(r => r.Changed8):P1} | {g.Count(r => r.Changed6 >= 0.05)} | {g.Count(r => r.Changed8 >= 0.05)} |");
        }
    }

    return 0;
}

static double QuickRepeats(List<PlannedNote> notes, double resetWindow)
{
    var last = new Dictionary<int, double>();
    int quick = 0, presses = 0;
    foreach (var n in notes)
    {
        foreach (var lane in n.Lanes.Keys)
        {
            presses++;
            if (last.TryGetValue(lane, out var before) && n.Time - before <= resetWindow)
            {
                quick++;
            }

            last[lane] = n.Time;
        }
    }

    return presses == 0 ? 0 : (double)quick / presses;
}

static double Changed(List<PlannedNote> four, List<PlannedNote> many, KeyLayout layout)
{
    var changed = 0;
    for (var i = 0; i < four.Count; i++)
    {
        var mapped = four[i].Lanes.Keys.Select(l => layout.FromLayout(KeyLayout.Four, l));
        changed += mapped.SequenceEqual(many[i].Lanes.Keys) ? 0 : 1;
    }

    return four.Count == 0 ? 0 : (double)changed / four.Count;
}

static int Level(string path)
{
    var level = AdofaiLevelReader.Parse(File.ReadAllText(path));
    var hits = level.Hits();
    var estimate = DifficultyEstimator.Estimate(hits, level.PlaybackRate);
    Console.WriteLine($"{hits.Count} 次按键，播放倍率 {level.PlaybackRate:0.##}；估算 {DifficultyTiers.Name(estimate.Tier)}（{estimate.Score:F2}）");
    if (level.Unsupported.Count > 0)
    {
        Console.WriteLine($"离线时间可能不准：{string.Join("、", level.Unsupported)}");
    }

    foreach (var (name, value) in estimate.Features.Named())
    {
        Console.WriteLine($"  {name,-16} {value,10:F3}");
    }

    return 0;
}
