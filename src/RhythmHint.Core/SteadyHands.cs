using System;
using System.Collections.Generic;

namespace RhythmHint.Core;

/// <summary>Assign both hand sequences directly from the chart's rhythm and track.</summary>
internal static partial class SteadyHands
{
    private const int Left = 0, Main = 1, Unassigned = -1;
    private const double Epsilon = 1e-6;
    private const double SingleFingerRecovery = .15;

    private readonly record struct Attack(TileHit Hit, int Presses, double Beat, TrackAxis Axis, double DoubleTurn, bool DuringHold)
    {
        public bool IsChord => Presses > 1;
        public bool Double => Presses == 2;
    }

    public static List<PlannedNote> Plan(List<List<TileHit>> moments, FingeringOptions options, FingeringRhythm? rhythm)
    {
        if (moments.Count == 0) return new List<PlannedNote>();
        var attacks = ReadAttacks(moments, rhythm);
        var lanes = new int[attacks.Length];
        for (var i = 0; i < lanes.Length; i++) lanes[i] = Unassigned;
        if (options.UseRoll) AssignRolls(attacks, lanes, options.OuterRoll);
        var alternate = options.ChordsOnOtherHand ? new bool[attacks.Length] : FindAlternatingChordRuns(attacks, lanes);
        var freeTap = UnreservedTaps(attacks, lanes, alternate);
        AssignHands(attacks, lanes, options.ChordsOnOtherHand, alternate);
        if (options.UseRoll) FinishChordRuns(attacks, lanes, alternate, options.OuterRoll);
        ApplyCornerTransitions(attacks, lanes, freeTap);
        FinishAlternatingTriplets(attacks, lanes, freeTap);
        ConnectAccelerations(attacks, lanes, freeTap);
        KeepSlowRepeatingFigures(attacks, lanes, freeTap);
        var notes = WriteNotes(moments, lanes);
        EnforceHandRecovery(notes);
        return notes;
    }

    private static Attack[] ReadAttacks(List<List<TileHit>> moments, FingeringRhythm? rhythm)
    {
        var beats = rhythm?.Beats;
        if (rhythm != null && (beats == null || beats.Count < 2))
            beats = BeatGrid.Build(rhythm.Tempo, rhythm.Origin, moments[moments.Count - 1][0].Time + 2);

        var grid = 0;
        var heldUntil = double.NegativeInfinity;
        var attacks = new Attack[moments.Count];
        for (var i = 0; i < moments.Count; i++)
        {
            var hit = moments[i][0];
            var beat = double.NaN;
            if (beats != null && beats.Count >= 2)
            {
                while (grid + 2 < beats.Count && hit.Time >= beats[grid + 1].Time) grid++;
                var duration = beats[grid + 1].Time - beats[grid].Time;
                if (duration > 0 && hit.Time >= beats[0].Time - duration && hit.Time <= beats[beats.Count - 1].Time + duration)
                    beat = grid + (hit.Time - beats[grid].Time) / duration;
            }

            var axis = TrackAxis.None;
            rhythm?.IncomingAxes?.TryGetValue(hit.TileIndex, out axis);
            var doubleTap = moments[i].Count == 2;
            foreach (var part in moments[i])
                if (part.HoldSeconds > 0) heldUntil = Math.Max(heldUntil, hit.Time + part.HoldSeconds);
            attacks[i] = new Attack(hit, moments[i].Count, beat, axis, doubleTap ? moments[i][1].Angle : double.NaN,
                heldUntil >= hit.Time - Epsilon);
        }
        return attacks;
    }

