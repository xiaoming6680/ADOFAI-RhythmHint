namespace RhythmHint.Core;

/// <summary>排键方式。时间重置法（界面上叫「标准」）是默认，审核偏好方式（「交替优先」）是可独立选择的候选规则。</summary>
public enum FingeringStyle
{
    TimeReset,
    ReviewPreferenceV1,
    SteadyHands,
    ReviewedTime,
}

/// <summary>排键参数（单位：秒）。对所有关卡一视同仁。</summary>
public sealed class FingeringOptions
{
    /// <summary>默认使用时间重置法；审核偏好方式不替换默认排法。</summary>
    public FingeringStyle Style { get; set; } = FingeringStyle.TimeReset;

    /// <summary>
    /// 键数：4（D F J K）、6（S D F J K L）、8（A S D F J K L ;）。6K、8K 先按 4K 排，放到中间四个键上，
    /// 只有快到一根手指隔不到 ResetWindow 就得再按的连打才用上外侧的无名指、小指（2026-11-22 用户：越快用的手指越多）。
    /// </summary>
    public int Keys { get; set; } = 4;

    /// <summary>审核偏好短串改为食指交互的最小间隔（秒）。更快的串保留轮指。</summary>
    public double ReviewShortMinGap { get; set; } = 0.060;

    /// <summary>审核偏好短串交互的最大间隔（秒），同时不超过 ResetWindow / 2。</summary>
    public double ReviewShortMaxGap { get; set; } = 0.0825;

    /// <summary>可转换的完整短串最少音符数，不对长串切滑动窗口。</summary>
    public int ReviewShortMinNotes { get; set; } = 4;

    /// <summary>可转换的完整短串最多音符数。</summary>
    public int ReviewShortMaxNotes { get; set; } = 5;

    /// <summary>短串相邻间隔相对首个间隔的容差（秒）。这些边界是待审核的工程参数。</summary>
    public double ReviewTimingTolerance { get; set; } = 0.001;

    /// <summary>
    /// 读不到转角时（TileHit.Angle 是 NaN）才用：和这一组第一块相隔小于它算同时按。包括伪双押：官方关卡常用两块相隔约 25–31 ms 的砖表示双押。
    /// 只看时间分不清多押和极快的连打（玩家反馈旧版把快速轮指段识别成双押），所以有转角时改看 ChordAngle。
    /// </summary>
    public double ChordWindow { get; set; } = 0.035;

    /// <summary>
    /// 多押转角上限（度）：相邻两块要按的砖之间星球转过的角度不超过它、而且相隔不超过 ChordMaxGap，就算同时按，
    /// 一块接一块连成双押、三押、四押。谱师画多押用的是很小的转角（常见 15°、30°），极快的连打转角正常、只是 BPM 高。
    /// 默认 30°：本机 37 张关卡上，均匀连打被误合成多押从 4220 处降到 7 处，小角度多押漏合从 92 处降到 3 处；
    /// 放宽到 45° 误合涨到 29 处，收紧到 20° 大多数 30° 的伪双押就合不上了。
    /// </summary>
    public double ChordAngle { get; set; } = 30;

    /// <summary>
    /// 小转角也要相隔不超过它才合并（秒），免得慢歌里转角小、实际隔得很开的两下被合成多押。
    /// 默认 60 ms：要盖住 120 BPM 下 15° 的伪双押（41.7 ms）；取 45、60、80 ms 在本机关卡上结果几乎一样。
    /// </summary>
    public double ChordMaxGap { get; set; } = 0.06;

    /// <summary>
    /// 主手重置间隔 R：和上一下隔得比它久，就回到右手食指（一根手指点得过来）；
    /// R/2 到 R 之间两只手的食指轮流；R/2 以内同一只手两根手指轮指。
    /// 默认 130 ms（2026-11-19 用户定，原来 165）：轮指线随之是 65 ms。依据 Avantgarde 严判完美通关视频（B 站 BV1cN411V7Pb）逐帧读出的 889 个音：
    /// 75 ms 的快段全是两指交替（原来的 82.5 ms 轮指线把它们排成了轮指），150 ms 基本单指连点；而 deleted（工坊 3386617648）
    /// 那种 50 ms 的旋转短串仍要轮指。改成 130 ms 后和视频 86% 一致（165 ms 时 44%），50 ms 短串的轮指不变。
    /// 更早的依据：原版「全节奏提示」作者交替 / 单指的分界约 160 ms，LaneReader 用 165。
    /// </summary>
    public double ResetWindow { get; set; } = 0.13;

    /// <summary>
    /// 紧挨着双押（前一下或后一下就是双押）、隔得不超过它的单押放另一只手（双押在右手时放左手，在左手时放右手），让按双押的手专心按双押。
    /// 用户：右手单押接右手双押，食指、中指配合不过来。默认 450 ms：本机关卡里单押和双押共用一个键的地方多在 167–375 ms。
    /// </summary>
    public double ChordFocusWindow { get; set; } = 0.45;

    /// <summary>同一条轨道连续按两下（中间没有别的音符）至少隔这么久。</summary>
    public double JackWindow { get; set; } = 0.15;

    /// <summary>快到 R/2 以内时同一只手两根手指轮指（四个键都用上）；关掉就只用两根食指交替。</summary>
    public bool UseRoll { get; set; } = true;

    /// <summary>轮指时外侧手指先落（K J D F，用户习惯的 4312）；关掉是食指先落（J K F D）。</summary>
    public bool OuterRoll { get; set; } = true;

    /// <summary>
    /// 左撇子：以左手为主手，整套排法左右镜像（D↔K、F↔J）。慢的左手食指 F 连点、停下回左手、双押优先左手、
    /// 轮指 D F K J；双押同手、不出现食指→外侧手指这些约束镜像后照样成立。
    /// </summary>
    public bool LeftHanded { get; set; }

    /// <summary>
    /// 双押优先另一只手（玩家反馈：双押用手和惯用手分开设置）。右手为主手时双押优先左手 D+F，
    /// 紧挨着双押的单押改由主手按；左撇子时整体镜像，双押优先右手 J+K。双押同手这条照样成立。
    /// </summary>
    public bool ChordsOnOtherHand { get; set; }

    /// <summary>复制一份（只有值类型的属性，浅复制就够）。</summary>
    public FingeringOptions Copy() => (FingeringOptions)MemberwiseClone();
}
