using System;
using System.Collections.Generic;
using System.Linq;

namespace RhythmHint.Core;

/// <summary>专属手序套到当前关卡上的结果。</summary>
public sealed class FingeringMatch
{
    internal FingeringMatch(List<PlannedNote> notes, List<bool> fromAuto, int matchedHits, int totalHits)
    {
        Notes = notes;
        FromAuto = fromAuto;
        MatchedHits = matchedHits;
        TotalHits = totalHits;
        for (var i = 0; i < fromAuto.Count; i++)
        {
            if (!fromAuto[i])
            {
                continue;
            }

            FallbackNotes++;
            if (i == 0 || !fromAuto[i - 1])
            {
                FallbackSections++;
            }
        }
    }

    /// <summary>最终要画的音符：对上的用专属手序，对不上的用自动排键。每块砖正好属于一个音符。</summary>
    public List<PlannedNote> Notes { get; }

    /// <summary>和 <see cref="Notes"/> 一一对应：这个音符是不是用自动排键补上的。</summary>
    public List<bool> FromAuto { get; }

    /// <summary>用上专属手序的砖块数。</summary>
    public int MatchedHits { get; }

    public int TotalHits { get; }

    /// <summary>用自动排键补上的音符数、连成几段。</summary>
    public int FallbackNotes { get; }

    public int FallbackSections { get; }

    /// <summary>本关有多少比例的砖用上了专属手序。</summary>
    public double Ratio => TotalHits == 0 ? 0 : (double)MatchedHits / TotalHits;

    /// <summary><see cref="Notes"/> 是几键的（这一关按几键显示）。</summary>
    public int Keys { get; internal set; } = CustomFingering.DefaultKeys;
}

/// <summary>文件夹里的一份手序和它的文件名。</summary>
public sealed record FingeringCandidate(string Name, CustomFingering File);

/// <summary>认出这一关用哪份手序的方式。</summary>
public enum FingeringReason
{
    /// <summary>没有像这一关的手序，用自动排键。</summary>
    None,

    /// <summary>谱面指纹完全一样：就是做手序时的那一版。</summary>
    Exact,

    /// <summary>谱面更新过，按节奏对齐跟上（对不上的地方用自动排键）。</summary>
    Followed,

    /// <summary>玩家之前选过这一关用哪份（或者选了都不用）。</summary>
    Chosen,

    /// <summary>有几份手序都像这一关，或者像但不够确定：不猜，等玩家选。</summary>
    Unsure,
}

/// <summary>从几份手序里给当前关卡挑出来的结果。</summary>
public sealed class FingeringChoice
{
    public FingeringReason Reason { get; internal set; }

    /// <summary>用上的那份；没有能用的为 null。</summary>
    public FingeringCandidate? Used { get; internal set; }

    public FingeringMatch? Match { get; internal set; }

    /// <summary>本关的谱面指纹（玩家选了以后按它记住）。</summary>
    public string Chart { get; internal set; } = string.Empty;

    /// <summary>像这一关的手序和对上比例，从高到低（Unsure 时给玩家选）。</summary>
    public List<(FingeringCandidate Candidate, int MatchedHits, int TotalHits)> Candidates { get; } = new();

    /// <summary>像这一关、但键数比这一关显示的多（或是奇数键）的手序：不套用，状态里说明。</summary>
    public List<FingeringCandidate> OtherKeys { get; } = new();

    /// <summary>当成一份的重复文件（日志用）：内容一样的几份，或同一位作者给这一版谱面存过好几次。</summary>
    public List<FingeringMerge> Merged { get; } = new();
}

/// <summary>重复的手序文件没参与挑选：<paramref name="Dropped"/> 和 <paramref name="Kept"/> 内容一样（<paramref name="SameContent"/>），或是同一位作者较早存的。</summary>
public sealed record FingeringMerge(string Dropped, string Kept, bool SameContent);

/// <summary>
/// 把专属手序对到当前关卡上（手序跟着歌走）：关卡更新后砖块可能增删、个别砖改了角度、整体偏移变了，
/// 这里按节奏把两边的砖一一对齐，对上的砖用专属手序，对不上的退回自动排键。
/// <para>
/// 比的是每块砖「和前一块砖的间隔、转角、有没有长按」，不看绝对时间和砖号：改偏移不影响，
/// 某块砖改了角度只影响那一块，中间加删的砖只影响那几块。对齐用最长公共子序列（限制在对角线附近的一条带里算，
/// 四千块砖约几百万次比较；一样长时选断开最少的，见 Align），节奏逐块一样时直接一一对应。
/// </para>
/// </summary>
public static class FingeringMatcher
{
    /// <summary>
    /// 谱面指纹对不上时，对上这么多才自动用（谱面小改后照样跟上）。不看曲名：同一首歌常有好几个谱师的版本，
    /// 用户创意工坊里两首 deleted 互相只对上 2%–6%，同一张谱改偏移、改一块砖后仍有 99.7% 以上。
    /// </summary>
    public const double MinRatio = 0.8;