    private static void AssignHands(Attack[] attacks, int[] lanes, bool otherChordHand, bool[] alternate)
    {
        // Two states per attack. Scoring the full sequence lets a regular passage
        // select its starting hand from both the lead-in and the exit.
        var previousHands = new int[attacks.Length, 2];
        var pickupPulses = FindPickupPulses(attacks);
        var shiftedStarts = FindShiftedStarts(attacks);
        var axisPulses = FindAxisPulses(attacks);
        var leftScore = 0.0;
        var mainScore = 0.0;
        for (var i = 0; i < attacks.Length; i++)
        {
            var switchScore = i == 0 ? 0 : SwitchScore(attacks, i, pickupPulses[i]);
            var leftFromMain = alternate[i] || mainScore + switchScore > leftScore;
            var mainFromLeft = alternate[i] || leftScore + switchScore > mainScore;
            previousHands[i, Left] = leftFromMain ? Main : Left;
            previousHands[i, Main] = mainFromLeft ? Left : Main;
            var nextLeft = leftFromMain ? mainScore + switchScore : leftScore;
            var nextMain = mainFromLeft ? leftScore + switchScore : mainScore;
            var mainPreference = MainPreference(attacks, i, otherChordHand, shiftedStarts[i]);
            if (axisPulses[i])
                mainPreference += attacks[i].Axis == TrackAxis.Horizontal ? 4 : -4;
            var forcedHand = lanes[i] == Unassigned ? Unassigned : Lanes.HandOf(lanes[i]);
            leftScore = forcedHand == Main ? double.NegativeInfinity : nextLeft;
            mainScore = forcedHand == Left ? double.NegativeInfinity : nextMain + mainPreference;
        }

        var hand = mainScore >= leftScore ? Main : Left;
        for (var i = attacks.Length - 1; i >= 0; i--)
        {
            if (lanes[i] == Unassigned) lanes[i] = Lanes.IndexLane(hand);
            hand = previousHands[i, hand];
        }
    }

    private static double MainPreference(Attack[] attacks, int i, bool otherChordHand, int shiftedPhase)
    {
        var attack = attacks[i];
        var quarter = Quarter(attack.Beat, attack.Double ? .015 : .05);
        var before = Gap(attacks, i);
        var after = Gap(attacks, i + 1);
        var nearby = Math.Min(before, after);
        var offQuarter = quarter == 1 || quarter == 3;
        if (attack.Presses > 2) return otherChordHand ? -9.0 : 9.0;
        if (attack.Double)
        {
            var previousDouble = i > 0 && attacks[i - 1].Double;
            var nextDouble = i + 1 < attacks.Length && attacks[i + 1].Double;
            // Close offbeats usually take the other hand. A slower straight
            // double often belongs to the main-hand pulse like its neighbours.
            var left = offQuarter && (!Near(Turn(attack), 180, .5) || nearby < .138);
            if (quarter == 2)
                left = (previousDouble && NearHalfBeat(i - 1, before)) || (nextDouble && NearHalfBeat(i + 1, after))
                    || (Near(attack.DoubleTurn, 15, .5) && (RepeatsHalfBeatDouble(-1) || RepeatsHalfBeatDouble(1)));
            if (quarter == 1 && previousDouble && nextDouble && MatchesBeatGap(i - 1, .75) && MatchesBeatGap(i + 1, .75))
                left = false;
            return left ^ otherChordHand ? -9.0 : 9.0;
        }

        var preference = .30;
        if (quarter == 0) preference += 2.6;
        else if (offQuarter)
            preference -= nearby < .105 || (nearby < .138 && (MatchesBeatGap(i - 1, .25) || MatchesBeatGap(i + 1, .25))) ? 2.4 : .45;
        else if (quarter == 2)
        {
            var quickHalf = (before < .128 && MatchesBeatGap(i - 1, .5)) || (after < .128 && MatchesBeatGap(i + 1, .5));
            preference += quickHalf ? -1.7 : .65;
        }

        var turnsInto = i > 0 && TrackSwitch(attacks[i - 1], attack);
        var turnsOut = i + 1 < attacks.Length && TrackSwitch(attack, attacks[i + 1]);
        if (nearby >= .10 && (turnsInto || turnsOut))
            preference += attack.Axis == TrackAxis.Horizontal ? 2.6 : -2.6;

        if (offQuarter && Near(Turn(attack), 270, .5) && MatchesBeatGap(i - 1, .75))
            preference -= 3;

        if (shiftedPhase != 0) preference += shiftedPhase * 5;

        return preference;

        bool MatchesBeatGap(int j, double expected) => j >= 0 && j < attacks.Length
            && Math.Abs(Math.Abs(attacks[j].Beat - attack.Beat) - expected) <= .045;
        bool NearHalfBeat(int j, double gap) => gap < .125 && MatchesBeatGap(j, .5);
        bool RepeatsHalfBeatDouble(int direction)
        {
            var between = i + direction;
            var echo = i + 2 * direction;
            return echo >= 0 && echo < attacks.Length && !attacks[between].Double && attacks[echo].Double
                && (MatchesBeatGap(echo, 1) || (MatchesBeatGap(echo, 1.5) && Near(attacks[echo].DoubleTurn, 15, .5)));
        }
    }

