using System;
using System.Collections.Generic;
using RhythmHint.Core;

namespace RhythmHint;

/// <summary>
/// 设置（Mods\RhythmHint\Settings.xml）的字段、默认值和不依赖游戏的逻辑。和 Mod 的 Settings.cs 是同一个类（partial）：
/// Mod 把 Core 的源码编进自己的 DLL，在那边加上 UMM 的 ModSettings 基类和保存；这里不引用 Unity，测试直接拿它读写设置文件。
/// <para>
/// 字段必须是 public 才会被存下来。设置文件已经在玩家手里（1.2.x、1.3.0、1.3.1 写的），XmlSerializer 按元素名读写，
/// 以后只能这样改（SettingsFileTests 拿各版本写出的样例守着）：只加字段，默认值写在字段初始值里；不改名（改名等于老文件里的值静默丢掉）；
/// 不改类型；不用 Dictionary、接口类型；公开字段不改成只读属性；枚举值不删不改名。
/// 1.3.x 读到不认识的枚举值会整份读失败，所以 Style 只能写 1.3.x 认识的值，以后加排法要另存新字段。
/// </para>
/// </summary>
public sealed partial class Settings
{
    private static readonly FingeringOptions ReviewDefaults = new();

    /// <summary>设置页打开时播放演示。</summary>
    public bool ShowPreview = true;

    /// <summary>演示时每个音符到达播放一声「嗒」，用来校准画面偏移。</summary>
    public bool PreviewSound = true;

    public float PreviewVolume = 0.6f;

    /// <summary>
    /// 自适应游戏画幅：提示面板出现时，游戏画面和界面文字（连击数、判定、暂停菜单等）一起缩到面板以外的区域，面板收起时还原。
    /// 试验阶段界面文字曾是单独的 ShrinkGameUi 开关，合并后旧设置文件里的那个字段读的时候会被忽略。
    /// </summary>
    public bool ShrinkGameView = true;

    public bool ShowLanes = true;

    /// <summary>四轨的音符画成和顶部太鼓条一样的圆球，按键框换成判定圈；关掉是横条和方形按键框。</summary>
    public bool RoundNotes;

    public bool ShowTaiko = true;
    public bool ShowBpm = true;

    /// <summary>BPM 框左半边画本关的估算难度（TUF 档位图标）；BPM 框关掉时也不画。</summary>
    public bool ShowDifficulty = true;

    /// <summary>
    /// 太鼓条上三押、四押的紫球中间写 3、4（2026-11-19 用户：只开太鼓条的玩家看不懂紫色，要能关）。
    /// 1.3.1 之后新加的字段：老版本读到会跳过，老文件里没有它就是默认值。
    /// </summary>
    public bool ShowChordCount = true;

    /// <summary>四轨面板放在屏幕右边（默认左边）。</summary>
    public bool LanesOnRight;

    /// <summary>太鼓条（连同 BPM 框）放在屏幕下面（默认上面）。</summary>
    public bool TaikoAtBottom;

    /// <summary>
    /// 下落面板放在屏幕中间（2026-11-22 用户：多键的面板宽，想像下落式音游那样放中间）；开着时不看 <see cref="LanesOnRight"/>。
    /// 面板盖在游戏画面上（画面不让位），太鼓条那一行的 BPM 框放在左边。1.3.x 不认这个字段，照 LanesOnRight 放。
    /// </summary>
    public bool LanesInCenter;

    /// <summary>整体缩放（在按屏幕高度 1080 换算的基础上再乘）。</summary>
    public float UiScale = 1f;

    /// <summary>下落速度倍率，1 = 每秒 1100 像素（1080p）。</summary>
    public float LaneSpeed = 1f;

    /// <summary>太鼓条滚动速度倍率，1 = 每秒 650 像素（1080p）。</summary>
    public float TaikoSpeed = 1f;

    /// <summary>面板底色不透明度，越高越不受游戏背景影响。2026-11-22 用户：面板放中间时要能调得更透明，下限从 30% 改成 0。</summary>
    public float PanelOpacity = 0.92f;

    /// <summary>
    /// 音符（下落面板和太鼓条上的音符、长按）的不透明度，判定线、按键框不变（2026-11-22 用户：面板放中间时音符挡画面）。
    /// 1.3.x 不认这个字段，照不透明画。
    /// </summary>
    public float NoteOpacity = 1f;

    /// <summary>画面整体提前（正数）或推后（负数），毫秒。只影响显示，不影响判定。</summary>
    public int VisualOffsetMs;

