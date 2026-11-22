using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

/// <summary>
/// 设置窗口每页的「恢复本页默认值」只动这一页上的设置（2026-11-22 重新分组：键数分界、三套键位、快捷键搬到按键页）。
/// 搬页以后旧的恢复方法还清着这些字段的话，在外观页点恢复会把按键页调好的键位一起清掉。
/// </summary>
public class SettingsResetTests
{
    /// <summary>按键页能调的全部改成非默认值，再加上不该被任何一页恢复的「用几键」。</summary>
    private static Settings Customized() => new()
    {
        KeyLabels = "A S K L",
        KeyLabels6 = "Q W E I O P",
        KeyLabels8 = "Z X C V M , . /",
        SixKeysFromTier = 30,
        EightKeysFromTier = 50,
        SettingsHotkey = "F9",
        AutoKeyCount = false,
        KeyCount = 6,
        UiScale = 1.5f,
        AutoUpdate = false,
        Language = "en",
    };

    [Fact]
    public void KeysPageRestoresKeysTiersAndHotkey()
    {
        var s = Customized();
        var d = new Settings();

        s.ResetKeys();

        Assert.Equal(d.KeyLabels, s.KeyLabels);
        Assert.Equal(d.KeyLabels6, s.KeyLabels6);
        Assert.Equal(d.KeyLabels8, s.KeyLabels8);
        Assert.Equal(d.SixKeysFromTier, s.SixKeysFromTier);
        Assert.Equal(d.EightKeysFromTier, s.EightKeysFromTier);
        Assert.Equal(d.SettingsHotkey, s.SettingsHotkey);

        // 用几键是玩家自己的选择，别的页的设置也不碰
        Assert.False(s.AutoKeyCount);
        Assert.Equal(6, s.KeyCount);
        Assert.Equal(1.5f, s.UiScale);
        Assert.False(s.AutoUpdate);
    }

    [Fact]
    public void OtherPagesLeaveKeysPageAlone()
    {
        var s = Customized();

        s.ResetDisplay();
        s.ResetFingering();
        s.ResetPreview();
        s.ResetGeneral();

        Assert.Equal("A S K L", s.KeyLabels);
        Assert.Equal("Q W E I O P", s.KeyLabels6);
        Assert.Equal("Z X C V M , . /", s.KeyLabels8);
        Assert.Equal(30, s.SixKeysFromTier);
        Assert.Equal(50, s.EightKeysFromTier);
        Assert.Equal("F9", s.SettingsHotkey);
        Assert.False(s.AutoKeyCount);
        Assert.Equal(6, s.KeyCount);

        // 各自那一页的照常恢复；界面语言不恢复
        var d = new Settings();
        Assert.Equal(d.UiScale, s.UiScale);
        Assert.Equal(d.AutoUpdate, s.AutoUpdate);
        Assert.Equal("en", s.Language);
    }
}
