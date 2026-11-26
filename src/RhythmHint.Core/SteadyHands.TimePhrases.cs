using System;
using System.Collections.Generic;

namespace RhythmHint.Core;

internal static partial class SteadyHands
{
    // The input notes are the time-reset plan. Only their hand/finger assignments change.
    // No beat grid, floor direction or per-chart reference is passed into this pass.
    internal static void ApplyTimePhrases(List<List<TileHit>> moments, List<PlannedNote> notes, FingeringOptions options)
    {
        if (notes.Count == 0) return;
        var attacks = ReadAttacks(moments, null);
        var rolls = new int[notes.Count];
        for (var i = 0; i < rolls.Length; i++) rolls[i] = Unassigned;
        if (options.UseRoll) AssignRolls(attacks, rolls, options.OuterRoll, timingOnly: true);

        // Near a hold or a multi-hand chord, keep the time-reset plan and its release handling.
        var blockedUntil = double.NegativeInfinity;
        var start = 0;
        for (var i = 0; i < notes.Count; i++)
        {
            var attack = attacks[i];
            if (attack.DuringHold || attack.Presses > 2)
                foreach (var hold in notes[i].Lanes.Values)
                    blockedUntil = Math.Max(blockedUntil, attack.Hit.Time + hold + options.JackWindow);
            if (attack.Hit.Time <= blockedUntil || (i + 1 < notes.Count &&
                (attacks[i + 1].DuringHold || attacks[i + 1].Presses > 2) && Gap(attacks, i + 1) <= options.JackWindow))
            {
                Arrange(start, i);
                start = i + 1;
            }
            else if (i > start && Gap(attacks, i) > 4 * Math.Max(options.JackWindow, options.ResetWindow))
            {
                Arrange(start, i);
                start = i;
            }
        }
        Arrange(start, notes.Count);

        void Arrange(int first, int end)
        {
            if (end - first < 2) return;
            var parents = new int[end - first, 2];
            var dotted = FindDottedPulse(attacks, first, end);
            var left = 0.0;
            var right = 0.0;
            var alternatingLimit = Math.Max(options.ResetWindow, .85 * options.JackWindow);
            for (var i = first; i < end; i++)
            {
                var before = i == first ? double.PositiveInfinity : Gap(attacks, i);
                var after = i + 1 == end ? double.PositiveInfinity : Gap(attacks, i + 1);
                var steps = dotted[i - first];
                var change = steps == 0 ? ChangeScore(i, before, alternatingLimit, first, end)
                    : steps % 2 == 0 ? -5 : 5;
                var fromRight = right + change > left;
                var fromLeft = left + change > right;
                parents[i - first, Left] = fromRight ? Main : Left;
                parents[i - first, Main] = fromLeft ? Left : Main;
                var nextLeft = fromRight ? right + change : left;
                var nextRight = fromLeft ? left + change : right;
                // A break and a slower pulse both anchor the main hand. Between
                // anchors the better phase is chosen using both ends of the phrase.
                var preference = before > alternatingLimit && after > alternatingLimit ? 1.5 : .05;
                if (steps != 0) preference = .05;
                if (before > 1.5 * alternatingLimit) preference += .6;
                if (after > 1.5 * alternatingLimit) preference += .35;
                if (attacks[i].Double) preference += 1;
                if (notes[i].PairHand == Main) preference += .04;
                else preference -= .04;
                var forced = rolls[i] >= 0 ? Lanes.HandOf(rolls[i]) : Unassigned;
                if (attacks[i].Double && options.ChordsOnOtherHand) forced = notes[i].PairHand;
                left = forced == Main ? double.NegativeInfinity : nextLeft;
                right = forced == Left ? double.NegativeInfinity : nextRight + preference;
            }

            var hand = right >= left ? Main : Left;
            for (var i = end - 1; i >= first; i--)
            {
                var lane = rolls[i] >= 0 ? rolls[i] : Lanes.IndexLane(hand);
                notes[i].Lanes.Clear();
                notes[i].Lanes.Add(lane, 0);
                if (attacks[i].Double) notes[i].Lanes.Add(Lanes.Partner(lane), 0);
                hand = parents[i - first, hand];
            }
        }

        double ChangeScore(int i, double gap, double limit, int first, int end)
        {
            if (gap <= limit) return 5;
            // A skipped subdivision advances the same alternating clock. E.g.
            // 75, 150, 75 ms gives switch, stay, switch; 225 spans three ticks.
            for (var distance = 1; distance <= 4; distance++)
            {
                var prior = i - distance > first ? Gap(attacks, i - distance) : double.PositiveInfinity;
                var next = i + distance < end ? Gap(attacks, i + distance) : double.PositiveInfinity;
                var tick = Math.Min(prior, next);
                if (tick <= Epsilon || tick > limit || tick < gap / 4.1) continue;
                var steps = (int)Math.Round(gap / tick);
                if (steps >= 2 && Similar(gap, steps * tick)) return steps % 2 == 0 ? -3 : 3;
            }
            return 0;
        }

    }

    // Look for recurring ratios of 2:3 or 3:4 to establish a slower subdivision.
    // Extend it only over single taps that stay on this grid, up to a change in cadence.
    private static int[] FindDottedPulse(Attack[] attacks, int first, int end)
    {
        var steps = new int[end - first];
        for (var i = first + 1; i + 1 < end; i++)
        {
            var a = Gap(attacks, i);
            var b = Gap(attacks, i + 1);
            var small = Math.Min(a, b);
            var large = Math.Max(a, b);
            if (!Similar(large, 1.5 * small) && !Similar(large, 4 * small / 3)) continue;
            var tick = Similar(large, 1.5 * small) ? small / 2 : small / 3;
            if (tick < .1 || tick > .20 || Count(i) == 0 || Count(i + 1) == 0) continue;
            var lo = i;
            while (lo > first + 1 && Count(lo - 1) > 0 && steps[lo - first - 1] == 0) lo--;
            var hi = i + 2;
            while (hi < end && Count(hi) > 0) hi++;
            var dotted = 0;
            var even = 0;
            for (var j = lo; j < hi; j++)
            {
                var n = Count(j);
                if (n == 3) dotted++;
                if (n % 2 == 0) even++;
            }
            if (dotted >= 2 && even >= 2)
                for (var j = lo; j < hi; j++) steps[j - first] = Count(j);
            i = hi - 2;

            int Count(int j)
            {
                if (attacks[j - 1].IsChord || attacks[j].IsChord) return 0;
                var gap = Gap(attacks, j);
                var n = (int)Math.Round(gap / tick);
                return n >= 1 && n <= 4 && Similar(gap, n * tick) ? n : 0;
            }
        }
        return steps;
    }
}
