namespace RhythmHint.Core;

internal static partial class SteadyHands
{
    // Capture reservations before hand assignment fills the remaining lanes. Rolls
    // can reserve an index finger, just like an ordinary tap after assignment.
    private static bool[] UnreservedTaps(Attack[] attacks, int[] lanes, bool[] alternate)
    {
        var freeTap = new bool[attacks.Length];
        for (var i = 0; i < attacks.Length; i++)
            freeTap[i] = !attacks[i].IsChord && !attacks[i].DuringHold && lanes[i] == Unassigned &&
                !alternate[i] && (i + 1 == alternate.Length || !alternate[i + 1]);
        return freeTap;
    }

    private static void ApplyCornerTransitions(Attack[] attacks, int[] lanes, bool[] freeTap)
    {
        // Two equal steps turn a horizontal approach through a diagonal onto a vertical leg.
        // When it turns back horizontally at a faster pace, the vertical leg is the handoff.
        for (var i = 1; i + 2 < attacks.Length; i++)
        {
            if (!freeTap[i - 1] || !freeTap[i] || !freeTap[i + 1] || lanes[i - 1] != 2
                || attacks[i - 1].Axis != TrackAxis.Horizontal || attacks[i].Axis != TrackAxis.None
                || attacks[i + 1].Axis != TrackAxis.Vertical) continue;
            var approach = Gap(attacks, i);
            var turn = Turn(attacks[i]) % 90;
            if (!(turn > .5 && turn < 89.5) || !Similar(approach, Gap(attacks, i + 1))) continue;
            var exit = i + 2;
            if (freeTap[exit] && attacks[exit].Axis == TrackAxis.Vertical && Near(Turn(attacks[exit]), 180, .5)
                && Gap(attacks, exit) <= 1.5 * approach + Epsilon) exit++;
            if (exit == attacks.Length) continue;
            var exitGap = Gap(attacks, exit);
            if (attacks[exit].Axis != TrackAxis.Horizontal || !IsCorner(attacks[exit]) || exitGap > .8 * approach
                || exitGap > .213 || Lanes.HandOf(lanes[exit]) != Main) continue;
            // A main-hand double can anchor the exit too. A roll or a held lane cannot.
            if (!freeTap[exit] && (!attacks[exit].Double || attacks[exit].DuringHold)) continue;
            lanes[i] = 2;
            for (var j = i + 1; j < exit; j++) lanes[j] = 1;
            if (exit + 1 < attacks.Length && freeTap[exit + 1] && attacks[exit + 1].Axis == TrackAxis.Vertical
                && IsCorner(attacks[exit + 1]) && Similar(exitGap, Gap(attacks, exit + 1)))
                lanes[exit + 1] = 1;
        }

        // An equal-time straight between two alternating corner runs can continue their phase.
        // Both flanks must independently support the same alternation across the straight.
        for (var start = 3; start + 2 < attacks.Length; start++)
        {
            if (!freeTap[start] || !Near(Turn(attacks[start]), 180, .5) || !IsCorner(attacks[start - 1])) continue;
            var step = Gap(attacks, start);
            var exit = start + 1;
            while (exit < attacks.Length && freeTap[exit] && Near(Turn(attacks[exit]), 180, .5)
                && Similar(step, Gap(attacks, exit))) exit++;
            if (exit + 1 >= attacks.Length || !freeTap[start - 3] || !CornerStep(start - 2) || !CornerStep(start - 1)
                || !CornerStep(exit) || !CornerStep(exit + 1)) continue;
            var first = lanes[start - 2];
            var second = lanes[start - 1];
            if (first == second || lanes[start - 3] != second || lanes[exit] == lanes[exit + 1]
                || lanes[exit] != Phase(exit)) continue;
            for (var j = start; j < exit; j++) lanes[j] = Phase(j);
            start = exit - 1;

            int Phase(int j) => (j - start) % 2 == 0 ? first : second;
            bool CornerStep(int j) => freeTap[j] && IsCorner(attacks[j]) && Similar(step, Gap(attacks, j));
        }
    }

    private static bool IsCorner(Attack attack) => Near(Turn(attack) % 180, 90, .5);
}
