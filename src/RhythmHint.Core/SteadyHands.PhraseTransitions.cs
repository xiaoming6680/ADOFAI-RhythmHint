using System;

namespace RhythmHint.Core;

internal static partial class SteadyHands
{
    private static void FinishChordRuns(Attack[] attacks, int[] lanes, bool[] alternate, bool outerFirst)
    {
        for (var i = 1; i + 1 < attacks.Length; i++)
        {
            if (!alternate[i] || alternate[i + 1] || !attacks[i - 1].Double
                || attacks[i].IsChord || attacks[i + 1].IsChord || attacks[i].DuringHold || attacks[i + 1].DuringHold)
                continue;

            // Keep a short downbeat fill on the assigned hand before a rest.
            // Away from the downbeat leave both notes in the ordinary sequence.
            var step = Gap(attacks, i);
            var fill = Gap(attacks, i + 1);
            if (Quarter(attacks[i].Beat, .04) != 0 || fill <= Epsilon || fill > step / 2 + Epsilon
                || Gap(attacks, i + 2) < 2 * step - Epsilon)
                continue;

            var index = lanes[i];
            var outer = Lanes.Partner(index);
            lanes[i] = outerFirst ? outer : index;
            lanes[i + 1] = outerFirst ? index : outer;
        }
    }

    private static void FinishAlternatingTriplets(Attack[] attacks, int[] lanes, bool[] freeTap)
    {
        for (var end = 3; end + 1 < attacks.Length; end++)
        {
            var step = Gap(attacks, end);
            if (step <= .071 || step >= .136 || !Similar(step, Gap(attacks, end - 1)) || !Similar(step, Gap(attacks, end - 2))
                || !Near(attacks[end].Beat - attacks[end - 3].Beat, 1, .04)
                || Gap(attacks, end - 3) < 2 * step || Gap(attacks, end + 1) < 1.5 * step - Epsilon) continue;

            // Preserve an established alternating entry through the last note of the
            // triplet. Its exit starts a new pulse with the starting hand.
            var startLane = lanes[end - 3];
            var otherLane = lanes[end - 2];
            if (freeTap[end - 3] && freeTap[end - 2] && freeTap[end - 1] && freeTap[end] && freeTap[end + 1]
                && startLane != otherLane && lanes[end - 1] == startLane && lanes[end] == startLane
                && lanes[end + 1] == startLane)
                lanes[end] = otherLane;
        }
    }

    private static void ConnectAccelerations(Attack[] attacks, int[] lanes, bool[] freeTap)
    {
        for (var start = 3; start + 3 < attacks.Length; start++)
        {
            if (!freeTap[start - 1] || !freeTap[start] || !freeTap[start + 1] || !freeTap[start + 2]) continue;
            var step = Gap(attacks, start + 2);
            var lead = Gap(attacks, start);
            var middle = Gap(attacks, start + 1);
            if (step <= .071 || step >= .136 || !Similar(middle, 2 * step)) continue;

            var main = Lanes.IndexLane(Main);
            var left = Lanes.IndexLane(Left);
            // An even main-hand stream can keep its finger through two half-time
            // steps, then interleave once the next subdivision arrives.
            if (middle >= .15 - Epsilon && Similar(lead, middle) && Similar(Gap(attacks, start - 1), 2 * middle)
                && Similar(Gap(attacks, start - 2), 2 * middle)
                && freeTap[start - 2] && freeTap[start - 3] && lanes[start - 2] == main && lanes[start - 1] == main
                && lanes[start + 1] == main && lanes[start + 2] == left && freeTap[start + 3]
                && lanes[start + 3] == main && Similar(step, Gap(attacks, start + 3)))
                lanes[start] = main;

            // A lead-in at three subdivisions followed by two gives the main
            // hand its next two notes. Alternate only when every subdivision is hit.
            if (!Similar(lead, 3 * step) || !Similar(Gap(attacks, start - 1), step)
                || !Near(attacks[start].Beat - attacks[start - 1].Beat, .75, .04)
                || !Near(attacks[start + 2].Beat - attacks[start + 1].Beat, .25, .04)
                || lanes[start - 1] != left) continue;

            var end = start + 2;
            while (end + 1 < attacks.Length && freeTap[end + 1] && Similar(step, Gap(attacks, end + 1))) end++;
            // Only rewrite a complete group whose exit leaves room for either hand.
            if (Gap(attacks, end + 1) < 2 * step - Epsilon || (end + 1 < attacks.Length && !freeTap[end + 1])) continue;
            lanes[start] = main;
            lanes[start + 1] = main;
            for (var i = start + 2; i <= end; i++) lanes[i] = (i - start) % 2 == 0 ? left : main;
            start = end;
        }
    }

    private static void KeepSlowRepeatingFigures(Attack[] attacks, int[] lanes, bool[] freeTap)
    {
        var main = Lanes.IndexLane(Main);
        var left = Lanes.IndexLane(Left);
        for (var length = 2; length <= 8; length++)
        {
            for (var start = 1; start + 3 * length <= attacks.Length; start++)
            {
                if (!freeTap[start - 1] || lanes[start - 1] != main) continue;
                var diagonal = false;
                var varied = false;
                var end = start;
                for (; end < start + length && Eligible(end); end++)
                {
                    var turn = Turn(attacks[end]);
                    diagonal |= !Near(turn, Math.Round(turn / 90) * 90, .5);
                    varied |= !Near(turn, Turn(attacks[start]), .5);
                }
                if (end != start + length || !diagonal || !varied) continue;

                // A repeated shape can rotate without changing the tapping cadence.
                // A few isolated hand changes inside a main-hand run don't set a new phase.
                while (end < attacks.Length && Eligible(end) && Similar(Gap(attacks, end), Gap(attacks, end - length))
                    && Near(Turn(attacks[end]), Turn(attacks[end - length]), .5)) end++;
                if (end - start < 3 * length) continue;
                var leftTaps = 0;
                for (var i = start; i < end; i++)
                    if (lanes[i] == left) leftTaps++;
                if (leftTaps * 3 <= end - start)
                    for (var i = start; i < end; i++) lanes[i] = main;
                start = end - 1;
            }
        }

        bool Eligible(int i) => freeTap[i] && Gap(attacks, i) >= .175
            && (lanes[i] != left || lanes[i - 1] != left);
    }
}
