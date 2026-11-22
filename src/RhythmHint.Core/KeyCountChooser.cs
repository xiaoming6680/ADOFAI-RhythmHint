namespace RhythmHint.Core;

/// <summary>这一关为什么按这个键数显示（写进状态和日志）。</summary>
public enum KeyCountReason
{
    /// <summary>玩家在设置里选的键数。</summary>
    Manual,

    /// <summary>按估算难度自动选的。</summary>
    Difficulty,

    /// <summary>自动选，但这一关估不出难度：4K。</summary>
    NoEstimate,

    /// <summary>自动选，用上的专属手序是几键就按几键（2026-11-22 用户：键数听手序文件的）。</summary>
    Fingering,
}

/// <summary>这一关按几键显示，以及为什么。</summary>
public readonly record struct KeyCountChoice(int Keys, KeyCountReason Reason)
{
    /// <summary>自动选键数时，用上的专属手序的键数要不要盖过难度选出的。</summary>
    public bool FollowFile => Reason != KeyCountReason.Manual;
}

/// <summary>
/// 这一关按几键显示（2026-11-22 用户：按谱面难度动态选择，玩家可以关掉、改难度区间或者直接选 K 数）。
/// 自动时按估算难度（<see cref="DifficultyEstimator"/>，试验性）落在哪个区间：比 6K 的起点低是 4K，到 8K 的起点是 8K，中间 6K。
/// 估不出难度时 4K。用上的专属手序是 4、6、8 键时听手序的（见 <see cref="FingeringMatcher"/> 的 Choose）。
/// </summary>
public static class KeyCountChooser
{
    /// <summary>档位上限之外的一档：起点设成它就是不用这种键数。</summary>
    public const int Never = DifficultyTiers.Count + 1;

    /// <param name="auto">按难度自动选。</param>
    /// <param name="manualKeys">不自动选时用的键数（不是 4、6、8 的当 4）。</param>
    /// <param name="sixFrom">从哪一档（1–60）起用 6K，<see cref="Never"/> 表示不用。</param>
    /// <param name="eightFrom">从哪一档起用 8K。</param>
    /// <param name="tier">这一关的估算档位，0 表示估不出。</param>
    public static KeyCountChoice Choose(bool auto, int manualKeys, int sixFrom, int eightFrom, int tier)
    {
        if (!auto)
        {
            return new KeyCountChoice(KeyLayout.IsMode(manualKeys) ? manualKeys : 4, KeyCountReason.Manual);
        }

        if (tier <= 0)
        {
            return new KeyCountChoice(4, KeyCountReason.NoEstimate);
        }

        var keys = tier >= eightFrom ? 8 : tier >= sixFrom ? 6 : 4;
        return new KeyCountChoice(keys, KeyCountReason.Difficulty);
    }
}
