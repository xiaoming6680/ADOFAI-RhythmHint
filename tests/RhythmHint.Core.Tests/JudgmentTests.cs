using System;
using System.Linq;
using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

/// <summary>判定提示（2026-11-23 用户：多 K 面板上加判定提示，但不能喧宾夺主）。</summary>
public class JudgmentTests
{
    [Fact]
    public void SeverityGrowsAwayFromPerfectOnBothSides()
    {
        Judgment[] early = { Judgment.Perfect, Judgment.EarlyPerfect, Judgment.VeryEarly, Judgment.TooEarly, Judgment.Overload };
        Judgment[] late = { Judgment.Perfect, Judgment.LatePerfect, Judgment.VeryLate, Judgment.TooLate, Judgment.Miss };
        for (var i = 1; i < early.Length; i++)
        {
            Assert.True(JudgmentRules.Severity(early[i]) > JudgmentRules.Severity(early[i - 1]), $"{early[i]} 应该比 {early[i - 1]} 差");
            Assert.True(JudgmentRules.Severity(late[i]) > JudgmentRules.Severity(late[i - 1]), $"{late[i]} 应该比 {late[i - 1]} 差");
            Assert.Equal(JudgmentRules.Severity(early[i]), JudgmentRules.Severity(late[i]));
        }
    }

    [Fact]
    public void PerfectShowsOnlyWhenAsked()
    {
        foreach (var j in Enum.GetValues(typeof(Judgment)).Cast<Judgment>())
        {
            Assert.True(JudgmentRules.Shows(j, showPerfect: true));
            Assert.Equal(j != Judgment.Perfect, JudgmentRules.Shows(j, showPerfect: false));
        }
    }

    /// <summary>
    /// 按名字认游戏的判定（2026-11-28）：游戏 3.4.0 之后完美分成 PerfectMinus、XPerfect、PerfectPlus，之前只有 Perfect，
    /// 编号也跟着错位；名字对上了新旧游戏都认得对。自动、中旋、多按、死后补记的不是玩家按出来的。
    /// </summary>
    [Theory]
    [InlineData("TooEarly", Judgment.TooEarly)]
    [InlineData("VeryEarly", Judgment.VeryEarly)]
    [InlineData("EarlyPerfect", Judgment.EarlyPerfect)]
    [InlineData("Perfect", Judgment.Perfect)] // 3.4.0 之前的游戏
    [InlineData("PerfectMinus", Judgment.Perfect)]
    [InlineData("XPerfect", Judgment.Perfect)]
    [InlineData("PerfectPlus", Judgment.Perfect)]
    [InlineData("LatePerfect", Judgment.LatePerfect)]
    [InlineData("VeryLate", Judgment.VeryLate)]
    [InlineData("TooLate", Judgment.TooLate)]
    [InlineData("FailMiss", Judgment.Miss)]
    [InlineData("FailOverload", Judgment.Overload)]
    public void GameJudgmentsAreRecognisedByName(string name, Judgment expected)
    {
        Assert.Equal(expected, JudgmentRules.FromGameName(name));
    }

    [Theory]
    [InlineData("Multipress")]
    [InlineData("Auto")]
    [InlineData("OverPress")]
    [InlineData("Midspin")]
    [InlineData("FailedFloor")]
    [InlineData("12")] // 游戏枚举里没有名字的编号
    public void JudgmentsThePlayerDidNotMakeAreIgnored(string name)
    {
        Assert.Null(JudgmentRules.FromGameName(name));
    }

    /// <summary>颜色照游戏默认配色（RDConstants.hitMarginColours）：快慢对称，完美、稍快稍慢、快慢、太快太慢、失败各不相同。</summary>
    [Fact]
    public void ColorsFollowTheGameDefaultsAndAreSymmetric()
    {
        Assert.Equal(0x60FF4Eu, JudgmentRules.Rgb(Judgment.Perfect));
        Assert.Equal(0xFF0000u, JudgmentRules.Rgb(Judgment.TooEarly));
        Assert.Equal(JudgmentRules.Rgb(Judgment.EarlyPerfect), JudgmentRules.Rgb(Judgment.LatePerfect));
        Assert.Equal(JudgmentRules.Rgb(Judgment.VeryEarly), JudgmentRules.Rgb(Judgment.VeryLate));
        Assert.Equal(JudgmentRules.Rgb(Judgment.TooEarly), JudgmentRules.Rgb(Judgment.TooLate));
        Assert.Equal(JudgmentRules.Rgb(Judgment.Miss), JudgmentRules.Rgb(Judgment.Overload));
        var levels = new[] { Judgment.Perfect, Judgment.EarlyPerfect, Judgment.VeryEarly, Judgment.TooEarly, Judgment.Miss };
        Assert.Equal(levels.Length, levels.Select(JudgmentRules.Rgb).Distinct().Count());
    }

