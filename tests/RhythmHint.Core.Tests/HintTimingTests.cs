using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

/// <summary>2026-11-25 用户：多押每次跟着提示准确按下，总是稍早或稍晚。伪多押的提示要放在第一块和最后一块砖的正中间。</summary>
public class HintTimingTests
{
    private static readonly FingeringOptions Fingering = new();

    [Fact]
    public void PseudoDoubleHintSitsBetweenItsTwoTiles()
    {
        var tiles = new[] { new TileHit(1, 1.0), new TileHit(2, 1.025), new TileHit(3, 1.2125), new TileHit(4, 1.4) };
        var notes = NotePlanner.Plan(tiles, Fingering);

        var times = HintTiming.Times(notes, tiles);

        Assert.Equal(3, times.Length);
        Assert.Equal(1.0125, times[0], 6);
        Assert.Equal(1.2125, times[1], 6);
        Assert.Equal(1.4, times[2], 6);
        Assert.Equal(1.0, notes[0].Time, 6); // 排键用的时间不变
    }

    [Fact]
    public void TripleHintSitsBetweenFirstAndLastTile()
    {
        var tiles = new[] { new TileHit(1, 2.0, Angle: 10), new TileHit(2, 2.025, Angle: 10), new TileHit(3, 2.05, Angle: 10), new TileHit(4, 2.5, Angle: 180) };
        var notes = NotePlanner.Plan(tiles, Fingering);

        var times = HintTiming.Times(notes, tiles);

        Assert.Equal(3, notes[0].Lanes.Count);
        Assert.Equal(2.025, times[0], 6);
    }

    [Fact]
    public void MultitapTileKeepsItsOwnTime()
    {
        var tiles = new[] { new TileHit(1, 1.0, Taps: 2, Multitap: true), new TileHit(2, 1.5) };
        var notes = NotePlanner.Plan(tiles, Fingering);

        var times = HintTiming.Times(notes, tiles);

        Assert.True(notes[0].IsChord);
        Assert.Equal(1.0, times[0], 6);
    }

    [Fact]
    public void MissingLastTileFallsBackToNoteTime()
    {
        var note = new PlannedNote(5, 3.0) { LastTileIndex = 6 };

        var times = HintTiming.Times(new[] { note }, new[] { new TileHit(5, 3.0) });

        Assert.Equal(3.0, times[0], 6);
    }
}