    /// <summary>第一名要比第二名多对上这么多才自动用，否则不猜、让玩家选。</summary>
    public const double MinLead = 0.1;

    /// <summary>对上这么多以上（但不够自动用）才列给玩家选，再低就当不是这一关。</summary>
    public const double MinCandidate = 0.3;

    /// <summary>
    /// 只认连续对上这么多块砖的整段。节奏只有几种取值，两首无关的歌零散地也能「对上」一半以上（最长公共子序列的性质），
    /// 真正同一首歌更新前后是一长段一长段地对上。
    /// </summary>
    public const int MinRun = 8;

    /// <summary>对齐带宽至少这么多块砖（中间整段插入、删掉时路径会偏离对角线）。</summary>
    private const int MinBand = 300;

    /// <summary>带里最多算这么多格，再多就把带收窄（极长的关卡）。</summary>
    private const long MaxCells = 30_000_000;

    /// <summary>对齐打分里「走不到」：比任何走得到的分数都小很多，减几次 1 也不会和走得到的混。</summary>
    private const long Unreachable = long.MinValue / 4;

    /// <summary>对上一块砖加的分：放在高 32 位，段数（最多几十万）在低位，先比砖数再比段数。</summary>
    private const long Matched = 1L << 32;

    /// <summary>对齐时每格记的一个字节：低 2 位是「这一步对不上」怎么走（斜着走、跳过本关的砖、跳过文件里的砖；0 是走不到）。</summary>
    private const int StepDiag = 1, StepUp = 2, StepLeft = 3, StepMask = 3;

    /// <summary>这一格的 T（接着对上用）、W（接着对不上用）取的是「最后一步对上」那个状态。</summary>
    private const int TFromM = 4, WFromM = 8;

    /// <summary>当前关卡每块砖对应文件里的第几块砖，对不上为 -1。</summary>
    public static int[] Align(IReadOnlyList<TileHit> current, IReadOnlyList<TileHit> file) => Align(new Rhythm(current), new Rhythm(file));

    /// <summary>
    /// 本关最多能有多少块砖对上这份文件的砖（上限：<see cref="Apply"/> 的 MatchedHits 不会超过它）。很便宜（几毫秒），
    /// 用来在对齐之前跳过不可能对上的文件（BPM、节奏不一样的歌）。做法见 Rhythm.Bound。
    /// </summary>
    public static int MatchBound(IReadOnlyList<TileHit> current, IReadOnlyList<TileHit> file) => Rhythm.Bound(new Rhythm(current), new Rhythm(file), MinRun);