    /// <summary>
    /// 四个按键框对应的键，用空格隔开：Unity 按键名或符号本身（如 D、;、LeftShift、Space），没指定的写 None。
    /// 按键框上显示它（长名字缩写），按下时点亮。设置窗口里可选预设，或用屏幕键盘自定义。
    /// </summary>
    public string KeyLabels = "D F J K";

    /// <summary>6K 的六个按键框（每只手无名指、中指、食指），写法同 <see cref="KeyLabels"/>。2026-11-22 新加的字段：老版本读到会跳过。</summary>
    public string KeyLabels6 = "S D F J K L";

    /// <summary>8K 的八个按键框（再加上小指）。</summary>
    public string KeyLabels8 = "A S D F J K L ;";

    public bool LightKeysOnPress = true;

    /// <summary>打开 / 关闭独立设置窗口的按键（Unity 按键名）。</summary>
    public string SettingsHotkey = "F8";

    /// <summary>界面语言代码（见 Core 的 Languages.All）；空 = 跟随游戏的语言。</summary>
    public string Language = string.Empty;

    /// <summary>UMM 查到过的最新版本号（见 UpdateNotice）；空 = 没查到过。设置窗口里不显示，恢复默认值也不清。</summary>
    public string LatestVersion = string.Empty;

    /// <summary>启动游戏时检查新版本，在后台下载安装，下次启动生效（AutoUpdater）。</summary>
    public bool AutoUpdate = true;

    /// <summary>
    /// 设置窗口的首次引导看过第几版（0 = 没看过：第一次打开窗口时自动播放；看完或跳过后记下）。
    /// 引导内容大改时把 SettingsWindow.GuideRevision 加一，老玩家也会再看一次。恢复默认值不清。
    /// </summary>
    public int GuideVersion;

    /// <summary>多押转角上限（度）：相邻两块砖转角不超过它、又挨得近，就合成一个多押（见 FingeringOptions.ChordAngle）。</summary>
    /// <remarks>
    /// 1.1.0 之前按时间合并（PseudoChordWindowMs，毫秒；更早叫 ChordWindowMs），分不清多押和极快的连打，
    /// 旧设置文件里的这两个字段读的时候会被忽略。
    /// </remarks>
    public int ChordAngleDeg = 30;

    // 1.0.0 起排键改成「时间重置法」，旧设置文件里的 UltraWindowMs、PhraseBreakMs、PatternRoll1243 等字段读的时候会被忽略

    /// <summary>
    /// 主手重置间隔：比它慢右手食指连点，一半到它之间左右食指轮流，一半以内同手轮指。
    /// 默认 130 ms（2026-11-19 用户，原来 165）：75 ms 的快段两指交替，50 ms 的短串仍轮指，依据见 FingeringOptions.ResetWindow。
    /// </summary>
    public int ResetWindowMs = 130;

    /// <summary>紧挨着多押、隔得不超过它的单押放另一只手，让按多押的手专心按多押。</summary>
    public int ChordFocusMs = 450;

    /// <summary>同一条轨道连续按两下至少隔这么久。</summary>
    public int JackWindowMs = 150;

    /// <summary>很快的地方同一只手两根手指轮指（四个键都用上）；关掉只用两根食指。</summary>
    public bool UseRoll = true;

    /// <summary>轮指时外侧手指先落（K J D F）；关掉是食指先落（J K F D）。</summary>
    public bool OuterRoll = true;

    /// <summary>排法：标准（时间重置法，默认）或交替优先（试验性）。</summary>
    public FingeringStyle Style = FingeringStyle.TimeReset;

    /// <summary>
    /// 默认值改过几次（见 <see cref="MigrateDefaults"/>）。0 = 老设置文件或新装；新装时本来就是新默认，迁移不改什么。
    /// </summary>
    public int DefaultsRevision;

    /// <summary>交替优先排法的短串参数（毫秒、音符数），只有这个排法用。</summary>
    public int ReviewShortMinGapMs = (int)Math.Round(ReviewDefaults.ReviewShortMinGap * 1000);
    public float ReviewShortMaxGapMs = (float)(ReviewDefaults.ReviewShortMaxGap * 1000);
    public int ReviewShortMinNotes = ReviewDefaults.ReviewShortMinNotes;
    public int ReviewShortMaxNotes = ReviewDefaults.ReviewShortMaxNotes;
    public float ReviewTimingToleranceMs = (float)(ReviewDefaults.ReviewTimingTolerance * 1000);

