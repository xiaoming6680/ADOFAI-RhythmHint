using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

public sealed class ReviewedTimeTests
{
    private static FingeringOptions Timing(bool mirror = false) => new()
    {
        Style = FingeringStyle.ReviewedTime,
        ResetWindow = .1,
        LeftHanded = mirror,
    };

    [Theory]
    [InlineData(false, "KJFJ")]
    [InlineData(true, "DFJF")]
    public void TripletReturnsToTheEstablishedPulse(bool mirror, string expected)
    {
        int[] ticks = { 0, 4, 8, 12, 16, 17, 18, 20, 24, 28 };
        var hits = ticks.Select((t, i) => new TileHit(i, t / 15.0, Angle: 180)).ToArray();
        var options = Timing(mirror);
        var plan = NotePlanner.Plan(hits, options);
        Assert.Equal(expected, Keys(plan.Skip(4).Take(4)));
        options.UseRoll = false;
        Assert.DoesNotContain(Keys(NotePlanner.Plan(hits, options)), key => key == 'D' || key == 'K');
    }

    [Fact]
    public void DottedDurationsAdvanceAnAlternatingClockAtTwoDifferentTempos()
    {
        int[] ticks = { 0, 2, 3, 6, 8, 9, 12, 14, 15, 18, 20 };
        foreach (var unit in new[] { .14, .17 })
        {
            var hits = ticks.Select((t, i) => new TileHit(i, t * unit, Angle: 180)).ToArray();
            Assert.Equal("JJFJJFJJFJJ", Keys(NotePlanner.Plan(hits, Timing())));
        }
    }

    [Fact]
    public void ASteadySlowPulseKeepsTheMainHandThroughAChordAndChangingDirections()
    {
        var hits = Enumerable.Range(0, 24).Select(i => new TileHit(i, i * .179,
            Angle: 90, Taps: i == 12 ? 2 : 1, Multitap: i == 12)).ToArray();
        var rhythm = new FingeringRhythm(.07, new[] { (0.0, .31) },
            hits.ToDictionary(h => h.TileIndex, h => h.TileIndex % 2 == 0 ? TrackAxis.Horizontal : TrackAxis.Vertical));
        var plan = NotePlanner.Plan(hits, Timing(), rhythm);
        Assert.Equal(new[] { 2, 3 }, plan[12].Lanes.Keys);
        Assert.All(plan.Where(n => n.TileIndex != 12), note => Assert.Equal(2, Assert.Single(note.Lanes.Keys)));
        Assert.Equal(Keys(plan), Keys(NotePlanner.Plan(hits, Timing())));
    }

    [Fact]
    public void TimePhrasesPreserveHeldNotesAndNativeMultitaps()
    {
        var hits = new[] { new TileHit(1, 0, HoldSeconds: .45), new TileHit(2, .11),
            new TileHit(3, .21), new TileHit(4, .36), new TileHit(5, .50),
            new TileHit(6, 1, Angle: 180, Taps: 3, Multitap: true) };
        var baseline = NotePlanner.Plan(hits, new FingeringOptions { ResetWindow = .1 });
        var output = NotePlanner.Plan(hits, Timing());
        Assert.Equal(baseline.SelectMany(n => n.Lanes), output.SelectMany(n => n.Lanes));
        Assert.Equal(hits.Select(n => n.Time), output.Select(n => n.Time));
    }

    private static string Keys(IEnumerable<PlannedNote> notes) =>
        string.Concat(notes.SelectMany(note => note.Lanes.Keys.Select(lane => "DFJK"[lane])));
}