    /// <summary>
    /// 带状最长公共子序列，一样长时选「断开」次数最少的那条（2026-11-19 评审 A 高1）。
    /// <para>
    /// 一串一样的砖（均匀的连打）里，最长公共子序列有很多条一样长的答案：改了一块砖的转角，「前面整段对到文件的下一块、
    /// 在改动处和开头各跳过一块」和「改动的那块原地对不上」一样长。原来平手时先选跳过，整段的键就反了相位。
    /// 所以每条路线再数一数有几段对不上（连在一起的跳过、对不上算一段，开头、结尾的也算）：先比对上的砖数，一样多时段数少的赢。
    /// 改一块砖是原地一段，加删砖时错开的地方贴着改动处（离开改动处再错开要多一段）。最长公共子序列的长度和原来完全一样，
    /// 只是断开少了、连续的整段更长（工坊和 TUF 谱上随机改、加、删一块砖实测，<see cref="KeepLongRuns"/> 之后对上的砖没有一次变少）。
    /// 加删的砖前后节奏完全接得上时（比如一串一样的砖里删一块、后面整体前移），只看节奏分不出动的是哪一块，错开的位置还是只能定在一串的一头。
    /// </para>
    /// <para>
    /// 每格记两个状态：最后一步是对上（M），还是对不上（X：斜着走但不算对上、跳过本关的砖、跳过文件里的砖）。
    /// 分数 = 对上的砖数 × 2^32 − 段数，比大小就是先比砖数再比段数。平手时先选留在对角线上（斜着走），再是跳过本关的砖、跳过文件里的砖。
    /// </para>
    /// </summary>
    private static int[] Align(Rhythm current, Rhythm file)
    {
        int n = current.Count, m = file.Count;
        var map = new int[n];
        for (var i = 0; i < n; i++)
        {
            map[i] = -1;
        }

        if (n == 0 || m == 0)
        {
            return map;
        }

        var band = Math.Max(MinBand, Math.Abs(n - m) + 150);
        if ((long)(n + 1) * ((2 * band) + 1) > MaxCells)
        {
            band = Math.Max(1, (int)((MaxCells / (n + 1)) - 1) / 2);
        }

        var width = (2 * band) + 1;

        // 每格两个状态的分数：M = 最后一步对上，X = 最后一步对不上。下一步只用到两个组合，所以每格只留这两个：
        // T = max(M, X)（下一步对上，从哪个状态来都一样），W = max(M − 1, X)（下一步对不上：从 M 来是新的一段）。同分都取 M
        var dir = new byte[(long)(n + 1) * width];
        long[] prevT = new long[width], prevW = new long[width], curT = new long[width], curW = new long[width];

        int Start(int i) => (int)Math.Round((double)i * m / n) - band;

        // 第 0 行：还没用本关的砖。起点算「刚对上」，开头跳过文件里的砖是一段
        var s0 = Start(0);
        for (var k = 0; k < width; k++)
        {
            var j = s0 + k;
            prevT[k] = j == 0 ? 0 : j > 0 && j <= m ? -1 : Unreachable;
            prevW[k] = j >= 0 && j <= m ? -1 : Unreachable;
        }

        for (var i = 1; i <= n; i++)
        {
            // 上一行同一列在 prev 里的位置 = k + shift（每一行的带子起点随对角线挪动）
            int s = Start(i), shift = s - Start(i - 1);
            var row = (long)i * width;
            var leftW = Unreachable;
            bool hold = current.Hold[i - 1], first = i == 1;
            double angle = current.Angle[i - 1], gap = current.Gap[i - 1];
            for (var k = 0; k < width; k++)
            {
                var j = s + k;
                if (j < 0 || j > m)
                {
                    curT[k] = curW[k] = leftW = Unreachable;
                    continue;
                }

                var u = k + shift;
                var upW = u >= 0 && u < width ? prevW[u] : Unreachable;
                long diagT = Unreachable, diagW = Unreachable;
                if (u >= 1 && u <= width)
                {
                    diagT = prevT[u - 1];
                    diagW = prevW[u - 1];
                }

                // M：这一步对上（斜着走、两块算同一块）
                var scoreM = j > 0 && diagT > Unreachable && Rhythm.Same(hold, angle, gap, first, file, j - 1) ? diagT + Matched : Unreachable;

                // X：这一步对不上，同分时斜着走、跳过本关的砖、跳过文件里的砖依次优先
                var scoreX = diagW;
                var step = StepDiag;
                if (upW > scoreX)
                {
                    scoreX = upW;
                    step = StepUp;
                }

                if (leftW > scoreX)
                {
                    scoreX = leftW;
                    step = StepLeft;
                }

                if (scoreX == Unreachable)
                {
                    step = 0;
                }

                bool tFromM = scoreM >= scoreX, wFromM = scoreM - 1 >= scoreX;
                curT[k] = tFromM ? scoreM : scoreX;
                curW[k] = leftW = wFromM ? scoreM - 1 : scoreX;
                dir[row + k] = (byte)(step | (tFromM ? TFromM : 0) | (wFromM ? WFromM : 0));
            }

            (prevT, curT) = (curT, prevT);
            (prevW, curW) = (curW, prevW);
        }

        // 从右下角往回走，记下对上的砖。到每一格时，后一步用的是这一格的 T 还是 W 决定这一格在哪个状态
        int x = n, y = m;
        var useT = true;
        while (x > 0 && y > 0)
        {
            var k = y - Start(x);
            if (k < 0 || k >= width)
            {
                break;
            }

            var d = dir[((long)x * width) + k];
            if ((d & (useT ? TFromM : WFromM)) != 0)
            {
                map[x - 1] = y - 1;
                x--;
                y--;
                useT = true;
                continue;
            }

            switch (d & StepMask)
            {
                case StepDiag:
                    x--;
                    y--;
                    break;
                case StepUp:
                    x--;
                    break;
                case StepLeft:
                    y--;
                    break;
                default:
                    return map;
            }

            useT = false;
        }

        return map;
    }