    /// <summary>进到有专属手序的歌时自动使用（Mods/RhythmHint/Fingerings 里的文件）。</summary>
    public bool UseCustomFingering = true;

    /// <summary>玩家在分不清时选的手序：一行「谱面指纹 Tab 文件名」，文件名为空表示这一关不用专属手序。</summary>
    public string FingeringChoices = string.Empty;

    /// <summary>左撇子：以左手为主手，整套排法左右镜像（D↔K、F↔J）。</summary>
    public bool LeftHanded;

    /// <summary>
    /// 多押优先另一只手（玩家反馈：双押用手和惯用手分开设置；三押跟着它走）。存的是相对惯用手的选择：默认跟惯用手，
    /// 老设置文件读进来不变；换惯用手时多押跟着换边。
    /// </summary>
    public bool ChordsOnOtherHand;

    /// <summary>
    /// 按估算难度自动选几键（试验性，2026-11-22 用户：按谱面难度动态选择，所有人默认开，可以关掉）。关掉时用 <see cref="KeyCount"/>。
    /// 用上的专属手序是 4、6、8 键时按它的键数显示（见 <see cref="KeyCountChooser"/>）。
    /// </summary>
    public bool AutoKeyCount = true;

    /// <summary>不自动选时用几键：4、6、8（别的数读进来改成 4）。</summary>
    public int KeyCount = 4;

    /// <summary>
    /// 自动选键数时，估算难度从这一档（1–60，P1…U20；61 是不用）起用 6K、8K。到 8K 的起点用 8K，到 6K 的起点用 6K，再低用 4K。
    /// 默认 G1、U6：825 张 TUF 校准谱上 P 档 6K 一个音都不改，G 档起改 4–12% 的音；U6 起 8K 比 6K 同一根手指隔不到 R 又按的明显再少（docs/开发说明.md）。
    /// </summary>
    public int SixKeysFromTier = 21;

    public int EightKeysFromTier = 46;

    // 以下不是 public 字段，不存进设置文件

    /// <summary>
    /// 设置窗口滑块的范围（SettingsWindow*.cs 里写的数；范围跟着别的设置变的，取最宽的那种），<see cref="Normalize"/> 按它夹。
    /// 测试按源码比对两边一致：改滑块范围时这里一起改。
    /// </summary>
    public static IReadOnlyDictionary<string, (double Min, double Max)> SliderRanges { get; } = new Dictionary<string, (double, double)>
    {
        [nameof(PreviewVolume)] = (0, 1),
        [nameof(UiScale)] = (0.5, 2),
        [nameof(LaneSpeed)] = (0.3, 3),
        [nameof(TaikoSpeed)] = (0.3, 3),
        [nameof(PanelOpacity)] = (0, 1),
        [nameof(NoteOpacity)] = (0.1, 1),
        [nameof(VisualOffsetMs)] = (-200, 200),
        [nameof(ChordAngleDeg)] = (0, 60),
        [nameof(ResetWindowMs)] = (100, 300),
        [nameof(ChordFocusMs)] = (0, 800),
        [nameof(JackWindowMs)] = (50, 300),

        // 短串最短间隔的上限是主手重置间隔的一半（最大 300 → 150），最长间隔在最短间隔和这个上限之间
        [nameof(ReviewShortMinGapMs)] = (35, 150),
        [nameof(ReviewShortMaxGapMs)] = (35, 150),
        [nameof(ReviewShortMinNotes)] = (2, 8),

        // 短串最多音符数的下限是最少音符数
        [nameof(ReviewShortMaxNotes)] = (2, 16),
        [nameof(ReviewTimingToleranceMs)] = (0, 10),

        // 自动选键数的两条分界（档位 1–60，61 是不用）；8K 的下限是 6K 的分界
        [nameof(SixKeysFromTier)] = (1, KeyCountChooser.Never),
        [nameof(EightKeysFromTier)] = (1, KeyCountChooser.Never),
    };

    // 排键相关设置上次的值和据此拼出的指纹（见 FingeringKey）
    private PlanInputs _keyInputs;
    private string? _key;

