using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

public sealed class CornerTransitionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DiagonalApproachHandsOffOnTheVerticalLanding(bool mirrored)
    {
        const double beat = .375;
        int[] turns = { 180, 180, 180, 135, 135, 180, 135, 135, 135, 135, 90, 180 };
        const string axes = "HHH.VV.H.VHH";
        var time = 0.0;
        var hits = turns.Select((turn, i) =>
        {
            if (i > 0) time += beat * turn / 180;
            return new TileHit(i, time, Angle: turn);
        }).ToArray();
        var lanes = Plan(hits, axes, beat, mirrored);
        // The diagonal at 3/4 of a beat leaves the main hand on the approach
        // and the other index on the vertical landing before the quicker turn.
        Assert.Equal(mirrored ? new[] { 1, 2, 1 } : new[] { 2, 1, 2 }, lanes.Skip(8).Take(3));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EqualTimeStraightConnectsAlternatingCornerRuns(bool mirrored)
    {
        const double beat = .38;
        const string axes = "HVHVHHHHHVHVH";
        var hits = axes.Select((axis, i) => new TileHit(i, i * beat / 2,
            Angle: i == 0 || axis == axes[i - 1] ? 180 : 90)).ToArray();
        var lanes = Plan(hits, axes, beat, mirrored);
        Assert.Equal(Enumerable.Range(0, axes.Length).Select(i => (i % 2 == 0) ^ mirrored ? 2 : 1), lanes);
    }

    [Fact]
    public void StraightCanDrumOnMainHandBeforeTwiceAsFastAlternation()
    {
        const double beat = .3;
        var axes = "HVHVH" + new string('H', 16) + "VHVHVHVH";
        var hits = axes.Select((axis, i) => new TileHit(i,
            i <= 20 ? i * beat / 2 : 10 * beat + (i - 20) * beat / 4,
            Angle: i == 0 || axis == axes[i - 1] ? 180 : 90)).ToArray();
        var lanes = Plan(hits, axes, beat, false);
        Assert.All(lanes.Skip(4).Take(17), lane => Assert.Equal(2, lane));
        Assert.Equal(new[] { 1, 2, 1, 2 }, lanes.Skip(21).Take(4));
    }

    private static int[] Plan(TileHit[] hits, string axes, double beat, bool mirrored)
    {
        var directions = Enumerable.Range(0, axes.Length).ToDictionary(i => i, i => axes[i] switch
        {
            'H' => TrackAxis.Horizontal,
            'V' => TrackAxis.Vertical,
            _ => TrackAxis.None
        });
        var options = new FingeringOptions { Style = FingeringStyle.SteadyHands, LeftHanded = mirrored };
        var rhythm = new FingeringRhythm(0, new[] { (0.0, beat) }, directions);
        return NotePlanner.Plan(hits, options, rhythm).Select(n => Assert.Single(n.Lanes.Keys)).ToArray();
    }
}