    /// <summary>
    /// 本关每块砖用文件里的第几块砖（对不上为 -1），只留两边都连续 <see cref="MinRun"/> 块以上的整段。
    /// <para>
    /// 两边一样多、第 i 块和第 i 块都算同一块时直接一一对应，不做带状对齐（2026-11-19 评审 A：3.5 万块的谱指纹一样也要算 140 毫秒，
    /// 同一首歌好几份手序时进关卡要等好几倍）。结果和对齐完全一样：这时最长公共子序列是全部 n 块，长度 n 的公共子序列只有一一对应这一条，
    /// 对角线又在带的正中间，<see cref="Align(Rhythm, Rhythm)"/> 算出来一定也是它。谱面指纹一样的都走这里：指纹把间隔取整到毫秒、
    /// 转角取整到度，取整后一样 ⇒ 间隔差不超过 1 毫秒、转角差不超过 1°、长按一样，都在 <see cref="Rhythm.Same(Rhythm, int, Rhythm, int)"/>
    /// 的容差里。这里直接逐块比、不看指纹，结果不依赖指纹的取整规则。
    /// </para>
    /// </summary>
    private static int[] Map(Rhythm current, Rhythm file)
    {
        if (!Rhythm.SameOneByOne(current, file))
        {
            return KeepLongRuns(Align(current, file));
        }

        var map = new int[current.Count];
        for (var i = 0; i < map.Length; i++)
        {
            map[i] = i;
        }

        return KeepLongRuns(map);
    }

    /// <summary>
    /// 把一份手序套到当前关卡：对上的砖用专属手序的键（作者和玩家惯用手不同时左右镜像，长按按比例换成本关的时长），
    /// 其余用 <paramref name="auto"/>（同一关的自动排键）。一个自动音符只有一部分砖对上时，压在它上面的专属音符也退回自动，
    /// 保证每块砖正好属于一个音符。键数不同（以后的六键、八键）时整首用自动。
    /// </summary>
    /// <param name="auto">这一关按 <paramref name="keys"/> 键自动排的音符（对不上的地方用它补）。</param>
    /// <param name="keys">这一关按几键显示。文件的键数一样就照文件；更少（偶数键）就同一只手、同一根手指放进来（4K 手序在 8K 里是中间四个键）；
    /// 更多或奇数键的不套用，整关用自动排键。</param>
    public static FingeringMatch Apply(CustomFingering file, IReadOnlyList<TileHit> hits, IReadOnlyList<PlannedNote> auto, bool leftHanded,
        int keys = CustomFingering.DefaultKeys) =>
        Apply(file, hits, auto, leftHanded, new Rhythm(hits), null, keys);

    /// <summary>键数为 <paramref name="fileKeys"/> 的手序能不能按 <paramref name="keys"/> 键显示：偶数键、不比它多。</summary>
    public static bool Fits(int fileKeys, int keys) => KeyLayout.IsSupported(fileKeys) && fileKeys <= keys;

