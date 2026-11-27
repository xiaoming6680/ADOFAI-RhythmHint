// 冻结副本，不能改：v1.5.0 标签里 src/RhythmHint.Core/SettingsFields.cs 的公开字段（XmlSerializer 读写的就是这些），
// 用来测「以后写出的 Settings.xml，玩家降级回 1.5.0 也能整份读」。写法同 Settings132.cs：只抄字段，排法枚举抄成 1.5.0 的四个值，
// 交替优先参数的默认值写成当时的数。1.5.0 比 1.3.2 多 13 项：实际 BPM、判定提示两项、面板居中、音符不透明度、6K / 8K 键位、
// 两种新排法（UseSteadyHands、UseReviewedTime）、键数四项。
using System.Xml.Serialization;

namespace RhythmHint.Core.Tests.Frozen;

/// <summary>1.5.0 的 RhythmHint.Core.FingeringStyle。</summary>
public enum FingeringStyle150
{
    TimeReset,
    ReviewPreferenceV1,
    SteadyHands,
    ReviewedTime,
}

/// <summary>v1.5.0 的 Settings（54 个字段：比 1.3.2 多 13 项）。</summary>
[XmlRoot("Settings")]
public sealed class Settings150
{
    public bool ShowPreview = true;
    public bool PreviewSound = true;
    public float PreviewVolume = 0.6f;
    public bool ShrinkGameView = true;
    public bool ShowLanes = true;
    public bool RoundNotes;
    public bool ShowTaiko = true;
    public bool ShowBpm = true;
    public bool ShowDifficulty = true;
    public bool ShowRealBpm = true;
    public bool ShowChordCount = true;
    public bool ShowJudgments = true;
    public bool ShowPerfectJudgments;
    public bool LanesOnRight;
    public bool TaikoAtBottom;
    public bool LanesInCenter;
    public float UiScale = 1f;
    public float LaneSpeed = 1f;
    public float TaikoSpeed = 1f;
    public float PanelOpacity = 0.92f;
    public float NoteOpacity = 1f;
    public int VisualOffsetMs;
    public string KeyLabels = "D F J K";
    public string KeyLabels6 = "S D F J K L";
    public string KeyLabels8 = "A S D F J K L ;";
    public bool LightKeysOnPress = true;
    public string SettingsHotkey = "F8";
    public string Language = string.Empty;
    public string LatestVersion = string.Empty;
    public bool AutoUpdate = true;
    public int GuideVersion;
    public int ChordAngleDeg = 30;
    public int ResetWindowMs = 130;
    public int ChordFocusMs = 450;
    public int JackWindowMs = 150;
    public bool UseRoll = true;
    public bool OuterRoll = true;
    public FingeringStyle150 Style = FingeringStyle150.TimeReset;
    public bool UseSteadyHands;
    public bool UseReviewedTime;
    public int DefaultsRevision;
    public int ReviewShortMinGapMs = 60;
    public float ReviewShortMaxGapMs = 82.5f;
    public int ReviewShortMinNotes = 4;
    public int ReviewShortMaxNotes = 5;
    public float ReviewTimingToleranceMs = 1f;
    public bool UseCustomFingering = true;
    public string FingeringChoices = string.Empty;
    public bool LeftHanded;
    public bool ChordsOnOtherHand;
    public bool AutoKeyCount = true;
    public int KeyCount = 4;
    public int SixKeysFromTier = 21;
    public int EightKeysFromTier = 46;
}
