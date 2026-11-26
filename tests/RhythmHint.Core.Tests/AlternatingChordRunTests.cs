using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

public sealed class AlternatingChordRunTests
{
    [Theory]
    [InlineData(false, "RLRLRLRLRL")]
    [InlineData(true, "LRLRLRLRLR")]
    public void DoublesEveryThirdAttackKeepTheSameAlternationAsTheSingles(bool mirror, string expected)
    {
        var notes = Plan(1.0 / 3, mirror: mirror);
        Assert.Equal(expected, string.Concat(notes.Select(n => n.PairHand == 1 ? 'R' : 'L')));
        Assert.Equal(new[] { 2, 1, 1, 2, 1, 1, 2, 1, 1, 2 }, notes.Select(n => n.Lanes.Count));
    }

    [Theory]
    [InlineData(.5, false, 1)]
    [InlineData(.5, true, 0)]
    public void SlowPulseKeepsThePreferredDoubles(double beat, bool otherChordHand, int hand)
    {
        var notes = Plan(beat, otherChordHand: otherChordHand);
        Assert.All(notes.Where(n => n.Lanes.Count == 2), n => Assert.Equal(hand, n.PairHand));
    }

    [Fact]
    public void FastPulseStartsWithThePreferredChordHandThenAlternatesForRecovery()
    {
        var notes = Plan(1.0 / 3, otherChordHand: true);
        Assert.Equal("LRLRLRLRLR", Hands(notes));
    }

    [Fact]
    public void EqualTimeDoublesKeepAlternatingWhenTheBeatGridDrifts()
    {
        const double step = .089;
        var hits = Enumerable.Range(0, 12)
            .SelectMany(i => new[] {
                new TileHit(2 * i + 1, i * step, Angle: 320),
                new TileHit(2 * i + 2, i * step + .01, Angle: 15),
            }).ToArray();
        var rhythm = new FingeringRhythm(0, new[] { (0.0, .46) });
        var options = new FingeringOptions { Style = FingeringStyle.SteadyHands };

        var notes = NotePlanner.Plan(hits, options, rhythm);
        Assert.Equal(12, notes.Count);
        Assert.All(notes, note => Assert.Equal(2, note.Lanes.Count));
        Assert.Equal("RLRLRLRLRLRL", Hands(notes));
    }

    [Fact]
    public void DoubleAfterRollStartsOnFreeHandAndKeepsItsPhaseThroughDeceleration()
    {
        const double rollGap = .044;
        const double recovery = .15;
        var hits = Enumerable.Range(0, 4)
            .Select(i => new TileHit(i + 1, i * rollGap, Angle: 90)).ToList();
        var time = 4 * rollGap;
        hits.Add(new TileHit(5, time, Angle: 180));
        hits.Add(new TileHit(6, time + .01, Angle: 15));
        var beats = new List<BeatLine> { new(time, true) };
        var step = 2 * rollGap;
        var alternates = 0;
        for (var i = 1; i <= 24; i++)
        {
            time += step;
            hits.Add(new TileHit(i + 6, time, Angle: 180));
            if (i % 4 == 0) beats.Add(new BeatLine(time, false));
            if (step < recovery) alternates++;
            step *= 1.035;
        }

        var rhythm = new FingeringRhythm(0, new[] { (0.0, .36) }, Beats: beats);
        var notes = NotePlanner.Plan(hits, new FingeringOptions { Style = FingeringStyle.SteadyHands }, rhythm);
        Assert.Equal("RRLL", Hands(notes.Take(4)));
        Assert.Equal(2, notes[4].Lanes.Count);
        Assert.Equal(string.Concat(Enumerable.Range(0, alternates + 1).Select(i => i % 2 == 0 ? 'R' : 'L')),
            Hands(notes.Skip(4).Take(alternates + 1)));
        Assert.Equal(1, notes[5 + alternates].PairHand);
    }

    private static string Hands(IEnumerable<PlannedNote> notes) =>
        string.Concat(notes.Select(note => note.PairHand == 1 ? 'R' : 'L'));

    private static List<PlannedNote> Plan(double beat, bool mirror = false, bool otherChordHand = false)
    {
        // Four doubles fall on successive half-beat offsets, with two equally
        // spaced singles between them. The double counts as one hand attack.
        var hits = new List<TileHit>();
        for (var i = 0; i < 10; i++)
        {
            var time = beat * (.5 + i / 3.0);
            hits.Add(new TileHit(2 * i + 1, time, Angle: i % 3 == 1 ? 210 : 240));
            if (i % 3 == 0) hits.Add(new TileHit(2 * i + 2, time + .01, Angle: 15));
        }
        var rhythm = new FingeringRhythm(0, new[] { (0.0, beat) });
        return NotePlanner.Plan(hits, new FingeringOptions
        { Style = FingeringStyle.SteadyHands, LeftHanded = mirror, ChordsOnOtherHand = otherChordHand }, rhythm);
    }
}
