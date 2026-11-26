using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

/// <summary>2026-11-26 玩家：BPM 旁边加实际 BPM（60 ÷ 两下按键的间隔），打雪花、直线这类匀速段时当按键速度的参考。</summary>
public class RealBpmTests
{
    private static readonly FingeringOptions Fingering = new();

    [Fact]
    public void UsesIntervalToPreviousNote()
    {
        // 砖块 BPM 200 的 60° 转角：每下 1/3 拍 = 0.1 秒，实际 600
        var tiles = new[] { new TileHit(1, 1.0), new TileHit(2, 1.1), new TileHit(3, 1.2) };
        var notes = NotePlanner.Plan(tiles, Fingering);

        Assert.Equal(600, RealBpm.At(notes, 1, 1), 6);
        Assert.Equal(600, RealBpm.At(notes, 2, 1), 6);
    }

    [Fact]
    public void NothingBeforeFirstOrAfterLastNote()
    {
        var notes = NotePlanner.Plan(new[] { new TileHit(1, 1.0), new TileHit(2, 1.5) }, Fingering);

        Assert.Equal(0, RealBpm.At(notes, 0, 1));
        Assert.Equal(0, RealBpm.At(notes, 2, 1));
    }

    [Fact]
    public void PseudoChordCountsAsOnePress()
    {
        // 两块相隔 25 ms 的砖合成一个伪双押，下一下隔 0.25 秒：实际 240，不是 2400
        var tiles = new[] { new TileHit(1, 1.0), new TileHit(2, 1.025), new TileHit(3, 1.25), new TileHit(4, 1.5) };
        var notes = NotePlanner.Plan(tiles, Fingering);

        Assert.Equal(3, notes.Count);
        Assert.Equal(240, RealBpm.At(notes, 1, 1), 6);
    }

    [Fact]
    public void PitchSpeedsItUp()
    {
        var notes = NotePlanner.Plan(new[] { new TileHit(1, 1.0), new TileHit(2, 1.5) }, Fingering);

        Assert.Equal(120, RealBpm.At(notes, 1, 1), 6);
        Assert.Equal(180, RealBpm.At(notes, 1, 1.5), 6);
        Assert.Equal(0, RealBpm.At(notes, 1, 0));
    }

    [Fact]
    public void LongRestIsNotShown()
    {
        var notes = NotePlanner.Plan(new[] { new TileHit(1, 1.0), new TileHit(2, 3.5), new TileHit(3, 4.0) }, Fingering);

        Assert.Equal(0, RealBpm.At(notes, 1, 1));
        Assert.Equal(120, RealBpm.At(notes, 2, 1), 6);
    }
}
