using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

/// <summary>这一关按几键显示（2026-11-22 用户：按谱面难度动态选择，玩家可以关掉、改难度区间或者直接选键数；所有人默认开）。</summary>
public class KeyCountTests
{
    [Theory]
    [InlineData(0, 4, KeyCountReason.NoEstimate)]
    [InlineData(1, 4, KeyCountReason.Difficulty)]
    [InlineData(20, 4, KeyCountReason.Difficulty)] // P20
    [InlineData(21, 6, KeyCountReason.Difficulty)] // G1
    [InlineData(45, 6, KeyCountReason.Difficulty)] // U5
    [InlineData(46, 8, KeyCountReason.Difficulty)] // U6
    [InlineData(60, 8, KeyCountReason.Difficulty)]
    public void AutomaticByEstimatedDifficulty(int tier, int keys, KeyCountReason reason)
    {
        var choice = new Settings().KeyCountFor(tier);

        Assert.Equal(new KeyCountChoice(keys, reason), choice);
        Assert.True(choice.FollowFile);
    }

    [Theory]
    [InlineData(6, 6)]
    [InlineData(8, 8)]
    [InlineData(4, 4)]
    [InlineData(5, 4)]
    public void ManualKeysIgnoreDifficulty(int setting, int keys)
    {
        var s = new Settings { AutoKeyCount = false, KeyCount = setting };

        Assert.All(new[] { 0, 1, 30, 60 }, tier => Assert.Equal(new KeyCountChoice(keys, KeyCountReason.Manual), s.KeyCountFor(tier)));
        Assert.False(s.KeyCountFor(50).FollowFile);
    }

    [Fact]
    public void BoundariesCanSkipAKeyCount()
    {
        // 两条分界一样：中间不用 6K，直接从 4K 到 8K；设成 61 就是不用这种键数
        Assert.Equal(8, KeyCountChooser.Choose(true, 4, 30, 30, 30).Keys);
        Assert.Equal(4, KeyCountChooser.Choose(true, 4, 30, 30, 29).Keys);
        Assert.Equal(6, KeyCountChooser.Choose(true, 4, 30, KeyCountChooser.Never, 60).Keys);
        Assert.Equal(4, KeyCountChooser.Choose(true, 4, KeyCountChooser.Never, KeyCountChooser.Never, 60).Keys);
    }

    [Fact]
    public void NormalizeFixesKeySettings()
    {
        var s = new Settings { KeyCount = 7, SixKeysFromTier = 50, EightKeysFromTier = 20, KeyLabels6 = null!, KeyLabels8 = null! };
        var changed = s.Normalize();

        Assert.Equal(4, s.KeyCount);
        Assert.Equal((20, 50), (s.SixKeysFromTier, s.EightKeysFromTier));
        Assert.Equal("S D F J K L", s.KeyLabels6);
        Assert.Equal("A S D F J K L ;", s.KeyLabels8);
        Assert.Contains(nameof(Settings.KeyCount), changed);

        var clamp = new Settings { SixKeysFromTier = -3, EightKeysFromTier = 99 };
        clamp.Normalize();
        Assert.Equal((1, KeyCountChooser.Never), (clamp.SixKeysFromTier, clamp.EightKeysFromTier));
    }

    [Fact]
    public void EachKeyCountHasItsOwnKeys()
    {
        var s = new Settings();
        s.SetKeyLabels(6, KeyBindings.Assign(s.KeyLabelsFor(6), 5, "Semicolon", 6));

        Assert.Equal("D F J K", s.KeyLabelsFor(4));
        Assert.Equal("S D F J K Semicolon", s.KeyLabelsFor(6));
        Assert.Equal(8, KeyBindings.Split(s.KeyLabelsFor(8), 8).Length);
        Assert.Equal(";", KeyBindings.Split(s.KeyLabelsFor(8), 8)[7]);
        Assert.Throws<ArgumentOutOfRangeException>(() => KeyBindings.Assign(s.KeyLabels, 4, "A"));
        Assert.Equal(6, s.ToFingering(6).Keys);
        Assert.Equal(4, s.ToFingering().Keys);
    }
}