    private static double SwitchScore(Attack[] attacks, int i, bool inPickupPulse)
    {
        var before = Gap(attacks, i - 1);
        var gap = Gap(attacks, i);
        var after = Gap(attacks, i + 1);
        var current = attacks[i];
        var previous = attacks[i - 1];
        if (previous.IsChord || current.IsChord)
        {
            if (previous.IsChord && current.IsChord) return 0;
            var sameSubdivision = Math.Abs(current.Beat - previous.Beat) <= .30;
            // Dense interleaving and turns around a double often put the single
            // on the other hand. A slower straight pulse often stays together.
            if ((sameSubdivision && gap < .138) || gap < .11) return 5;
            if (gap < .136) return 3;
            var halfBeat = Near(Math.Abs(current.Beat - previous.Beat), .5, .04);
            var arrival = Turn(current);
            if (halfBeat && (Near(arrival, 75, .5) || Near(arrival, 90, .5))) return 4.5;
            return gap < .22 ? -4 : 0;
        }

        // The fast alternating passages taper into slower parts rather than
        // forbidding same-finger taps inside a fixed recovery window.
        if (gap < .136) return Math.Min(4.5, (.136 - gap) * 100 + 1);

        var turn = Turn(current);
        var shorterNeighbour = Math.Min(before, after) < .75 * gap;
        var lateTurn = Near(turn, 270, .5);
        if (lateTurn && Near(current.Beat - previous.Beat, .75, .04)) return 3.8;
        var corner = Near(turn, 90, .5) || lateTurn;
        if (corner && !inPickupPulse && !shorterNeighbour)
        {
            var fasterEntry = Similar(2 * gap, before) || Similar(2 * gap, after);
            var even = Similar(gap, before) || Similar(gap, after);
            if (gap < .213) return fasterEntry ? 3.4 : even ? 2.6 : 2.0;
            if (lateTurn && gap < .5) return even || TrackSwitch(previous, current) ? 3.1 : 1.8;
        }

        if (gap >= .5) return 0;
        return inPickupPulse || Near(turn, 180, .5) || shorterNeighbour ? -3.8 : -1.4;
    }

    // In a straight run entered and left through half-step turns, the hand on
    // that axis keeps the full pulse even across several consecutive strong beats.
    private static bool[] FindAxisPulses(Attack[] attacks)
    {
        var pulse = new bool[attacks.Length];
        for (var start = 1; start + 3 < attacks.Length; start++)
        {
            var step = Gap(attacks, start + 1);
            if (step < .136 || step >= .5 || !TrackSwitch(attacks[start - 1], attacks[start])
                || !FreeTap(start - 1) || !FreeTap(start)
                || !Similar(step, 2 * Gap(attacks, start))) continue;

            var end = start;
            while (end + 1 < attacks.Length && attacks[end + 1].Axis == attacks[start].Axis
                && FreeTap(end + 1) && Similar(step, Gap(attacks, end + 1)))
                end++;
            if (end - start >= 2 && end + 1 < attacks.Length && TrackSwitch(attacks[end], attacks[end + 1])
                && FreeTap(end + 1) && Similar(step, 2 * Gap(attacks, end + 1)))
                for (var i = start; i <= end; i++) pulse[i] = true;
            start = end;
        }
        return pulse;

        bool FreeTap(int i) => !attacks[i].IsChord && !attacks[i].DuringHold;
    }

