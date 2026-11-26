using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

public sealed class ReviewedTripletCadenceTests
{
    [Theory]
    [InlineData(false, "DJFJ")]
    [InlineData(true, "KFJF")]
    public void SubdividedMainPulseSwitchesItsOpeningHandAndKeepsTheExit(bool leftHanded, string keys)
    {
        Assert.Equal(keys, Phrase(.067, 2, leftHanded: leftHanded));
    }

    [Fact]
    public void IsolatedThreeTapFigureWithTimeToRecoverUsesBothIndices()
    {
        Assert.Equal("JFJJ", Phrase(.067, 6));
    }

    [Fact]
    public void SlowerSubdivisionAndDisabledRollsStayOnIndices()
    {
        Assert.Equal("JFJJ", Phrase(.080, 2));
        Assert.DoesNotContain(Phrase(.067, 2, useRoll: false), c => c == 'K' || c == 'D');
    }

    private static string Phrase(double tick, int exitGap, bool leftHanded = false, bool useRoll = true)
    {
        int[] positions = { 0, 4, 8, 12, 16, 17, 18, 18 + exitGap, 22 + exitGap, 26 + exitGap };
        var hits = positions.Select((value, i) => new TileHit(i, value * tick,
            Angle: i == 5 || i == 6 ? 45 : i == 7 ? exitGap * 45 : 180)).ToArray();
        var rhythm = new FingeringRhythm(0, new[] { (0.0, 8 * tick) });
        var options = new FingeringOptions { Style = FingeringStyle.SteadyHands, LeftHanded = leftHanded, UseRoll = useRoll };
        return string.Concat(NotePlanner.Plan(hits, options, rhythm).Skip(4).Take(4)
            .Select(n => "DFJK"[Assert.Single(n.Lanes.Keys)]));
    }
}