    private static FingeringMatch Apply(CustomFingering file, IReadOnlyList<TileHit> hits, IReadOnlyList<PlannedNote> auto, bool leftHanded,
        Rhythm rhythm, Rhythm? fileRhythm, int keys)
    {
        var layout = KeyLayout.Of(keys);
        var n = hits.Count;
        var curIndex = IndexByTile(hits);
        var autoRange = new List<(int First, int Last)>(auto.Count);
        var autoOf = new int[n];
        for (var a = 0; a < auto.Count; a++)
        {
            var range = (curIndex[auto[a].TileIndex], curIndex[auto[a].LastTileIndex]);
            autoRange.Add(range);
            for (var i = range.Item1; i <= range.Item2; i++)
            {
                autoOf[i] = a;
            }
        }

        var customs = new List<(int First, int Last, PlannedNote Source, int FileFirst)>();
        var claimedBy = new int[n];
        for (var i = 0; i < n; i++)
        {
            claimedBy[i] = -1;
        }

        if (Fits(file.Keys, keys))
        {
            var map = Map(rhythm, fileRhythm ?? new Rhythm(file.Hits));
            var toCur = new int[file.Hits.Count];
            for (var j = 0; j < toCur.Length; j++)
            {
                toCur[j] = -1;
            }

            for (var i = 0; i < n; i++)
            {
                if (map[i] >= 0)
                {
                    toCur[map[i]] = i;
                }
            }

            var fileIndex = IndexByTile(file.Hits);
            foreach (var note in file.Notes)
            {
                int fa = fileIndex[note.TileIndex], fb = fileIndex[note.LastTileIndex];
                var first = toCur[fa];
                var ok = first >= 0;
                for (var f = fa + 1; ok && f <= fb; f++)
                {
                    ok = toCur[f] == first + (f - fa);
                }

                if (!ok)
                {
                    continue;
                }

                var c = customs.Count;
                customs.Add((first, first + (fb - fa), note, fa));
                for (var i = first; i <= first + (fb - fa); i++)
                {
                    claimedBy[i] = c;
                }
            }

            // 一个自动音符被认领了一部分：这几块砖的分组两边不一样，压在上面的专属音符退回自动，直到没有这种情况
            for (var changed = true; changed;)
            {
                changed = false;
                foreach (var (first, last) in autoRange)
                {
                    var claimed = 0;
                    for (var i = first; i <= last; i++)
                    {
                        claimed += claimedBy[i] >= 0 ? 1 : 0;
                    }

                    if (claimed == 0 || claimed == last - first + 1)
                    {
                        continue;
                    }

                    for (var i = first; i <= last; i++)
                    {
                        if (claimedBy[i] >= 0)
                        {
                            var (cf, cl, _, _) = customs[claimedBy[i]];
                            for (var h = cf; h <= cl; h++)
                            {
                                claimedBy[h] = -1;
                            }

                            changed = true;
                        }
                    }
                }
            }
        }

        var notes = new List<PlannedNote>(auto.Count);
        var fromAuto = new List<bool>(auto.Count);
        var matched = 0;
        var mirror = file.LeftHanded != leftHanded;
        for (var i = 0; i < n;)
        {
            if (claimedBy[i] >= 0)
            {
                var (first, last, source, fileFirst) = customs[claimedBy[i]];
                var note = Place(source, file, fileFirst, hits, first, last, mirror, layout);
                FitTaps(note, hits, first, last, leftHanded, layout);
                notes.Add(note);
                fromAuto.Add(false);
                matched += last - first + 1;
                i = last + 1;
            }
            else
            {
                var a = autoOf[i];
                notes.Add(auto[a].Clone());
                fromAuto.Add(true);
                i = autoRange[a].Last + 1;
            }
        }

        return new FingeringMatch(notes, fromAuto, matched, n) { Keys = keys };
    }

    /// <summary>
    /// 从文件夹里的几份手序里挑这一关用哪份，名字不参与判断，只看谱面本身：
    /// <list type="number">
    /// <item>玩家选过（<paramref name="chosen"/>：文件名，空串表示选了都不用）就照玩家的；文件不在了当没选过</item>
    /// <item>谱面指纹完全一样：直接用（同一张谱，换文件名、换电脑都一样）</item>
    /// <item>否则按节奏对齐：对上不低于 <see cref="MinRatio"/> 且比第二名多 <see cref="MinLead"/> 才自动用</item>
    /// <item>有像的但不够确定（不低于 <see cref="MinCandidate"/>）：不猜，列出来让玩家选</item>
    /// </list>
    /// 挑之前先把重复的当成一份（2026-11-19 评审 A：同一张谱两份就永远「不确定」）：内容一样的（浏览器重复下载出的「(1)」）只留一份；
    /// 指纹完全一样的几份里，同一位作者的只留 updatedAt 最新的。键数显示不了的（比这一关多、奇数键）不套用，像这一关的记进 <see cref="FingeringChoice.OtherKeys"/>。
    /// 同一张谱有几种键数的手序时，先只看和 <paramref name="keys"/> 一样的，挑不出来再看全部。
    /// </summary>
    /// <param name="autoFor">按几键自动排的音符（对不上的地方用它补；只会要 4、6、8）。</param>
    /// <param name="chosen">玩家给这个谱面指纹选过的文件名；没选过为 null。</param>
    /// <param name="keys">这一关按几键显示（手动选的键数，或自动模式下按难度选的）。</param>
    /// <param name="followFile">自动模式：用上的手序是 4、6、8 键时按它的键数显示（2026-11-22 用户：键数听手序文件的）。</param>
    public static FingeringChoice Choose(IReadOnlyList<FingeringCandidate> candidates, IReadOnlyList<TileHit> hits,
        Func<int, IReadOnlyList<PlannedNote>> autoFor, bool leftHanded, string? chosen, int keys, bool followFile)
    {
        var chart = CustomFingering.ChartPrint(hits);
        var choice = new FingeringChoice { Chart = chart };
        var rhythm = new Rhythm(hits);
        int ShownKeys(CustomFingering file) => followFile && KeyLayout.IsMode(file.Keys) ? file.Keys : keys;
        FingeringMatch ApplyTo(FingeringCandidate c, Rhythm? fileRhythm) =>
            Apply(c.File, hits, autoFor(ShownKeys(c.File)), leftHanded, rhythm, fileRhythm, ShownKeys(c.File));
        var usable = new List<FingeringCandidate>();
        foreach (var c in candidates)
        {
            if (Fits(c.File.Keys, ShownKeys(c.File)))
            {
                usable.Add(c);
            }
            else if (LooksLike(c.File, chart, rhythm))
            {
                choice.OtherKeys.Add(c);
            }
        }

        if (chosen != null)
        {
            if (chosen.Length == 0)
            {
                choice.Reason = FingeringReason.Chosen;
                return choice;
            }

            var picked = usable.FirstOrDefault(c => string.Equals(c.Name, chosen, StringComparison.OrdinalIgnoreCase));
            if (picked != null)
            {
                return Use(choice, picked, ApplyTo(picked, null), FingeringReason.Chosen);
            }
        }

        usable = WithoutCopies(usable, choice.Merged);
        var sameKeys = usable.FindAll(c => c.File.Keys == keys);
        if (sameKeys.Count > 0 && sameKeys.Count < usable.Count)
        {
            var first = new FingeringChoice { Chart = chart };
            Pick(first, sameKeys, chart, ApplyTo, rhythm);
            if (first.Used != null)
            {
                first.OtherKeys.AddRange(choice.OtherKeys);
                first.Merged.InsertRange(0, choice.Merged);
                return first;
            }
        }

        return Pick(choice, usable, chart, ApplyTo, rhythm);
    }

