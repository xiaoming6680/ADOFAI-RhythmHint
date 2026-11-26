using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

public sealed class HandRecoveryTests
{
    [Theory]
    [InlineData(.119, "RLR")]
    [InlineData(.120, "RRR")]
    public void ShortDoubleRunRespectsThe120MillisecondBoundary(double step, string expected)
    {
        var hits = Enumerable.Range(0, 3)
            .Select(i => new TileHit(i + 1, i * step, Angle: 180, Taps: 2, Multitap: true)).ToArray();
        var notes = NotePlanner.Plan(hits, new FingeringOptions { Style = FingeringStyle.SteadyHands });

        Assert.Equal(expected, string.Concat(notes.Select(note => note.PairHand == 1 ? 'R' : 'L')));
        Assert.All(notes, note => Assert.Equal(2, note.Lanes.Count));
    }

    [Fact]
    public void ThreeTapsOver120MillisecondsCanAlternateEvenWhenOriginallyRolled()
    {
        var hits = Enumerable.Range(0, 3).Select(i => new TileHit(i + 1, i * .060, Angle: 90)).ToArray();
        var notes = NotePlanner.Plan(hits, new FingeringOptions { Style = FingeringStyle.SteadyHands });

        Assert.Equal(notes[0].PairHand, notes[2].PairHand);
        Assert.NotEqual(notes[0].PairHand, notes[1].PairHand);
    }

    [Fact]
    public void DenseRollKeepsEveryHitWithoutRepeatingAKeyInsideTheRecoveryWindow()
    {
        var hits = Enumerable.Range(0, 12).Select(i => new TileHit(i + 1, i * .050, Angle: 90)).ToArray();
        var notes = NotePlanner.Plan(hits, new FingeringOptions { Style = FingeringStyle.SteadyHands });

        Assert.Equal(hits.Select(hit => hit.Time), notes.Select(note => note.Time));
        foreach (var lane in notes.GroupBy(note => Assert.Single(note.Lanes.Keys)))
            Assert.All(lane.Zip(lane.Skip(1)), pair => Assert.True(pair.Second.Time - pair.First.Time >= .120));
    }
}
