const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const vm = require('node:vm');
const { spawnSync } = require('node:child_process');
const P = require('./note-planner.js');
const root = path.resolve(__dirname, '../..');
const normalize = value => JSON.parse(JSON.stringify(value));
const comparable = notes => notes.map(({ tileIndex, lastTileIndex, time, lanes }) => ({ tileIndex, lastTileIndex, time, lanes }));
function run(command, args) {
  const result = spawnSync(command, args, { cwd: root, encoding: 'utf8', timeout: 180000, windowsHide: true });
  assert.equal(result.status, 0, `${command} failed. Install .NET 10 / Python or set DOTNET_HOST_PATH / PYTHON.\n${result.error || ''}\n${result.stdout}\n${result.stderr}`);
}
function layoutContext(html, planner) {
  // Extract whole top-level functions by the next top-level closing brace. Nested
  // braces remain indented in the source; this executes the actual wrapper, not a copy.
  const names = ['toMoments', 'plannerOptions', 'planMod'];
  const parts = names.map(name => {
    const match = html.match(new RegExp('function ' + name + '\\([^]*?^}', 'm'));
    assert.ok(match, name + ' must remain testable'); return match[0];
  });
  const params = html.match(/const P = \{[^]*?^};/m);
  assert.ok(params);
  const context = vm.createContext({ NotePlanner: planner });
  vm.runInContext(params[0] + '\nconst EPS=1e-6, ANGLE_TOL=.1;\n' + parts.join('\n') + '\nglobalThis.parameters=P;', context);
  return context;
}
const fields = { ChordWindow:'chordWindow', ChordAngle:'chordAngle', ChordMaxGap:'chordGap', ResetWindow:'reset',
  ChordFocusWindow:'focus', JackWindow:'jack', UseRoll:'roll', OuterRoll:'outer', LeftHanded:'left', ChordsOnOtherHand:'chordOther',
  Style:'style', ReviewShortMinGap:'reviewShortMinGap', ReviewShortMaxGap:'reviewShortMaxGap', ReviewShortMinNotes:'reviewShortMinNotes',
  ReviewShortMaxNotes:'reviewShortMaxNotes', ReviewTimingTolerance:'reviewTimingTolerance', Keys:'keys' };

test('current C# planner, browser planner and layout-lab agree per note across synthetic boundaries and seeded random charts', () => {
  const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'rhythmhint-parity-'));
  try {
    const output = path.join(temporary, 'fixtures.json');
    run(process.env.DOTNET_HOST_PATH || 'dotnet', ['run', path.join(root, 'tools/ExportPlannerParity.cs'), '--', output]);
    const cases = JSON.parse(fs.readFileSync(output, 'utf8'));
    const layout = layoutContext(fs.readFileSync(path.join(root, 'tools/layout-lab.html'), 'utf8'), P);
    let count = 0, three = 0, four = 0, wide = 0, outer = 0;
    for (const chart of cases) {
      assert.equal(chart.algorithm, P.algorithmFor(chart.options));
      const actual = P.plan(chart.sourceHits, chart.options);
      assert.deepEqual(actual, comparable(chart.notes), chart.name);
      for (const [key, value] of Object.entries(chart.options)) {
        assert.ok(fields[key], 'layout-lab must expose ' + key);
        layout.parameters[fields[key]] = value;
      }
      assert.deepEqual(normalize(layout.plannerOptions()), chart.options);
      const tiles = chart.sourceHits.map(h => ({ i: h.tileIndex, t: h.time, hold: h.holdSeconds, ang: h.angle ?? NaN, taps: h.taps }));
      assert.deepEqual(normalize(layout.planMod(layout.toMoments(tiles))), chart.notes.map(n => n.lanes.map(l => l.lane)), chart.name + ' layout-lab');
      count += chart.notes.length;
      three += chart.notes.filter(n => n.lanes.length === 3).length;
      four += chart.notes.filter(n => n.lanes.length === 4).length;
      // 6K / 8K：5–8 键的双押砖、用到 8K 最外侧那一轨（右手小指）的多指轮
      wide += chart.notes.filter(n => n.lanes.length > 4).length;
      outer += chart.notes.filter(n => n.lanes.length === 1 && n.lanes[0].lane === 7).length;
    }
    assert.ok(cases.length > 600 && count > 20000 && three > 0 && four > 0 && wide > 0 && outer > 0);
    // A portable output outside the repository must contain the same shared planner.
    const source = path.join(temporary, 'input.json'), portable = path.join(temporary, 'lab.html');
    fs.writeFileSync(source, JSON.stringify({ name: '</script> synthetic', tiles: [], beats: [] }));
    run(process.env.PYTHON || 'python', [path.join(root, 'tools/build-layout-lab.py'), portable, 'synthetic=' + source]);
    const packed = fs.readFileSync(portable, 'utf8');
    assert.ok(!packed.includes('src="planner-js/note-planner.js"'));
    const shared = packed.match(/<script>\s*(\/\* Shared browser port[^]*?)<\/script>/);
    assert.ok(shared, 'portable artifact must inline the shared planner');
    const bundled = vm.createContext({}); vm.runInContext(shared[1], bundled);
    assert.equal(bundled.NotePlanner.version, P.version);
    for (const chart of cases) assert.deepEqual(normalize(bundled.NotePlanner.plan(chart.sourceHits, chart.options)), comparable(chart.notes), chart.name + ' portable');
  } finally { fs.rmSync(temporary, { recursive: true, force: true }); }
});