    private static int[] FindShiftedStarts(Attack[] attacks)
    {
        var phase = new int[attacks.Length];
        for (var start = 1; start + 3 < attacks.Length; start++)
        {
            var step = Gap(attacks, start + 1);
            var leadIn = Gap(attacks, start);
            var quarter = Quarter(attacks[start].Beat, .035);
            if (step < .073 || step > .10 || leadIn / step < .88 || leadIn / step > .945
                || (quarter != 0 && quarter != 2) || attacks[start].IsChord
                || (!attacks[start - 1].Double && Gap(attacks, start - 1) < 1.5 * step)) continue;

            // An accelerated entry can shift a straight stream by one hit even
            // when the inferred beat grid still puts its first note on a pulse.
            var end = start;
            while (end + 1 < attacks.Length && !attacks[end + 1].IsChord && Near(Turn(attacks[end + 1]), 180, .5)
                && Similar(step, Gap(attacks, end + 1))) end++;
            if (end - start < 3) continue;
            for (var i = start; i <= end; i++) phase[i] = (i - start) % 2 == 0 ? -1 : 1;
            start = end;
        }
        return phase;
    }

    // Each dense double takes one rhythmic slot, including streams made entirely
    // of doubles. Keep both phases unless an entry determines the opening hand.
    private static bool[] FindAlternatingChordRuns(Attack[] attacks, int[] lanes)
    {
        var alternate = new bool[attacks.Length];
        for (var start = 0; start + 1 < attacks.Length;)
        {
            var step = Gap(attacks, start + 1);
            if (step <= .071 || step >= .136 || attacks[start].Presses > 2 || attacks[start].DuringHold || lanes[start] != Unassigned)
            {
                start++;
                continue;
            }
            var entry = Gap(attacks, start);
            var entersFromRoll = attacks[start].Double && start > 1 && !attacks[start - 1].IsChord
                && !attacks[start - 1].DuringHold && lanes[start - 1] != Unassigned && entry <= .071;
            var slowing = false;
            var end = start;
            var doubles = attacks[start].Double ? 1 : 0;
            while (end + 1 < attacks.Length && attacks[end + 1].Presses <= 2
                && !attacks[end + 1].DuringHold && lanes[end + 1] == Unassigned)
            {
                var gap = Gap(attacks, end + 1);
                if (!Similar(step, gap))
                {
                    // A straight slowdown can keep the same rhythmic subdivision
                    // even though the interval in seconds increases on every tile.
                    if (!entersFromRoll || gap >= SingleFingerRecovery || !ContinuesSlowdown(end + 1)) break;
                    slowing = true;
                }
                end++;
                if (attacks[end].Double) doubles++;
            }
            var count = end - start + 1;
            if (count >= 6 && (doubles >= 2 || slowing))
            {
                if (attacks[start].Double && !alternate[start])
                {
                    if (entersFromRoll)
                        alternate[start] = true;
                    else if (entry >= 2 * step - Math.Max(.001, step * .035))
                        lanes[start] = Lanes.IndexLane(Main);
                }
                for (var i = start + 1; i <= end; i++) alternate[i] = true;
                // Once the same subdivision is slow enough for one finger,
                // let the main hand start the slower pulse immediately.
                if (slowing && end + 1 < attacks.Length && lanes[end + 1] == Unassigned
                    && Gap(attacks, end + 1) >= SingleFingerRecovery && ContinuesSlowdown(end + 1))
                    lanes[end + 1] = Lanes.IndexLane(Main);
            }
            start = Math.Max(start + 1, end);
        }
        return alternate;

        bool ContinuesSlowdown(int i)
        {
            var gap = Gap(attacks, i);
            var previous = Gap(attacks, i - 1);
            return !attacks[i].IsChord && !attacks[i].DuringHold && Near(Turn(attacks[i]), 180, .5)
                && Near(attacks[i].Beat - attacks[i - 1].Beat, .25, .05)
                && gap > previous && gap <= 1.08 * previous;
        }
    }

