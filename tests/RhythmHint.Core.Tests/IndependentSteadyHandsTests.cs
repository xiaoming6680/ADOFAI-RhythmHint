using RhythmHint.Core;

namespace RhythmHint.Core.Tests;

public sealed class IndependentSteadyHandsTests
{
    [Fact]
    public void StraightPulseMayRepeatBelow150MsRegardlessOfStandardSettings()
    {
        const double gap = .144;
        var hits = EvenHits(count: 8, gap, angle: 180);
        var rhythm = new FingeringRhythm(0, new[] { (0.0, gap) });
        var loose = new FingeringOptions
        {
            Style = FingeringStyle.SteadyHands,
            ResetWindow = .01,
            JackWindow = .01,
            ChordFocusWindow = .01,
        };
        var strict = new FingeringOptions
        {
            Style = FingeringStyle.SteadyHands,
            ResetWindow = .8,
            JackWindow = .8,
            ChordFocusWindow = .8,
        };

        var expected = Enumerable.Repeat(2, hits.Length);
        Assert.Equal(expected, SingleKeys(NotePlanner.Plan(hits, loose, rhythm)));
        Assert.Equal(expected, SingleKeys(NotePlanner.Plan(hits, strict, rhythm)));
    }

    [Fact]
    public void BeatPhaseCanSelectEitherStartingHandForAFullAlternatingSection()
    {
        const double gap = .09;
        var hits = EvenHits(count: 8, gap, angle: 90);
        var options = new FingeringOptions { Style = FingeringStyle.SteadyHands };
        var rightStarts = new FingeringRhythm(0, new[] { (0.0, 2 * gap) });
        var leftStarts = new FingeringRhythm(gap, new[] { (gap, 2 * gap) });

        Assert.Equal(new[] { 2, 1, 2, 1, 2, 1, 2, 1 }, SingleKeys(NotePlanner.Plan(hits, options, rightStarts)));
        Assert.Equal(new[] { 1, 2, 1, 2, 1, 2, 1, 2 }, SingleKeys(NotePlanner.Plan(hits, options, leftStarts)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HalfStepTurnsFrameAnEvenStraightPulse(bool mirror)
    {
        // Both vertical runs fall on strong beats. The horizontal pickup between them
        // is half a beat away on either side and lets the vertical hand keep its pulse.
        int[] ticks = { 0, 2, 3, 4, 6, 8, 10, 11, 12, 14, 16, 18, 19, 20, 21, 22 };
        const string path = "HHHVVVVHVVVVHHHH";
        var hits = ticks.Select((tick, i) => new TileHit(i + 1, tick / 6.0,
            Angle: i > 0 && path[i] != path[i - 1] ? 90 : 180)).ToArray();
        var axes = hits.ToDictionary(hit => hit.TileIndex, hit =>
            path[hit.TileIndex - 1] == 'V' ? TrackAxis.Vertical : TrackAxis.Horizontal);
        var rhythm = new FingeringRhythm(0, new[] { (0.0, 1.0 / 3) }, axes);
        var options = new FingeringOptions { Style = FingeringStyle.SteadyHands, LeftHanded = mirror };
        var lanes = SingleKeys(NotePlanner.Plan(hits, options, rhythm)).ToArray();
        var expected = mirror ? "JJJJFJJJJ" : "FFFFJFFFF";
        Assert.Equal(expected, string.Concat(lanes.Skip(3).Take(9).Select(lane => "DFJK"[lane])));
    }

    [Theory]
    [InlineData(FingeringStyle.TimeReset)]
    [InlineData(FingeringStyle.ReviewPreferenceV1)]
    public void SupplyingBeatEvidenceDoesNotAffectOtherStyles(FingeringStyle style)
    {
        var hits = EvenHits(count: 12, gap: .1, angle: 90);
        var options = new FingeringOptions { Style = style };
        var beatGrid = new FingeringRhythm(.1, new[] { (.1, .2) });

        Assert.Equal(SingleKeys(NotePlanner.Plan(hits, options)), SingleKeys(NotePlanner.Plan(hits, options, beatGrid)));
    }

    [Fact]
    public void NativeMultitapsAndPairedDoublesStayGroupedWhileThreeCloseTilesRemainSingles()
    {
        var hits = new[]
        {
            new TileHit(1, 0, Angle: 180),
            new TileHit(2, .20, Angle: 180, Taps: 2, Multitap: true),
            new TileHit(3, .40, HoldSeconds: .30, Angle: 180),
            new TileHit(4, .65, Angle: 180),
            new TileHit(5, .90, Angle: 180),
            new TileHit(6, .91, Angle: 15),
            new TileHit(7, .92, Angle: 15),
            new TileHit(8, 1.20, HoldSeconds: .11, Angle: 180),
            new TileHit(9, 1.22, HoldSeconds: .19, Angle: 15),
            new TileHit(10, 1.60, Angle: 180, Taps: 3, Multitap: true),
            new TileHit(11, 1.90, Angle: 180, Taps: 4, Multitap: true),
            new TileHit(12, 2.20, Angle: 180, Taps: 1, Multitap: true),
        };
        var rhythm = new FingeringRhythm(0, new[] { (0.0, .2) });
        var options = new FingeringOptions { Style = FingeringStyle.SteadyHands };
        var right = NotePlanner.Plan(hits, options, rhythm);
        options.LeftHanded = true;
        var left = NotePlanner.Plan(hits, options, rhythm);

        Assert.Equal(new[] { (1, 1, 1), (2, 2, 2), (3, 3, 1), (4, 4, 1), (5, 5, 1), (6, 6, 1),
            (7, 7, 1), (8, 9, 2), (10, 10, 3), (11, 11, 4), (12, 12, 1) }, right.Select(Structure));
        Assert.Equal(right.Select(Structure), left.Select(Structure));
        Assert.Equal(hits.Where(hit => hit.TileIndex != 9).Select(hit => hit.Time), right.Select(note => note.Time));
        Assert.Equal(right.Select(note => note.Time), left.Select(note => note.Time));
        Assert.Equal(.30, Assert.Single(right.Single(note => note.TileIndex == 3).Lanes.Values));
        Assert.Equal(new[] { .11, .19 }, right.Single(note => note.TileIndex == 8).Lanes.Values.OrderBy(hold => hold));

        for (var i = 0; i < right.Count; i++)
        {
            var mirrored = right[i].Lanes.Reverse().Select(lane => KeyValuePair.Create(3 - lane.Key, lane.Value));
            Assert.Equal(mirrored, left[i].Lanes);
        }
    }

    private static TileHit[] EvenHits(int count, double gap, double angle) =>
        Enumerable.Range(0, count).Select(i => new TileHit(i + 1, i * gap, Angle: angle)).ToArray();

    private static IEnumerable<int> SingleKeys(IEnumerable<PlannedNote> notes) =>
        notes.Select(note => Assert.Single(note.Lanes.Keys));

    private static (int First, int Last, int Keys) Structure(PlannedNote note) =>
        (note.TileIndex, note.LastTileIndex, note.Lanes.Count);
}