    /// <summary>
    /// 兼容旧的调用（测试）：按 4 键显示、键数固定。
    /// </summary>
    public static FingeringChoice Choose(IReadOnlyList<FingeringCandidate> candidates, IReadOnlyList<TileHit> hits,
        IReadOnlyList<PlannedNote> auto, bool leftHanded, string? chosen = null) =>
        Choose(candidates, hits, _ => auto, leftHanded, chosen, CustomFingering.DefaultKeys, followFile: false);

    /// <summary>在 pool 里挑：指纹完全一样的只有一份（同一位作者的只留最新）就用它，否则按节奏对齐打分。</summary>
    private static FingeringChoice Pick(FingeringChoice choice, List<FingeringCandidate> pool, string chart,
        Func<FingeringCandidate, Rhythm?, FingeringMatch> applyTo, Rhythm rhythm)
    {
        var exact = NewestOfEachAuthor(pool.FindAll(c => c.File.Charts.Contains(chart)), choice.Merged);
        if (exact.Count == 1)
        {
            return Use(choice, exact[0], applyTo(exact[0], null), FingeringReason.Exact);
        }

        return Score(choice, exact.Count > 1 ? exact : pool, exact.Count > 1, applyTo, rhythm);
    }

    /// <summary>按节奏对齐给候选打分；不可能对上 <see cref="MinCandidate"/> 的（砖数差太多、节奏片段对不上）先跳过，不做对齐。</summary>
    private static FingeringChoice Score(FingeringChoice choice, List<FingeringCandidate> pool, bool allExact,
        Func<FingeringCandidate, Rhythm?, FingeringMatch> applyTo, Rhythm rhythm)
    {
        var scored = new List<(FingeringCandidate Candidate, FingeringMatch Match)>();
        foreach (var c in pool)
        {
            var fileRhythm = CouldMatch(c.File, rhythm);
            if (fileRhythm != null)
            {
                var match = applyTo(c, fileRhythm);
                if (match.Ratio >= MinCandidate)
                {
                    scored.Add((c, match));
                }
            }
        }

        scored = scored.OrderByDescending(x => x.Match.Ratio).ThenByDescending(x => x.Candidate.File.UpdatedAt).ToList();
        foreach (var x in scored)
        {
            choice.Candidates.Add((x.Candidate, x.Match.MatchedHits, x.Match.TotalHits));
        }

        if (scored.Count == 0)
        {
            choice.Reason = FingeringReason.None;
            return choice;
        }

        var best = scored[0];
        var lead = scored.Count > 1 ? best.Match.Ratio - scored[1].Match.Ratio : 1;
        if (!allExact && best.Match.Ratio >= MinRatio && lead >= MinLead)
        {
            return Use(choice, best.Candidate, best.Match, FingeringReason.Followed);
        }

        choice.Reason = FingeringReason.Unsure;
        return choice;
    }