    private static bool[] FindPickupPulses(Attack[] attacks)
    {
        var pulse = new bool[attacks.Length];
        for (var start = 0; start + 3 < attacks.Length;)
        {
            var gap = Gap(attacks, start + 1);
            if (gap < .136 || gap > .213 || attacks[start].IsChord)
            {
                start++;
                continue;
            }

            var end = start;
            while (end + 1 < attacks.Length && !attacks[end + 1].IsChord && Similar(gap, Gap(attacks, end + 1))) end++;
            if (end - start >= 3 && (SplitBeat(start - 1, start) || SplitBeat(end + 1, end + 2)))
                for (var i = start + 1; i <= end; i++) pulse[i] = true;
            start = Math.Max(start + 1, end);

            bool SplitBeat(int a, int b) => a > 0 && b < attacks.Length && !attacks[a].IsChord && !attacks[b].IsChord
                && Similar(gap, 2 * Gap(attacks, a)) && Similar(gap, 2 * Gap(attacks, b));
        }
        return pulse;
    }

    private static void AssignRolls(Attack[] attacks, int[] lanes, bool outerFirst, bool timingOnly = false)
    {
        var cycle = outerFirst ? new[] { 3, 2, 0, 1 } : new[] { 2, 3, 1, 0 };
        for (var start = 0; start + 1 < attacks.Length;)
        {
            var end = start;
            var interval = Gap(attacks, start + 1);
            if (interval > .071 || interval <= Epsilon || IsHeldOrDouble(attacks[start]))
            {
                start++;
                continue;
            }

            while (end + 1 < attacks.Length && !IsHeldOrDouble(attacks[end + 1]) && Similar(interval, Gap(attacks, end + 1)))
                end++;

            var count = end - start + 1;
            var veryDense = interval <= .064;
            if (count >= 4 && (veryDense || count >= 12))
            {
                for (var i = start; i <= end; i++) lanes[i] = cycle[(i - start) % cycle.Length];
                if (count == 4 && interval > .055)
                {
                    lanes[end - 1] = Lanes.IndexLane(Left);
                    lanes[end] = Lanes.IndexLane(Main);
                }
                else if (Gap(attacks, end + 1) >= interval * 1.97 && (lanes[end] == 0 || lanes[end] == 3))
                {
                    lanes[end] = Lanes.Partner(lanes[end]);
                }
            }
            else if (count == 3)
            {
                // In the borderline speed range, a three-tap fill can either lead
                // back onto the main pulse or finish before a rest. Read its exit.
                var pulse = 4 * interval;
                var onMainPulse = !veryDense && start > 0 && end + 1 < attacks.Length
                    && (timingOnly ? Similar(pulse, Gap(attacks, start - 1)) : Quarter(attacks[start].Beat, .04) == 0)
                    && Similar(pulse, Gap(attacks, start))
                    && !attacks[start - 1].IsChord && !attacks[start].DuringHold
                    && !attacks[end + 1].IsChord && !attacks[end + 1].DuringHold;
                var exitGap = Gap(attacks, end + 1);
                var returnsOnPulse = onMainPulse && Similar(pulse, attacks[end + 1].Hit.Time - attacks[start].Hit.Time)
                    && Similar(pulse, Gap(attacks, end + 2));
                if (veryDense || returnsOnPulse)
                {
                    lanes[start] = cycle[0];
                    lanes[start + 1] = cycle[1];
                    lanes[end] = Lanes.IndexLane(Left);
                    if (returnsOnPulse) lanes[end + 1] = Lanes.IndexLane(Main);
                }
                else if (onMainPulse && exitGap >= pulse - Epsilon)
                {
                    lanes[start] = Lanes.IndexLane(Main);
                    lanes[start + 1] = Lanes.IndexLane(Left);
                    lanes[end] = Lanes.IndexLane(Main);
                }
            }

            // A cadence change can leave the same onset at the edge of both runs.
            start = Math.Max(start + 1, end);
        }
    }