    [Fact]
    public void OnlyNotesNearNowAreFresh()
    {
        Assert.True(JudgmentRules.IsFresh(10.0, 10.2));
        Assert.True(JudgmentRules.IsFresh(10.3, 10.0));
        Assert.False(JudgmentRules.IsFresh(8.0, 10.0)); // 编辑器从后面的砖开始播放，进度往前跳过的音符
    }

    [Fact]
    public void NarrowLanesNeedWiderGaps()
    {
        Assert.Equal(0.75, JudgmentRules.MinGap(40, 68));
        Assert.Equal(1.25, JudgmentRules.MinGap(60, 48));
        Assert.Equal(0.75, JudgmentRules.MinGap(60, 0));
    }

    [Fact]
    public void DemoJudgmentsAreMostlyPerfectAndBalanced()
    {
        var cycle = Enumerable.Range(0, 22).Select(JudgmentRules.DemoAt).ToList();
        Assert.True(cycle.Count(j => j == Judgment.Perfect) > cycle.Count / 2);
        Assert.Equal(cycle.Count(j => j is Judgment.EarlyPerfect or Judgment.VeryEarly), cycle.Count(j => j is Judgment.LatePerfect or Judgment.VeryLate));
        Assert.DoesNotContain(cycle, j => JudgmentRules.Severity(j) > 2);
        Assert.Equal(JudgmentRules.DemoAt(3), JudgmentRules.DemoAt(3 + 22));
        Assert.Equal(JudgmentRules.DemoAt(21), JudgmentRules.DemoAt(-1));
    }

    [Fact]
    public void ChordPopSitsBetweenItsLanes()
    {
        var pops = new JudgmentPops<string>();
        pops.Add(new[] { 2, 3 }, "稍快", 0);
        Assert.Equal(3.0, pops.Items.Single().Center);

        pops.Add(Array.Empty<int>(), "不出", 0);
        Assert.Single(pops.Items);
    }

    [Fact]
    public void NewPopReplacesOverlappingOnes()
    {
        var pops = new JudgmentPops<string>();
        pops.Add(new[] { 0 }, "第一下", 0);
        pops.Add(new[] { 3 }, "远处", 0.01);
        pops.Add(new[] { 0 }, "同一条轨道", 0.05);
        Assert.Equal(new[] { "远处", "同一条轨道" }, pops.Items.Select(p => p.Value));

        // 4K 相邻轨道放得下；窄轨道按 minGap 换掉旁边的
        pops.Add(new[] { 1 }, "旁边", 0.06);
        Assert.Equal(3, pops.Items.Count);
        pops.Add(new[] { 2 }, "窄轨道", 0.07, minGap: 1.25);
        Assert.Equal(new[] { "同一条轨道", "窄轨道" }, pops.Items.Select(p => p.Value));
    }

    [Fact]
    public void PopsFadeOutAfterTheirLife()
    {
        var pops = new JudgmentPops<string>();
        pops.Add(new[] { 0 }, "a", 1.0);
        pops.Add(new[] { 3 }, "b", 1.2);
        Assert.Equal(0.5, pops.Items[0].Age(1.0 + (JudgmentPops<string>.Life / 2)), 6);

        pops.Expire(1.0 + JudgmentPops<string>.Life + 0.001);
        Assert.Equal("b", pops.Items.Single().Value);

        pops.Expire(0.5); // 时钟倒退（不该发生）：全部去掉，不留一个永远不消失的字
        Assert.Empty(pops.Items);
    }
}
