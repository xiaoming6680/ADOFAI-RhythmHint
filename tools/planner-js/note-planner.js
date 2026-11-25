/* Shared browser port of src/RhythmHint.Core/NotePlanner.cs.
 * Keep the method order, epsilon and post-processing in sync with the C# source.
 * This plans already-timed TileHits; .adofai timing is a separate concern.
 */
(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.NotePlanner = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';
  const Epsilon = 1e-6;
  const AngleTolerance = 0.1;
  const LinkGap = 0.3; // C# LayoutStats.DefaultLinkGap
  const revision = '054fe9cd6a8961afbf0c8f9c181bd3dcedb12b18';
  const LeftOuter = 0, LeftIndex = 1, RightIndex = 2, RightOuter = 3;
  const defaults = Object.freeze({
    ChordWindow: 0.035,
    ChordAngle: 30,
    ChordMaxGap: 0.06,
    ResetWindow: 0.13,
    ChordFocusWindow: 0.45,
    JackWindow: 0.15,
    UseRoll: true,
    OuterRoll: true,
    LeftHanded: false,
    ChordsOnOtherHand: false,
    Style: 'TimeReset',
    ReviewShortMinGap: 0.060,
    ReviewShortMaxGap: 0.0825,
    ReviewShortMinNotes: 4,
    ReviewShortMaxNotes: 5,
    ReviewTimingTolerance: 0.001,
    Keys: 4
  });
  const handOf = lane => lane < 2 ? 0 : 1;
  const indexLane = hand => hand === 0 ? LeftIndex : RightIndex;
  const partner = lane => lane === 0 ? 1 : lane === 1 ? 0 : lane === 2 ? 3 : 2;
  const sorted = lanes => lanes.sort((a, b) => a.lane - b.lane);
  const firstLane = note => note.lanes[0].lane;
  const isChord = note => note.lanes.length > 1;
  const hasLane = (note, lane) => note.lanes.some(item => item.lane === lane);
  const pairHand = note => note.lanes.reduce((sum, item) => sum + handOf(item.lane), 0) * 2 >= note.lanes.length ? 1 : 0;
  // 6K / 8K add a suffix; the 4K string stays as it was.
  const algorithmFor = options => 'NotePlanner/' + revision + '/' + (options?.Style ?? defaults.Style)
    + ((options?.Keys ?? defaults.Keys) !== 4 ? '/' + options.Keys + 'K' : '');
  // C# KeyLayout: left half is the left hand; fingers count outwards from the middle (index, middle, ring, little).
  const layoutOf = keys => {
    const per = keys / 2;
    return { count: keys, per, handOf: lane => lane < per ? 0 : 1, finger: lane => lane < per ? per - 1 - lane : lane - per,
      lane: (hand, finger) => hand === 0 ? per - 1 - finger : per + finger, mirror: lane => keys - 1 - lane };
  };
  const fourKeys = layoutOf(4);

  function plan(hits, options = defaults) {
    const o = { ...defaults, ...options };
    validateInput(hits, o);
    // C# TileHit has a default HoldSeconds of zero and Taps of one; accept the same omissions.
    const tiles = hits.map(hit => ({ tileIndex: hit.tileIndex, time: hit.time, holdSeconds: hit.holdSeconds ?? 0, angle: hit.angle ?? NaN, taps: hit.taps ?? 1 }));
    if (o.Keys > 4) return PlanManyKeys(tiles, o, layoutOf(o.Keys));
    const moments = Moments(tiles, o);
    const reviewPreference = o.Style === 'ReviewPreferenceV1' && !tiles.some(hit => hit.holdSeconds > 0);
    const notes = [], rolled = [], hands = new HandState(), focus = FocusChords(moments, o, reviewPreference);
    const preferredChordHand = o.ChordsOnOtherHand ? 0 : 1;
    const yieldIndex = indexLane(1 - preferredChordHand), yieldOuter = partner(yieldIndex);
    for (let i = 0; i < moments.length; i++) {
      const moment = moments[i], time = moment[0].time, last = notes.length ? notes[notes.length - 1] : null;
      if (moment.length > 1) {
        const chordHand = ChordHand(preferredChordHand, time, last, o);
        notes.push(BuildChord(moment, chordHand, last, o));
        rolled.push(false);
      } else {
        let [lane, roll] = hands.Next(time, o);
        if (focus[i] >= 0) {
          const chordHand = focus[i] < i ? pairHand(notes[focus[i]]) : preferredChordHand;
          [lane, roll] = [FocusLane(1 - chordHand, time, last, o), false];
        }
        if (lane === yieldIndex && i + 1 < moments.length && focus[i + 1] === i + 2
          && moments[i + 1][0].time - time < o.JackWindow - Epsilon && !Blocked(yieldOuter, time, last, o)) {
          [lane, roll] = [yieldOuter, false];
        }
        // A hold still pressed at this moment (adjacent hold tiles) is taken over by the other hand.
        const holding = HoldingHand(last, time, fourKeys);
        if (holding >= 0 && handOf(lane) === holding) [lane, roll] = [FocusLane(1 - holding, time, last, o), false];
        if (Blocked(lane, time, last, o)) [lane, roll] = [NearestFreeLane(lane, time, o, last, fourKeys), false];
        notes.push({ tileIndex: moment[0].tileIndex, lastTileIndex: moment[0].tileIndex, time,
          lanes: [{ lane, holdSeconds: moment[0].holdSeconds }] });
        rolled.push(roll);
      }
      hands.Pressed(notes[notes.length - 1]);
    }
    if (o.UseRoll && o.OuterRoll) OuterFirst(notes, rolled, o);
    if (reviewPreference) ReviewPhrases(notes, o);
    return o.LeftHanded ? notes.map(note => Mirror(note, fourKeys)) : notes;
  }

  function Mirror(note, layout) {
    return { tileIndex: note.tileIndex, lastTileIndex: note.lastTileIndex, time: note.time,
      lanes: sorted(note.lanes.map(item => ({ lane: layout.mirror(item.lane), holdSeconds: item.holdSeconds }))) };
  }

  // 6K / 8K (C# NotePlanner.ManyKeys.cs PlanManyKeys): a planner of its own, 4K never comes here. Slow notes on the right index,
  // medium ones alternate the index fingers, fast runs (gaps within R/2) roll outer to inner on one hand and only move to the other
  // hand once every finger of the first has played, chords stay on one hand while it has fingers enough.
  function PlanManyKeys(tiles, o, layout) {
    const reviewPreference = o.Style === 'ReviewPreferenceV1' && !tiles.some(hit => hit.holdSeconds > 0);
    const moments = Moments(tiles, o), focus = FocusChords(moments, o, reviewPreference), runEnd = FastRuns(moments, focus, o);
    const preferred = o.ChordsOnOtherHand ? 0 : 1;
    const yieldIndex = layout.lane(1 - preferred, 0), yieldMiddle = layout.lane(1 - preferred, 1);
    const notes = [], hands = [];
    for (let i = 0; i < moments.length; i++) {
      const moment = moments[i], time = moment[0].time, last = notes.length ? notes[notes.length - 1] : null;
      const lastHand = hands.length ? hands[hands.length - 1] : 1;
      const recent = last !== null && time - last.time <= o.ResetWindow + Epsilon;
      if (moment.length > 1) {
        const presses = ChordPresses(moment, layout), hand = ManyChordHand(preferred, presses.length, time, last, layout, o);
        notes.push(BuildManyChord(moment, presses, hand, last, layout, o));
        hands.push(hand);
        continue;
      }
      if (runEnd[i] > i) {
        const run = RunLanes(moments, i, runEnd[i], recent ? 1 - lastHand : 1, notes, focus, preferred, layout, o, reviewPreference);
        run.forEach((lane, k) => {
          const tile = moments[i + k][0];
          notes.push({ tileIndex: tile.tileIndex, lastTileIndex: tile.tileIndex, time: tile.time, lanes: [{ lane, holdSeconds: tile.holdSeconds }] });
          hands.push(layout.handOf(lane));
        });
        i = runEnd[i];
        continue;
      }
      let lane;
      if (focus[i] >= 0) {
        const chordHand = focus[i] < i ? hands[focus[i]] : preferred;
        lane = IndexOrMiddle(1 - chordHand, time, last, layout, o);
        if (focus[i] > i && Blocked(lane, time, last, o) && !Blocked(layout.lane(chordHand, 0), time, last, o)
          && moments[i + 1][0].time - time < o.JackWindow - Epsilon) lane = layout.lane(chordHand, 0);
      } else lane = layout.lane(recent ? 1 - lastHand : 1, 0);
      if (lane === yieldIndex && i + 1 < moments.length && focus[i + 1] === i + 2
        && moments[i + 1][0].time - time < o.JackWindow - Epsilon && !Blocked(yieldMiddle, time, last, o)) lane = yieldMiddle;
      const holding = HoldingHand(last, time, layout);
      if (holding >= 0 && layout.handOf(lane) === holding) lane = IndexOrMiddle(1 - holding, time, last, layout, o);
      if (Blocked(lane, time, last, o)) lane = NearestFreeLane(lane, time, o, last, layout);
      notes.push({ tileIndex: moment[0].tileIndex, lastTileIndex: moment[0].tileIndex, time, lanes: [{ lane, holdSeconds: moment[0].holdSeconds }] });
      hands.push(layout.handOf(lane));
    }
    RestFingers(notes, layout, o);
    if (o.UseRoll && o.OuterRoll) OuterFirstMany(notes, layout, o);
    return o.LeftHanded ? notes.map(note => Mirror(note, layout)) : notes;
  }
  // C# FastRuns: for each run start, the last note of its run (gaps within R/2, no yielding notes, no note under a held key).
  function FastRuns(moments, focus, o) {
    const n = moments.length, end = new Array(n).fill(0);
    const eligible = i => moments[i].length === 1 && focus[i] < 0
      && (i === 0 || !moments[i - 1].some(t => t.holdSeconds > 0 && moments[i - 1][0].time + t.holdSeconds > moments[i][0].time - Epsilon));
    for (let start = 0; start < n;) {
      end[start] = start;
      if (!eligible(start)) { start++; continue; }
      let last = start;
      while (last + 1 < n && eligible(last + 1) && moments[last + 1][0].time - moments[last][0].time <= o.ResetWindow / 2 + Epsilon) { last++; end[last] = last; }
      end[start] = last;
      start = last + 1;
    }
    return end;
  }
  const distance = (a, b) => Math.min(...a.lanes.flatMap(x => b.lanes.map(y => Math.abs(x.lane - y.lane))));
  // C# RunLanes: groups are one whole hand (2026-11-25: fewer hand switches, roll on one side). Candidates are both start hands (and, when
  // the last group is one note, that note on the middle finger); a start jacking the previous note is out, the rest are scored
  // 20 per press whose finger has not rested R/2 (not when only index fingers are used), end-on-yield-index 12, start inner-to-outer 10,
  // previous note left lone 5, middle finger 3, not the natural start hand 1. When every one-hand candidate tires a finger, the old
  // half-and-half split is tried too and kept when it scores no worse.
  function RunLanes(moments, start, end, startHand, notes, focus, preferred, layout, o, reviewPreference) {
    const n = end - start + 1;
    const indexOnly = !o.UseRoll || (reviewPreference && ReviewShortRun(moments, start, end, o));
    const assign = (hand, group) => Array.from({ length: n }, (_, k) => {
      const g = Math.floor(k / group), size = Math.min(group, n - g * group), pos = k % group;
      return layout.lane(g % 2 === 0 ? hand : 1 - hand, o.OuterRoll ? size - 1 - pos : pos);
    });
    const time = moments[start][0].time, endTime = moments[end][0].time;
    const last = notes.length ? notes[notes.length - 1] : null, beforeLast = notes.length > 1 ? notes[notes.length - 2] : null;
    const endFits = lanes => end + 1 >= moments.length || focus[end + 1] !== end + 2 || lanes[n - 1] !== layout.lane(1 - preferred, 0)
      || moments[end + 1][0].time - endTime >= o.JackWindow - Epsilon;
    const innerToOuter = lane => o.OuterRoll && last !== null && !isChord(last) && time - last.time <= o.ResetWindow + Epsilon
      && layout.handOf(firstLane(last)) === layout.handOf(lane) && layout.finger(lane) > layout.finger(firstLane(last));
    const isolatesLast = lane => last !== null && beforeLast !== null && time - last.time <= LinkGap && last.time - beforeLast.time <= LinkGap
      && distance(beforeLast, last) >= 2 && Math.min(...last.lanes.map(item => Math.abs(item.lane - lane))) >= 2;
    const released = (lanes, k) => {
      for (let j = k - 1; j >= 0; j--) if (lanes[j] === lanes[k]) return moments[start + j][0].time;
      for (let j = notes.length - 1; j >= 0 && time - notes[j].time <= o.ResetWindow + Epsilon; j--) {
        const item = notes[j].lanes.find(x => x.lane === lanes[k]);
        if (item) return notes[j].time + item.holdSeconds;
      }
      return -Infinity;
    };
    const tiredCount = lanes => lanes.reduce((count, _, k) => count + (moments[start + k][0].time - released(lanes, k) < o.ResetWindow / 2 - Epsilon ? 1 : 0), 0);
    const best = group => {
      let result = { lanes: null, penalty: Infinity, tired: 0 };
      for (const hand of [startHand, 1 - startHand]) {
        const plain = assign(hand, group), lastAlone = n > 1 && (n - 1) % group === 0;
        for (const middle of lastAlone ? [false, true] : [false]) {
          const lanes = plain.slice();
          if (middle) lanes[n - 1] = layout.lane(layout.handOf(lanes[n - 1]), 1);
          if (Blocked(lanes[0], time, last, o)) continue;
          const tired = indexOnly ? 0 : tiredCount(lanes);
          const penalty = 20 * tired + (endFits(lanes) ? 0 : 12) + (innerToOuter(lanes[0]) ? 10 : 0) + (isolatesLast(lanes[0]) ? 5 : 0)
            + (middle ? 3 : 0) + (hand === startHand ? 0 : 1);
          if (penalty < result.penalty) result = { lanes, penalty, tired };
        }
      }
      return result;
    };
    const full = indexOnly ? 1 : layout.per;
    let chosen = best(full);
    const split = Math.min(Math.max(Math.floor((n + 1) / 2), 1), layout.per);
    if (!indexOnly && split !== full && (chosen.lanes === null || chosen.tired > 0)) {
      const halves = best(split);
      if (halves.lanes !== null && halves.penalty <= chosen.penalty) chosen = halves;
    }
    let lanes = chosen.lanes;
    if (lanes === null) { lanes = assign(startHand, full); lanes[0] = NearestFreeLane(lanes[0], time, o, last, layout); }
    return lanes;
  }
  // C# NotePlanner.Rest.cs RestFingers: a second pass over the 6K / 8K plan so every finger rests. A lane costs by how long since it was
  // released (under R/2 a lot, R/2..R a little, over R nothing) plus how far it is from the planned lane; a move may not leave a lone note.
  const TiredCost = 30, SoreCost = 6, SameHandCost = 4, OtherHandCost = 10, FingerStepCost = 3, InnerToOuterCost = 15;
  const JumpCost = 4, LoneCost = 80, HeldHandCost = 50, BusyHandCost = 8, ChordShiftCost = 5;
  function RestFingers(notes, layout, o) {
    const free = new Array(layout.count).fill(-Infinity);
    const lastSingle = [{ time: -Infinity, finger: 0 }, { time: -Infinity, finger: 0 }];
    let prev = null;
    for (let i = 0; i < notes.length; i++) {
      let note = notes[i];
      const next = i + 1 < notes.length ? notes[i + 1] : null;
      if (!isChord(note)) note = RestSingle(note, prev, next, PrevFar(notes, i), free, lastSingle, layout, o);
      else if (note.lanes.length <= layout.per && new Set(note.lanes.map(item => layout.handOf(item.lane))).size === 1)
        note = RestChord(note, prev, next, PrevFar(notes, i), free, layout, o);
      notes[i] = note;
      for (const item of note.lanes) free[item.lane] = note.time + item.holdSeconds;
      if (!isChord(note)) lastSingle[layout.handOf(firstLane(note))] = { time: note.time, finger: layout.finger(firstLane(note)) };
      prev = note;
    }
  }
  // C# OuterFirstMany: two inner-to-outer presses of one hand within R swap places (outer first), unless that jacks, leaves a finger
  // under R/2 of rest, makes a new inner-to-outer pair with a neighbour or adds a lone note nearby.
  function OuterFirstMany(notes, layout, o) {
    const free = new Array(layout.count).fill(-Infinity);
    const single = k => k >= 0 && k < notes.length && !isChord(notes[k]);
    const laneOf = k => firstLane(notes[k]);
    const lone = k => k >= 1 && k + 1 < notes.length && notes[k].time - notes[k - 1].time <= LinkGap && notes[k + 1].time - notes[k].time <= LinkGap
      && distance(notes[k], notes[k - 1]) >= 2 && distance(notes[k], notes[k + 1]) >= 2;
    const loneAround = a => [a - 1, a, a + 1, a + 2].filter(lone).length;
    const innerToOuter = (from, to) => single(from) && single(to) && Math.abs(notes[to].time - notes[from].time) <= o.ResetWindow + Epsilon
      && layout.handOf(laneOf(from)) === layout.handOf(laneOf(to)) && layout.finger(laneOf(to)) > layout.finger(laneOf(from));
    for (let i = 1; i < notes.length; i++) {
      const a = i - 1, b = i;
      if (innerToOuter(a, b)) {
        const inner = laneOf(a), outer = laneOf(b), ta = notes[a].time, tb = notes[b].time;
        const rested = ta - free[outer] >= o.ResetWindow / 2 - Epsilon && tb - free[inner] >= o.ResetWindow / 2 - Epsilon;
        const noJack = (a === 0 || !Jacks(notes[a - 1], outer, ta, o)) && (b + 1 >= notes.length || !Jacks(notes[b + 1], inner, tb, o));
        const oldA = notes[a], oldB = notes[b], before = loneAround(a);
        notes[a] = WithLane(oldA, outer);
        notes[b] = WithLane(oldB, inner);
        const clean = !innerToOuter(a - 1, a) && !innerToOuter(b, b + 1) && loneAround(a) <= before;
        if (!(rested && noJack && clean)) { notes[a] = oldA; notes[b] = oldB; }
      }
      for (const item of notes[i - 1].lanes) free[item.lane] = notes[i - 1].time + item.holdSeconds;
    }
  }
  function PrevFar(notes, i) {
    return i >= 2 && notes[i - 1].time - notes[i - 2].time <= LinkGap && distance(notes[i - 2], notes[i - 1]) >= 2;
  }
  function SpreadCost(lanes, time, prev, next, prevFar) {
    const far = other => other !== null && Math.abs(other.time - time) <= LinkGap
      && Math.min(...other.lanes.map(item => Math.min(...lanes.map(c => Math.abs(item.lane - c))))) >= 2;
    const before = far(prev), after = far(next);
    return (before && after ? LoneCost : before || after ? JumpCost : 0) + (before && prevFar ? LoneCost : 0);
  }
  function RestCost(rest, o) {
    const half = o.ResetWindow / 2;
    return rest < half - Epsilon ? TiredCost + TiredCost * (half - rest) / half
      : rest < o.ResetWindow - Epsilon ? SoreCost * (o.ResetWindow - rest) / half
      : 0;
  }
  function Usable(lane, time, prev, free, o) { return !Blocked(lane, time, prev, o) && time >= free[lane] - Epsilon; }
  function RestSingle(note, prev, next, prevFar, free, lastSingle, layout, o) {
    const time = note.time, proposal = firstLane(note), hand = layout.handOf(proposal), finger = layout.finger(proposal);
    const holding = HoldingHand(prev, time, layout);
    const stuck = !Usable(proposal, time, prev, free, o);
    const cost = c => {
      if (!Usable(c, time, prev, free, o)) return Infinity;
      const spread = SpreadCost([c], time, prev, next, prevFar);
      if (c === proposal) return RestCost(time - free[c], o) + spread;
      if (!stuck && (spread >= LoneCost || (next !== null && Jacks(next, c, time, o)))) return Infinity;
      const h = layout.handOf(c), f = layout.finger(c);
      let total = RestCost(time - free[c], o) + spread + (h === hand ? SameHandCost : OtherHandCost) + FingerStepCost * Math.abs(f - finger);
      if (h === holding) total += HeldHandCost;
      const last = lastSingle[h];
      if (o.OuterRoll && time - last.time <= o.ResetWindow + Epsilon && f > last.finger) total += InnerToOuterCost;
      if (o.OuterRoll && next !== null && !isChord(next) && next.time - time <= o.ResetWindow + Epsilon
        && layout.handOf(firstLane(next)) === h && layout.finger(firstLane(next)) > f) total += InnerToOuterCost;
      return total;
    };
    let best = proposal, bestCost = cost(proposal);
    if (bestCost <= 0) return note;
    for (let c = 0; c < layout.count; c++) {
      if (!o.UseRoll && layout.finger(c) !== 0) continue;
      const value = cost(c);
      if (value < bestCost - Epsilon) { best = c; bestCost = value; }
    }
    return best === proposal ? note : WithLane(note, best);
  }
  function RestChord(note, prev, next, prevFar, free, layout, o) {
    const time = note.time, size = note.lanes.length, hand = layout.handOf(firstLane(note));
    const start = Math.min(...note.lanes.map(item => layout.finger(item.lane)));
    const holding = HoldingHand(prev, time, layout);
    const stuck = note.lanes.some(item => !Usable(item.lane, time, prev, free, o));
    const near = (other, h) => other !== null && !isChord(other) && Math.abs(other.time - time) < o.JackWindow - Epsilon && layout.handOf(firstLane(other)) === h;
    const cost = (h, s) => {
      let total = 0;
      const lanes = [];
      for (let f = s; f < s + size; f++) {
        const lane = layout.lane(h, f);
        if (!Usable(lane, time, prev, free, o)) return Infinity;
        total += RestCost(time - free[lane], o);
        lanes.push(lane);
      }
      total += ChordShiftCost * s;
      if (h !== hand) total += OtherHandCost;
      if (h === holding) total += HeldHandCost;
      if (near(prev, h) || near(next, h)) total += BusyHandCost;
      const spread = SpreadCost(lanes, time, prev, next, prevFar);
      const moved = h !== hand || s !== start, takesNext = next !== null && lanes.some(lane => Jacks(next, lane, time, o));
      return moved && !stuck && (spread >= LoneCost || takesNext) ? Infinity : total + spread;
    };
    let bestHand = hand, bestStart = start, bestCost = cost(hand, start);
    if (bestCost <= ChordShiftCost * start + Epsilon) return note;
    for (const h of [hand, 1 - hand]) {
      for (let s = 0; s + size <= layout.per; s++) {
        const value = cost(h, s);
        if (value < bestCost - Epsilon) { bestHand = h; bestStart = s; bestCost = value; }
      }
    }
    if (bestHand === hand && bestStart === start) return note;
    const holds = note.lanes.slice().sort((a, b) => layout.finger(a.lane) - layout.finger(b.lane)).map(item => item.holdSeconds);
    return { tileIndex: note.tileIndex, lastTileIndex: note.lastTileIndex, time: note.time,
      lanes: sorted(holds.map((hold, k) => ({ lane: layout.lane(bestHand, bestStart + k), holdSeconds: hold }))) };
  }

  function ReviewShortRun(moments, start, end, o) {
    const n = end - start + 1;
    if (n < o.ReviewShortMinNotes || n > o.ReviewShortMaxNotes) return false;
    const firstGap = moments[start + 1][0].time - moments[start][0].time;
    for (let i = start + 1; i <= end; i++) {
      const gap = moments[i][0].time - moments[i - 1][0].time;
      if (gap < o.ReviewShortMinGap - Epsilon || gap > Math.min(o.ReviewShortMaxGap, o.ResetWindow / 2) + Epsilon
        || Math.abs(gap - firstGap) > o.ReviewTimingTolerance + Epsilon) return false;
    }
    return true;
  }
  // C# ChordPresses: one multitap tile needing more than four presses gets min(taps, keys).
  function ChordPresses(moment, layout) {
    const presses = moment.map(t => t.holdSeconds), tile = moment[0];
    if (moment.length === 4 && moment.every(t => t.tileIndex === tile.tileIndex))
      while (presses.length < Math.min(Math.max(1, tile.taps), layout.count)) presses.push(tile.holdSeconds);
    return presses;
  }
  function ManyChordHand(preferred, presses, time, last, layout, o) {
    const fingers = Math.min(presses, layout.per);
    const free = hand => { for (let f = 0; f < fingers; f++) if (Blocked(layout.lane(hand, f), time, last, o)) return false; return true; };
    return !free(preferred) && free(1 - preferred) ? 1 - preferred : preferred;
  }
  function BuildManyChord(moment, presses, hand, last, layout, o) {
    const first = moment[0];
    const fingers = h => {
      const lanes = Array.from({ length: layout.per }, (_, f) => layout.lane(h, f));
      return lanes.filter(l => !Blocked(l, first.time, last, o)).concat(lanes.filter(l => Blocked(l, first.time, last, o)));
    };
    const main = Math.min(presses.length, layout.per);
    const order = fingers(hand).slice(0, main).concat(fingers(1 - hand).slice(0, presses.length - main));
    return { tileIndex: first.tileIndex, lastTileIndex: moment[moment.length - 1].tileIndex, time: first.time,
      lanes: sorted(presses.map((hold, k) => ({ lane: order[k], holdSeconds: hold }))) };
  }
  function IndexOrMiddle(hand, time, last, layout, o) {
    const index = layout.lane(hand, 0);
    return Blocked(index, time, last, o) ? layout.lane(hand, 1) : index;
  }

  class HandState {
    constructor() { this.last = -1; this.lastTime = 0; this.lastRight = -Infinity; }
    Next(time, o) {
      const r = o.ResetWindow, d = time - this.lastTime;
      switch (this.last) {
        case RightIndex:
          if (d > r + Epsilon) return [RightIndex, false];
          if (d > r / 2 + Epsilon || !o.UseRoll) return [LeftIndex, false];
          return [RightOuter, true];
        case RightOuter:
          return d > r + Epsilon ? [RightIndex, false] : [LeftIndex, false];
        case LeftIndex:
          if (o.UseRoll && (d <= r / 2 + Epsilon || time - this.lastRight <= r + Epsilon)) return [LeftOuter, true];
          return [RightIndex, false];
        default:
          return [RightIndex, false];
      }
    }
    Pressed(note) {
      const first = firstLane(note), lane = isChord(note) ? (pairHand(note) === 1 ? RightOuter : LeftOuter) : first;
      this.last = lane; this.lastTime = note.time;
      if (note.lanes.some(item => handOf(item.lane) === 1)) this.lastRight = note.time;
    }
  }

  function FocusChords(moments, o, reviewPreference) {
    const focus = new Array(moments.length).fill(-1);
    const focusWindow = reviewPreference ? Math.min(o.ChordFocusWindow, o.ResetWindow) : o.ChordFocusWindow;
    for (let i = 0; i < moments.length; i++) {
      if (moments[i].length > 1) continue;
      const time = moments[i][0].time;
      const before = i > 0 && moments[i - 1].length > 1 ? time - moments[i - 1][0].time : Infinity;
      const after = i + 1 < moments.length && moments[i + 1].length > 1 ? moments[i + 1][0].time - time : Infinity;
      if (after <= focusWindow + Epsilon && after <= before + Epsilon) focus[i] = i + 1;
      else if (before <= focusWindow + Epsilon) focus[i] = i - 1;
    }
    return focus;
  }

  function FocusLane(hand, time, last, o) {
    const index = indexLane(hand);
    return Blocked(index, time, last, o) ? partner(index) : index;
  }

  function Moments(tiles, o) {
    const moments = [];
    for (const tile of tiles) {
      const group = moments[moments.length - 1];
      const samePress = group && (Number.isNaN(tile.angle)
        ? tile.time - group[0].time < o.ChordWindow
        : tile.angle <= o.ChordAngle + AngleTolerance && tile.time - group[group.length - 1].time <= o.ChordMaxGap + Epsilon);
      if (samePress) group.push(tile);
      else moments.push([tile]);
    }
    // A multitap tile takes one slot per press (C# Expand); more than four presses split by tile.
    const presses = tile => Math.max(1, tile.taps);
    const expand = group => {
      const out = [];
      for (const tile of group) for (let k = 0; k < presses(tile) && out.length < 4; k++) out.push(tile);
      return out;
    };
    return moments.flatMap(group => group.reduce((sum, tile) => sum + presses(tile), 0) > 4 ? group.map(tile => expand([tile])) : [expand(group)]);
  }

  function ChordHand(preferred, time, last, o) {
    const handBlocked = hand => Blocked(hand * 2, time, last, o) || Blocked(hand * 2 + 1, time, last, o);
    return handBlocked(preferred) && !handBlocked(1 - preferred) ? 1 - preferred : preferred;
  }

  function BuildChord(moment, hand, last, o) {
    const first = moment[0], main = indexLane(hand), otherIndex = indexLane(1 - hand);
    const extra = Blocked(otherIndex, first.time, last, o) && !Blocked(partner(otherIndex), first.time, last, o) ? partner(otherIndex) : otherIndex;
    const order = [main, partner(main), extra, partner(extra)];
    return { tileIndex: first.tileIndex, lastTileIndex: moment[moment.length - 1].tileIndex, time: first.time,
      lanes: sorted(moment.map((hit, i) => ({ lane: order[i], holdSeconds: hit.holdSeconds }))) };
  }

  function ReviewPhrases(notes, o) {
    for (let start = 0; start < notes.length;) {
      if (isChord(notes[start])) { start++; continue; }
      let end = start;
      while (end + 1 < notes.length && !isChord(notes[end + 1]) && notes[end + 1].time - notes[end].time <= o.ResetWindow + Epsilon) end++;
      const length = end - start + 1;
      if (length >= 2) {
        let medium = true, shortRoll = o.UseRoll && o.OuterRoll && length >= o.ReviewShortMinNotes && length <= o.ReviewShortMaxNotes;
        const firstGap = notes[start + 1].time - notes[start].time;
        for (let i = start + 1; i <= end; i++) {
          const gap = notes[i].time - notes[i - 1].time;
          medium = medium && gap > o.ResetWindow / 2 + Epsilon;
          shortRoll = shortRoll && gap >= o.ReviewShortMinGap - Epsilon && gap <= Math.min(o.ReviewShortMaxGap, o.ResetWindow / 2) + Epsilon
            && Math.abs(gap - firstGap) <= o.ReviewTimingTolerance + Epsilon;
        }
        if (medium || shortRoll) {
          let preferred = RightIndex;
          if (start > 0 && isChord(notes[start - 1]) && notes[start].time - notes[start - 1].time <= Math.min(o.ChordFocusWindow, o.ResetWindow) + Epsilon)
            preferred = indexLane(1 - pairHand(notes[start - 1]));
          const first = ReviewPhaseFits(notes, start, end, preferred, o) ? preferred
            : ReviewPhaseFits(notes, start, end, OtherIndex(preferred), o) ? OtherIndex(preferred) : -1;
          if (first >= 0) {
            for (let i = start; i <= end; i++) {
              notes[i] = WithLane(notes[i], (i - start) % 2 === 0 ? first : OtherIndex(first));
            }
          }
        }
      }
      start = end + 1;
    }
  }
  const OtherIndex = lane => lane === RightIndex ? LeftIndex : RightIndex;
  function ReviewPhaseFits(notes, start, end, first, o) {
    const last = (end - start) % 2 === 0 ? first : OtherIndex(first);
    return (start === 0 || ReviewBoundaryFits(notes[start - 1], notes[start].time, first, true, o))
      && (end + 1 === notes.length || ReviewBoundaryFits(notes[end + 1], notes[end].time, last, false, o));
  }
  function ReviewBoundaryFits(neighbour, time, lane, before, o) {
    if (Jacks(neighbour, lane, time, o)) return false;
    if (!o.UseRoll || !o.OuterRoll || isChord(neighbour) || Math.abs(neighbour.time - time) > o.ResetWindow + Epsilon) return true;
    const from = before ? firstLane(neighbour) : lane, to = before ? lane : firstLane(neighbour);
    return !((from === LeftIndex && to === LeftOuter) || (from === RightIndex && to === RightOuter));
  }

  function OuterFirst(notes, rolled, o) {
    for (let i = 1; i < notes.length; i++) {
      const prev = notes[i - 1], cur = notes[i];
      if (!rolled[i] || isChord(prev) || cur.time - prev.time > o.ResetWindow + Epsilon) continue;
      const outer = firstLane(cur), index = firstLane(prev);
      if (partner(outer) !== index) continue;
      if ((i >= 2 && Jacks(notes[i - 2], outer, prev.time, o))
        || (i + 1 < notes.length && Jacks(notes[i + 1], index, cur.time, o))) {
        const other = indexLane(1 - handOf(outer)), next = i + 1 < notes.length ? notes[i + 1] : null;
        if (!Jacks(prev, other, cur.time, o)
          && !(next !== null && (Jacks(next, other, cur.time, o) || HandsOverTo(cur, next, handOf(other))))) notes[i] = WithLane(cur, other);
        continue;
      }
      notes[i - 1] = WithLane(prev, outer);
      notes[i] = WithLane(cur, index);
    }
  }

  function HandsOverTo(note, next, hand) {
    return note.lanes.some(item => StillHeld(note, item.holdSeconds, next.time)) && next.lanes.some(item => handOf(item.lane) === hand);
  }
  function Jacks(neighbour, lane, time, o) { return hasLane(neighbour, lane) && Math.abs(neighbour.time - time) < o.JackWindow - Epsilon; }
  function WithLane(note, lane) {
    return { tileIndex: note.tileIndex, lastTileIndex: note.lastTileIndex, time: note.time, lanes: [{ lane, holdSeconds: note.lanes[0].holdSeconds }] };
  }
  function Blocked(lane, time, last, o) {
    if (last === null) return false;
    const item = last.lanes.find(entry => entry.lane === lane);
    return item !== undefined && (time - last.time < o.JackWindow - Epsilon || StillHeld(last, item.holdSeconds, time));
  }
  function StillHeld(last, hold, time) { return hold > 0 && last.time + hold > time - Epsilon; }
  function HoldingHand(last, time, layout) {
    if (last === null) return -1;
    const hands = [...new Set(last.lanes.filter(item => StillHeld(last, item.holdSeconds, time)).map(item => layout.handOf(item.lane)))];
    return hands.length === 1 ? hands[0] : -1;
  }
  function NearestFreeLane(lane, time, o, last, layout) {
    let best = lane, bestScore = 2147483647;
    for (let c = 0; c < layout.count; c++) {
      if (Blocked(c, time, last, o)) continue;
      const score = Math.min(...last.lanes.map(item => Math.abs(item.lane - c))) * 10 + Math.abs(c - lane);
      if (score < bestScore) { best = c; bestScore = score; }
    }
    return best;
  }

  function validateInput(hits, options) {
    if (!Array.isArray(hits)) throw new Error('排键输入必须为击打数组');
    for (const field of ['ChordWindow', 'ChordAngle', 'ChordMaxGap', 'ResetWindow', 'ChordFocusWindow', 'JackWindow']) {
      if (typeof options[field] !== 'number' || !Number.isFinite(options[field]) || options[field] < 0) throw new Error('排键参数无效：' + field);
    }
    for (const field of ['UseRoll', 'OuterRoll', 'LeftHanded', 'ChordsOnOtherHand']) {
      if (typeof options[field] !== 'boolean') throw new Error('排键参数无效：' + field);
    }
    if (![4, 6, 8].includes(options.Keys)) throw new Error('排键参数无效：Keys');
    if (options.Style === 'ReviewPreferenceV1') {
      for (const field of ['ReviewShortMinGap', 'ReviewShortMaxGap', 'ReviewTimingTolerance'])
        if (typeof options[field] !== 'number' || !Number.isFinite(options[field]) || options[field] < 0) throw new Error('排键参数无效：' + field);
      if (!Number.isInteger(options.ReviewShortMinNotes) || !Number.isInteger(options.ReviewShortMaxNotes)
        || options.ReviewShortMinNotes < 2 || options.ReviewShortMaxNotes < options.ReviewShortMinNotes || options.ReviewShortMaxGap < options.ReviewShortMinGap)
        throw new Error('短串排键参数无效');
    }
    let previous = -Infinity;
    for (const hit of hits) {
      if (!hit || !Number.isInteger(hit.tileIndex) || hit.tileIndex < 0
        || typeof hit.time !== 'number' || !Number.isFinite(hit.time) || hit.time < previous
        || (hit.holdSeconds !== undefined && (typeof hit.holdSeconds !== 'number' || !Number.isFinite(hit.holdSeconds) || hit.holdSeconds < 0))
        || (hit.taps !== undefined && !Number.isInteger(hit.taps))
        || (hit.angle != null && (typeof hit.angle !== 'number' || (!Number.isNaN(hit.angle) && (!Number.isFinite(hit.angle) || hit.angle < 0))))) {
        throw new Error('击打必须按时间排序，并包含有效砖号、时间和长按时长');
      }
      previous = hit.time;
    }
  }

  return { defaults, plan, revision, algorithmFor, version: algorithmFor(defaults) };
});
