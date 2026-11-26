using System;
using System.Collections.Generic;

namespace RhythmHint.Core;

internal static partial class SteadyHands
{
    private const double MinimumHandGap = .120;
    private const double NoPress = double.NegativeInfinity;

    private readonly record struct RecentPresses(double D, double F, double J, double K)
    {
        public RecentPresses Since(double cutoff)
        {
            return new RecentPresses(Keep(D), Keep(F), Keep(J), Keep(K));

            double Keep(double time) => time > cutoff ? time : NoPress;
        }

        public int KeyMask => (D > NoPress ? 1 : 0) | (F > NoPress ? 2 : 0)
            | (J > NoPress ? 4 : 0) | (K > NoPress ? 8 : 0);

        public RecentPresses Press(double time, int mask) => new(
            (mask & 1) != 0 ? time : D, (mask & 2) != 0 ? time : F,
            (mask & 4) != 0 ? time : J, (mask & 8) != 0 ? time : K);
    }

    private readonly record struct RecoveryCost(int EarlyHands, int EarlyKeys, int Moved)
    {
        public bool BetterThan(RecoveryCost other) =>
            (EarlyKeys, EarlyHands, Moved).CompareTo((other.EarlyKeys, other.EarlyHands, other.Moved)) < 0;
    }

    private sealed record RecoveryStep(RecoveryCost Cost, bool Mirror, RecoveryStep? Previous);

    // Compare the two possible hands for each tap or same-hand double.
    // If both hands can meet the interval there are no short repeats of any key.
    // Dense bursts that cannot meet it avoid key repeats first, then minimize
    // short hand intervals and changes to the original musical fingering.
    private static void EnforceHandRecovery(List<PlannedNote> notes)
    {
        var empty = new RecentPresses(NoPress, NoPress, NoPress, NoPress);
        var active = new Dictionary<RecentPresses, RecoveryStep> { [empty] = new(default, false, null) };
        var releases = new[] { NoPress, NoPress, NoPress, NoPress };

        foreach (var note in notes)
        {
            var original = 0;
            var occupied = 0;
            var startsHold = false;
            for (var lane = 0; lane < releases.Length; lane++)
                if (releases[lane] >= note.Time - Epsilon) occupied |= 1 << lane;
            foreach (var pair in note.Lanes)
            {
                original |= 1 << pair.Key;
                if (pair.Value <= 0) continue;
                startsHold = true;
                releases[pair.Key] = Math.Max(releases[pair.Key], note.Time + pair.Value);
            }

            var reversed = 0;
            // A note that needs both hands already uses both. Keep all hold starts
            // on their planned lanes so subsequent free keys can be computed here.
            if (!startsHold && CountHands(original) == 1)
                foreach (var lane in note.Lanes.Keys)
                    reversed |= 1 << (Lanes.Count - 1 - lane);

            var next = new Dictionary<RecentPresses, RecoveryStep>();
            foreach (var entry in active)
            {
                var previous = entry.Value;
                var recent = entry.Key.Since(note.Time - MinimumHandGap + Epsilon);
                var recentlyUsed = recent.KeyMask;
                Consider(original, false);
                if (reversed != 0 && (reversed & occupied) == 0) Consider(reversed, true);

                void Consider(int mask, bool mirror)
                {
                    var earlyHands = CountHands(mask & HandMask(recentlyUsed));
                    var cost = new RecoveryCost(previous.Cost.EarlyHands + earlyHands,
                        previous.Cost.EarlyKeys + CountKeys(mask & recentlyUsed),
                        previous.Cost.Moved + (mirror ? note.Lanes.Count : 0));
                    var now = recent.Press(note.Time, mask);
                    if (!next.TryGetValue(now, out var best) || cost.BetterThan(best.Cost))
                        next[now] = new RecoveryStep(cost, mirror, previous);
                }
            }
            active = next;
        }

        RecoveryStep? step = null;
        foreach (var path in active.Values)
            if (step == null || path.Cost.BetterThan(step.Cost)) step = path;

        for (var i = notes.Count - 1; i >= 0 && step != null; i--, step = step.Previous)
        {
            if (!step.Mirror) continue;
            var note = notes[i];
            var reversed = new PlannedNote(note.TileIndex, note.Time) { LastTileIndex = note.LastTileIndex };
            foreach (var pair in note.Lanes)
                reversed.Lanes[Lanes.Count - 1 - pair.Key] = pair.Value;
            notes[i] = reversed;
        }
    }

    private static int CountHands(int mask) => ((mask & 3) != 0 ? 1 : 0) + ((mask & 12) != 0 ? 1 : 0);

    private static int HandMask(int mask) => ((mask & 3) != 0 ? 3 : 0) | ((mask & 12) != 0 ? 12 : 0);

    private static int CountKeys(int mask) => (mask & 1) + ((mask >> 1) & 1) + ((mask >> 2) & 1) + ((mask >> 3) & 1);
}