    /// <summary>
    /// 这份文件有没有可能对上本关至少 <see cref="MinCandidate"/>：砖数差太多的、节奏片段对不上的（<see cref="MatchBound"/>）不可能。
    /// 只是跳过注定对不上的，不改结果。有可能时返回文件的节奏（对齐接着用），不可能为 null。
    /// 节奏逐块一样的（指纹一样的几份手序）不用估：每个片段都对得上，上限就是全部砖（20 万块的谱估一次要 100 毫秒）。
    /// </summary>
    private static Rhythm? CouldMatch(CustomFingering file, Rhythm rhythm)
    {
        var n = rhythm.Count;
        if (Math.Min(file.Hits.Count, n) < MinCandidate * n)
        {
            return null;
        }

        var fileRhythm = new Rhythm(file.Hits);
        return Rhythm.SameOneByOne(rhythm, fileRhythm) || Rhythm.Bound(rhythm, fileRhythm, MinRun) >= MinCandidate * n ? fileRhythm : null;
    }

    /// <summary>键数不同的文件像不像这一关：谱面指纹一样，或者按节奏对齐能对上 <see cref="MinCandidate"/> 以上。</summary>
    private static bool LooksLike(CustomFingering file, string chart, Rhythm rhythm)
    {
        if (file.Charts.Contains(chart))
        {
            return true;
        }

        var fileRhythm = CouldMatch(file, rhythm);
        return fileRhythm != null && Map(rhythm, fileRhythm).Count(i => i >= 0) >= MinCandidate * rhythm.Count;
    }

    /// <summary>
    /// 内容一样的几份（键数、惯用手、砖块、每个音符的键和长按都一样，套到关卡上结果一定一样）只留一份：updatedAt 新的，再是名字短的
    /// （「Song.json」而不是浏览器重复下载出的「Song (1).json」）。保留原来的顺序。
    /// </summary>
    private static List<FingeringCandidate> WithoutCopies(List<FingeringCandidate> candidates, List<FingeringMerge> merged)
    {
        var kept = new List<FingeringCandidate>();
        foreach (var c in candidates.OrderByDescending(c => c.File.UpdatedAt).ThenBy(c => c.Name.Length).ThenBy(c => c.Name, StringComparer.Ordinal))
        {
            var same = kept.Find(k => SamePlay(k.File, c.File));
            if (same == null)
            {
                kept.Add(c);
            }
            else
            {
                merged.Add(new FingeringMerge(c.Name, same.Name, SameContent: true));
            }
        }

        return candidates.FindAll(kept.Contains);
    }

    /// <summary>
    /// 指纹完全一样的几份里，同一位作者（去掉标签、首尾空白，不分大小写）的只留 updatedAt 最新的那份。
    /// 没写作者的不算同一位；最新的有好几份一样新时分不出来，这一位的都留着（照旧让玩家选）。
    /// </summary>
    private static List<FingeringCandidate> NewestOfEachAuthor(List<FingeringCandidate> exact, List<FingeringMerge> merged)
    {
        var result = new List<FingeringCandidate>(exact);
        foreach (var group in exact.GroupBy(c => CustomFingering.PlainText(c.File.Author).ToLowerInvariant()).Where(g => g.Key.Length > 0))
        {
            var newest = group.Max(c => c.File.UpdatedAt);
            var winners = group.Where(c => c.File.UpdatedAt == newest).ToList();
            if (winners.Count != 1)
            {
                continue;
            }

            foreach (var older in group.Where(c => !ReferenceEquals(c, winners[0])))
            {
                result.Remove(older);
                merged.Add(new FingeringMerge(older.Name, winners[0].Name, SameContent: false));
            }
        }

        return result;
    }

    /// <summary>套到关卡上结果一定一样：键数、惯用手、每块砖（砖号、时间、长按、转角）、每个音符的砖和键、长按都一样。</summary>
    private static bool SamePlay(CustomFingering a, CustomFingering b)
    {
        if (a.Keys != b.Keys || a.LeftHanded != b.LeftHanded || a.Hits.Count != b.Hits.Count || a.Notes.Count != b.Notes.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Hits.Count; i++)
        {
            if (!a.Hits[i].Equals(b.Hits[i]))
            {
                return false;
            }
        }

        for (var i = 0; i < a.Notes.Count; i++)
        {
            PlannedNote x = a.Notes[i], y = b.Notes[i];
            if (x.TileIndex != y.TileIndex || x.LastTileIndex != y.LastTileIndex || !x.Lanes.SequenceEqual(y.Lanes))
            {
                return false;
            }
        }

        return true;
    }

    private static FingeringChoice Use(FingeringChoice choice, FingeringCandidate candidate, FingeringMatch match, FingeringReason reason)
    {
        choice.Used = candidate;
        choice.Match = match;
        choice.Reason = reason;
        return choice;
    }