    /// <summary>
    /// 默认值改过时，把老设置文件里的旧默认换成新默认，每次改动只换一次（之后玩家自己调的不动）。返回有没有改。
    /// 2：主手重置间隔默认 165 → 130 ms，还是 165 的换成 130。（1 是 1.3.0 测试版里已删掉的拍点交替用过的，跳过）
    /// </summary>
    public bool MigrateDefaults()
    {
        if (DefaultsRevision >= 2)
        {
            return false;
        }

        if (ResetWindowMs == 165)
        {
            ResetWindowMs = 130;
        }

        DefaultsRevision = 2;
        return true;
    }

    /// <summary>排键参数；<paramref name="keys"/> 是这一关按几键显示（<see cref="KeyCountFor"/>）。</summary>
    public FingeringOptions ToFingering(int keys = 4)
    {
        var options = PlanInputs.Of(this).ToOptions();
        options.Keys = keys;
        return options;
    }

    /// <summary>这一关按几键显示（专属手序的键数另算，见 <see cref="KeyCountChoice.FollowFile"/>）。<paramref name="tier"/> 是估算档位，0 表示估不出。</summary>
    public KeyCountChoice KeyCountFor(int tier) => KeyCountChooser.Choose(AutoKeyCount, KeyCount, SixKeysFromTier, EightKeysFromTier, tier);

    /// <summary>这个键数的按键框写法（4K 是 <see cref="KeyLabels"/>）。</summary>
    public string KeyLabelsFor(int keys) => keys switch
    {
        6 => KeyLabels6,
        8 => KeyLabels8,
        _ => KeyLabels,
    };

    /// <summary>改这个键数的按键框。</summary>
    public void SetKeyLabels(int keys, string labels)
    {
        switch (keys)
        {
            case 6:
                KeyLabels6 = labels;
                break;
            case 8:
                KeyLabels8 = labels;
                break;
            default:
                KeyLabels = labels;
                break;
        }
    }

    /// <summary>
    /// 排键相关设置的指纹，变了就要重排。关卡里、演示时每帧都要比：设置没变时返回上次那个字符串，不重新拼、不分配内存。
    /// 和 <see cref="ToFingering"/> 读的是同一组值（<see cref="PlanInputs"/>），加排键参数时不会漏掉其中一处。
    /// </summary>
    public string FingeringKey
    {
        get
        {
            var now = PlanInputs.Of(this);
            if (_key == null || !now.Equals(_keyInputs))
            {
                _keyInputs = now;
                _key = now.ToString();
            }

            return _key;
        }
    }