    private static List<PlannedNote> WriteNotes(List<List<TileHit>> moments, int[] lanes)
    {
        var notes = new List<PlannedNote>(moments.Count);
        var releases = new[] { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
        for (var i = 0; i < moments.Count; i++)
        {
            var group = moments[i];
            var first = group[0];
            var note = new PlannedNote(first.TileIndex, first.Time) { LastTileIndex = group[group.Count - 1].TileIndex };
            var lane = lanes[i];
            var hand = Lanes.HandOf(lane);
            var other = Lanes.IndexLane(1 - hand);
            if (group.Count == 2)
            {
                var heldOnHand = HeldCount(hand);
                var heldOnOther = HeldCount(1 - hand);
                var secondLane = Lanes.Partner(lane);
                if (heldOnHand == 1 && heldOnOther == 1)
                {
                    if (Held(lane)) lane = secondLane;
                    secondLane = Held(other) ? Lanes.Partner(other) : other;
                }
                else if (heldOnHand > heldOnOther)
                {
                    lane = other;
                    secondLane = Lanes.Partner(lane);
                }

                Add(lane, first.HoldSeconds);
                Add(secondLane, group[1].HoldSeconds);
            }
            else if (group.Count > 2)
            {
                if (HeldCount(hand) > HeldCount(1 - hand)) (lane, other) = (other, lane);
                if (Held(other) && !Held(Lanes.Partner(other))) other = Lanes.Partner(other);
                Add(lane, first.HoldSeconds);
                Add(Lanes.Partner(lane), group[1].HoldSeconds);
                Add(other, group[2].HoldSeconds);
                if (group.Count == 4) Add(Lanes.Partner(other), group[3].HoldSeconds);
            }
            else
            {
                if (HeldCount(hand) > 0 && !Held(other)) lane = other;
                else if (Held(lane) && !Held(Lanes.Partner(lane))) lane = Lanes.Partner(lane);
                else if (Held(lane) && !Held(Lanes.Partner(other))) lane = Lanes.Partner(other);
                Add(lane, first.HoldSeconds);
            }
            notes.Add(note);

            bool Held(int key) => releases[key] >= first.Time - Epsilon;
            int HeldCount(int side) => (Held(2 * side) ? 1 : 0) + (Held(2 * side + 1) ? 1 : 0);
            void Add(int key, double hold)
            {
                note.Lanes.Add(key, hold);
                if (hold > 0) releases[key] = Math.Max(releases[key], first.Time + hold);
            }
        }
        return notes;
    }

    private static bool IsHeldOrDouble(Attack attack) => attack.IsChord || attack.Hit.HoldSeconds > 0;
    private static bool TrackSwitch(Attack first, Attack second) => first.Axis != TrackAxis.None
        && second.Axis != TrackAxis.None && first.Axis != second.Axis;
    private static double Gap(Attack[] attacks, int i) => i > 0 && i < attacks.Length
        ? attacks[i].Hit.Time - attacks[i - 1].Hit.Time : double.PositiveInfinity;
    private static bool Similar(double first, double second) => Near(first, second, Math.Max(.001, first * .035));
    private static bool Near(double value, double expected, double tolerance) => Math.Abs(value - expected) <= tolerance;

    private static double Turn(Attack attack) => Math.Abs(attack.Hit.Angle) % 360;

    private static int Quarter(double beat, double tolerance)
    {
        var tick = Math.Round(beat * 4);
        return Near(beat * 4, tick, tolerance * 4) ? ((int)tick % 4 + 4) % 4 : -1;
    }
}
