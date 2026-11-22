using System;

namespace RhythmHint.Core;

/// <summary>
/// 按键数的键位：左边一半是左手、右边一半是右手，每只手从中间往外依次是食指、中指、无名指、小指（不用拇指，2026-11-22 用户定）。
/// 4K = D F J K（中指、食指 | 食指、中指），6K = S D F J K L，8K = A S D F J K L ;。
/// 同一根手指在不同键数里的轨道不同，但 4K 的四个键在 6K、8K 里正好是中间四个，手指也一样。
/// 只认偶数键（奇数键中间那一轨属于哪只手没定，手序文件读得出来但不套用）。
/// </summary>
public sealed class KeyLayout
{
    /// <summary>界面上能选的键数。</summary>
    public static readonly int[] Modes = { 4, 6, 8 };

    public static readonly KeyLayout Four = new(4);
    public static readonly KeyLayout Six = new(6);
    public static readonly KeyLayout Eight = new(8);

    private KeyLayout(int count)
    {
        Count = count;
    }

    /// <summary>键数。</summary>
    public int Count { get; }

    /// <summary>每只手几根手指。</summary>
    public int FingersPerHand => Count / 2;

    /// <summary>界面上能选的键数（4、6、8）。</summary>
    public static bool IsMode(int keys) => keys is 4 or 6 or 8;

    /// <summary>能按手指对应的键数：2–16 的偶数（手序文件里可能有）。</summary>
    public static bool IsSupported(int keys) => keys is >= 2 and <= 16 && keys % 2 == 0;

    /// <summary>界面上能选的键数用现成的实例，其他偶数键新建一个。</summary>
    public static KeyLayout Of(int keys) => keys switch
    {
        4 => Four,
        6 => Six,
        8 => Eight,
        _ when IsSupported(keys) => new KeyLayout(keys),
        _ => throw new ArgumentOutOfRangeException(nameof(keys), keys, "键数只能是 2–16 的偶数。"),
    };

    /// <summary>轨道属于哪只手：0 左手，1 右手。</summary>
    public int HandOf(int lane) => lane < FingersPerHand ? 0 : 1;

    /// <summary>轨道是这只手的第几根手指：0 食指、1 中指、2 无名指、3 小指（从中间往外数）。</summary>
    public int Finger(int lane) => lane < FingersPerHand ? FingersPerHand - 1 - lane : lane - FingersPerHand;

    /// <summary>某只手某根手指的轨道。</summary>
    public int Lane(int hand, int finger) => hand == 0 ? FingersPerHand - 1 - finger : FingersPerHand + finger;

    /// <summary>左右镜像：同一根手指换到另一只手。</summary>
    public int Mirror(int lane) => Count - 1 - lane;

    /// <summary>别的键数（<paramref name="from"/>）里的轨道换成这个键数里同一只手、同一根手指的轨道。键少换到键多不会出界。</summary>
    public int FromLayout(KeyLayout from, int lane) => Lane(from.HandOf(lane), from.Finger(lane));
}