    /// <summary>
    /// 读完设置后修正一遍（Main.Load）：数值夹到设置窗口滑块的范围（<see cref="SliderRanges"/>，界面上调得出来的值都不动）、
    /// 最小值大于最大值时交换、小数不是有限数时换回默认值、字符串为 null 时换回默认值。
    /// 正常操作界面碰不到这些（滑块夹住了），手改 Settings.xml、以后改了滑块范围再降级时会出现；
    /// 不修的话交替优先排法的参数不合法会让排键抛异常、整个 Mod 停用。返回改了哪些字段（写日志）。
    /// </summary>
    public List<string> Normalize()
    {
        var d = new Settings();
        var changed = new List<string>();
        PreviewVolume = Fix(nameof(PreviewVolume), PreviewVolume, d.PreviewVolume, changed);
        UiScale = Fix(nameof(UiScale), UiScale, d.UiScale, changed);
        LaneSpeed = Fix(nameof(LaneSpeed), LaneSpeed, d.LaneSpeed, changed);
        TaikoSpeed = Fix(nameof(TaikoSpeed), TaikoSpeed, d.TaikoSpeed, changed);
        PanelOpacity = Fix(nameof(PanelOpacity), PanelOpacity, d.PanelOpacity, changed);
        NoteOpacity = Fix(nameof(NoteOpacity), NoteOpacity, d.NoteOpacity, changed);
        VisualOffsetMs = Fix(nameof(VisualOffsetMs), VisualOffsetMs, changed);
        ChordAngleDeg = Fix(nameof(ChordAngleDeg), ChordAngleDeg, changed);
        ResetWindowMs = Fix(nameof(ResetWindowMs), ResetWindowMs, changed);
        ChordFocusMs = Fix(nameof(ChordFocusMs), ChordFocusMs, changed);
        JackWindowMs = Fix(nameof(JackWindowMs), JackWindowMs, changed);
        ReviewShortMinGapMs = Fix(nameof(ReviewShortMinGapMs), ReviewShortMinGapMs, changed);
        ReviewShortMaxGapMs = Fix(nameof(ReviewShortMaxGapMs), ReviewShortMaxGapMs, d.ReviewShortMaxGapMs, changed);
        ReviewShortMinNotes = Fix(nameof(ReviewShortMinNotes), ReviewShortMinNotes, changed);
        ReviewShortMaxNotes = Fix(nameof(ReviewShortMaxNotes), ReviewShortMaxNotes, changed);
        ReviewTimingToleranceMs = Fix(nameof(ReviewTimingToleranceMs), ReviewTimingToleranceMs, d.ReviewTimingToleranceMs, changed);

        if (ReviewShortMinGapMs > ReviewShortMaxGapMs)
        {
            (ReviewShortMinGapMs, ReviewShortMaxGapMs) = ((int)Math.Floor(ReviewShortMaxGapMs), ReviewShortMinGapMs);
            changed.Add(nameof(ReviewShortMinGapMs) + "↔" + nameof(ReviewShortMaxGapMs));
        }

        if (ReviewShortMinNotes > ReviewShortMaxNotes)
        {
            (ReviewShortMinNotes, ReviewShortMaxNotes) = (ReviewShortMaxNotes, ReviewShortMinNotes);
            changed.Add(nameof(ReviewShortMinNotes) + "↔" + nameof(ReviewShortMaxNotes));
        }

        KeyLabels = Fix(nameof(KeyLabels), KeyLabels, d.KeyLabels, changed);
        KeyLabels6 = Fix(nameof(KeyLabels6), KeyLabels6, d.KeyLabels6, changed);
        KeyLabels8 = Fix(nameof(KeyLabels8), KeyLabels8, d.KeyLabels8, changed);
        if (!KeyLayout.IsMode(KeyCount))
        {
            KeyCount = d.KeyCount;
            changed.Add(nameof(KeyCount));
        }

        SixKeysFromTier = Fix(nameof(SixKeysFromTier), SixKeysFromTier, changed);
        EightKeysFromTier = Fix(nameof(EightKeysFromTier), EightKeysFromTier, changed);
        if (SixKeysFromTier > EightKeysFromTier)
        {
            (SixKeysFromTier, EightKeysFromTier) = (EightKeysFromTier, SixKeysFromTier);
            changed.Add(nameof(SixKeysFromTier) + "↔" + nameof(EightKeysFromTier));
        }

        SettingsHotkey = Fix(nameof(SettingsHotkey), SettingsHotkey, d.SettingsHotkey, changed);
        Language = Fix(nameof(Language), Language, d.Language, changed);
        LatestVersion = Fix(nameof(LatestVersion), LatestVersion, d.LatestVersion, changed);
        FingeringChoices = Fix(nameof(FingeringChoices), FingeringChoices, d.FingeringChoices, changed);
        return changed;
    }

    private static float Fix(string name, float value, float fallback, List<string> changed)
    {
        var (min, max) = SliderRanges[name];
        var result = float.IsNaN(value) || float.IsInfinity(value) ? fallback : (float)Math.Max(min, Math.Min(max, value));
        if (!result.Equals(value))
        {
            changed.Add(name);
        }

        return result;
    }

    private static int Fix(string name, int value, List<string> changed)
    {
        var (min, max) = SliderRanges[name];
        var result = (int)Math.Max(min, Math.Min(max, value));
        if (result != value)
        {
            changed.Add(name);
        }

        return result;
    }

    private static string Fix(string name, string? value, string fallback, List<string> changed)
    {
        if (value != null)
        {
            return value;
        }

        changed.Add(name);
        return fallback;
    }

    // 设置窗口每一页的「恢复本页默认值」：默认值只写在上面的字段初始值里，这里从一个新实例抄回来

    /// <summary>「校准」页：演示、提示音、画面偏移。</summary>
    public void ResetPreview()
    {
        var d = new Settings();
        ShowPreview = d.ShowPreview;
        PreviewSound = d.PreviewSound;
        PreviewVolume = d.PreviewVolume;
        VisualOffsetMs = d.VisualOffsetMs;
    }