    /// <summary>去掉短于 <see cref="MinRun"/> 的零散对应（两边都连续的才算一段）。</summary>
    private static int[] KeepLongRuns(int[] map)
    {
        for (var start = 0; start < map.Length;)
        {
            if (map[start] < 0)
            {
                start++;
                continue;
            }

            var end = start;
            while (end + 1 < map.Length && map[end + 1] == map[end] + 1)
            {
                end++;
            }

            if (end - start + 1 < MinRun)
            {
                for (var i = start; i <= end; i++)
                {
                    map[i] = -1;
                }
            }

            start = end + 1;
        }

        return map;
    }

    /// <summary>文件里的一个音符放到本关：作者和玩家惯用手不同时按文件的键数镜像，键数比本关少时同一只手、同一根手指放进来。</summary>
    private static PlannedNote Place(PlannedNote source, CustomFingering file, int fileFirst, IReadOnlyList<TileHit> hits, int first, int last, bool mirror,
        KeyLayout layout)
    {
        var fileLayout = KeyLayout.Of(file.Keys);
        var note = new PlannedNote(hits[first].TileIndex, hits[first].Time) { LastTileIndex = hits[last].TileIndex };

        // 长按：文件里这几块砖的长按总时长换算到本关（谱面变速后长按跟着变）
        double fileHold = 0, curHold = 0;
        for (var f = fileFirst; f < file.Hits.Count && file.Hits[f].TileIndex <= source.LastTileIndex; f++)
        {
            fileHold += file.Hits[f].HoldSeconds;
        }

        for (var i = first; i <= last; i++)
        {
            curHold += hits[i].HoldSeconds;
        }

        foreach (var kv in source.Lanes)
        {
            var lane = layout.FromLayout(fileLayout, mirror ? fileLayout.Mirror(kv.Key) : kv.Key);
            note.Lanes[lane] = kv.Value > 0 && fileHold > 0 ? kv.Value * curHold / fileHold : 0;
        }

        return note;
    }

    /// <summary>
    /// 盖着双押砖的专属音符：键数补到 / 减到这几块砖现在一共要按几下（最多是这一关的键数）。手序可能是在另一种游戏设置下做的
    /// （设成「双押砖只按一下」时双押砖上是单押）：少了游戏不前进；多了多按的那下打到下一块砖。
    /// 补键先补已有键同一只手的别的手指（双押同手，从食指往外），还不够再按「惯用手食指、另一只手食指、惯用手中指……」的顺序补；
    /// 减键先去最外侧的手指，最后留惯用手食指。不盖双押砖的音符不动（第三方工具在普通砖上写的多个键照旧）。
    /// </summary>
    private static void FitTaps(PlannedNote note, IReadOnlyList<TileHit> hits, int first, int last, bool leftHanded, KeyLayout layout)
    {
        var multitap = false;
        var need = 0;
        for (var i = first; i <= last; i++)
        {
            multitap |= hits[i].Multitap;
            need += Math.Max(1, hits[i].Taps);
        }

        if (!multitap)
        {
            return;
        }

        need = Math.Min(need, layout.Count);
        var order = TapOrder(layout, leftHanded);
        for (var k = order.Length - 1; k >= 0 && note.Lanes.Count > need; k--)
        {
            note.Lanes.Remove(order[k]);
        }

        var hold = note.Lanes.Values.DefaultIfEmpty(0).Max();
        while (note.Lanes.Count < need)
        {
            var sameHand = order.Where(note.Lanes.ContainsKey)
                .SelectMany(lane => Enumerable.Range(0, layout.FingersPerHand).Select(finger => layout.Lane(layout.HandOf(lane), finger)))
                .Where(l => !note.Lanes.ContainsKey(l));
            note.Lanes[sameHand.Concat(order.Where(l => !note.Lanes.ContainsKey(l))).First()] = hold;
        }
    }

    /// <summary>补键的先后：惯用手食指、另一只手食指、惯用手中指、另一只手中指……（4K 右手为主是 J F K D）。</summary>
    internal static int[] TapOrder(KeyLayout layout, bool leftHanded)
    {
        var main = leftHanded ? 0 : 1;
        return Enumerable.Range(0, layout.FingersPerHand).SelectMany(finger => new[] { layout.Lane(main, finger), layout.Lane(1 - main, finger) }).ToArray();
    }

    private static Dictionary<int, int> IndexByTile(IReadOnlyList<TileHit> hits)
    {
        var index = new Dictionary<int, int>(hits.Count);
        for (var i = 0; i < hits.Count; i++)
        {
            index[hits[i].TileIndex] = i;
        }

        return index;
    }
}