    /// <summary>「外观」页：音符样式、位置、显示内容、自适应游戏画幅、按键框、尺寸与速度。</summary>
    public void ResetDisplay()
    {
        var d = new Settings();
        ShowLanes = d.ShowLanes;
        RoundNotes = d.RoundNotes;
        ShowTaiko = d.ShowTaiko;
        LanesOnRight = d.LanesOnRight;
        LanesInCenter = d.LanesInCenter;
        TaikoAtBottom = d.TaikoAtBottom;
        ShowBpm = d.ShowBpm;
        ShowDifficulty = d.ShowDifficulty;
        ShowChordCount = d.ShowChordCount;
        ShrinkGameView = d.ShrinkGameView;
        UiScale = d.UiScale;
        LaneSpeed = d.LaneSpeed;
        TaikoSpeed = d.TaikoSpeed;
        PanelOpacity = d.PanelOpacity;
        NoteOpacity = d.NoteOpacity;
        KeyLabels = d.KeyLabels;
        KeyLabels6 = d.KeyLabels6;
        KeyLabels8 = d.KeyLabels8;
        LightKeysOnPress = d.LightKeysOnPress;
    }

    /// <summary>「排键」页的参数。惯用手、多押用手、用几键（自动还是手动选的哪种）和专属手序开关不动：那是玩家自己的习惯，不是调出来的参数。</summary>
    public void ResetFingering()
    {
        var d = new Settings();
        SixKeysFromTier = d.SixKeysFromTier;
        EightKeysFromTier = d.EightKeysFromTier;
        ChordAngleDeg = d.ChordAngleDeg;
        ResetWindowMs = d.ResetWindowMs;
        ChordFocusMs = d.ChordFocusMs;
        JackWindowMs = d.JackWindowMs;
        UseRoll = d.UseRoll;
        OuterRoll = d.OuterRoll;
        Style = d.Style;
        ReviewShortMinGapMs = d.ReviewShortMinGapMs;
        ReviewShortMaxGapMs = d.ReviewShortMaxGapMs;
        ReviewShortMinNotes = d.ReviewShortMinNotes;
        ReviewShortMaxNotes = d.ReviewShortMaxNotes;
        ReviewTimingToleranceMs = d.ReviewTimingToleranceMs;
    }

    /// <summary>「通用」页：打开设置窗口的快捷键（界面语言不动）。</summary>
    public void ResetHotkey() => SettingsHotkey = new Settings().SettingsHotkey;

    public void ResetGeneral()
    {
        ResetHotkey();
        AutoUpdate = new Settings().AutoUpdate;
    }

    /// <summary>
    /// 影响排键的设置（<see cref="ToFingering"/> 用到的全部，加上专属手序开关）。只有值、没有引用，每帧比一次不分配内存；
    /// 变了才拼成 <see cref="FingeringKey"/>。
    /// </summary>
    private readonly record struct PlanInputs(
        int ChordAngleDeg, int ResetWindowMs, int ChordFocusMs, int JackWindowMs, bool UseRoll, bool OuterRoll, FingeringStyle Style,
        int ReviewShortMinGapMs, float ReviewShortMaxGapMs, int ReviewShortMinNotes, int ReviewShortMaxNotes, float ReviewTimingToleranceMs,
        bool LeftHanded, bool ChordsOnOtherHand, bool UseCustomFingering,
        bool AutoKeyCount, int KeyCount, int SixKeysFromTier, int EightKeysFromTier)
    {
        public static PlanInputs Of(Settings s) => new(
            s.ChordAngleDeg, s.ResetWindowMs, s.ChordFocusMs, s.JackWindowMs, s.UseRoll, s.OuterRoll, s.Style,
            s.ReviewShortMinGapMs, s.ReviewShortMaxGapMs, s.ReviewShortMinNotes, s.ReviewShortMaxNotes, s.ReviewTimingToleranceMs,
            s.LeftHanded, s.ChordsOnOtherHand, s.UseCustomFingering,
            s.AutoKeyCount, s.KeyCount, s.SixKeysFromTier, s.EightKeysFromTier);

        public FingeringOptions ToOptions() => new()
        {
            ChordAngle = ChordAngleDeg,
            ResetWindow = ResetWindowMs / 1000.0,
            ChordFocusWindow = ChordFocusMs / 1000.0,
            JackWindow = JackWindowMs / 1000.0,
            UseRoll = UseRoll,
            OuterRoll = OuterRoll,
            Style = Style,
            ReviewShortMinGap = ReviewShortMinGapMs / 1000.0,
            ReviewShortMaxGap = ReviewShortMaxGapMs / 1000.0,
            ReviewShortMinNotes = ReviewShortMinNotes,
            ReviewShortMaxNotes = ReviewShortMaxNotes,
            ReviewTimingTolerance = ReviewTimingToleranceMs / 1000.0,
            LeftHanded = LeftHanded,
            ChordsOnOtherHand = ChordsOnOtherHand,
        };
    }
}
