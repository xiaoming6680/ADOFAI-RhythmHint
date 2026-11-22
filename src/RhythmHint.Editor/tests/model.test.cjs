// 手序编辑器的数据层：读 Mod 给的会话、编辑、撤销、保存成 Mod 能读的手序文件。
// 运行：node --test src/RhythmHint.Editor/tests/
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const M = require('../model.js');

const fixtures = path.join(__dirname, 'fixtures');
const session = () => JSON.parse(fs.readFileSync(path.join(fixtures, 'session.json'), 'utf8'));
const lanesOf = n => n.lanes.map(l => l[0]);

test('reads the session the mod writes', () => {
  const doc = M.fromSession(session());
  assert.equal(doc.keys, 4);
  assert.deepEqual(doc.keyLabels, ['D', 'F', 'J', 'K']);
  assert.equal(doc.meta.song, 'Sample Song');
  assert.equal(doc.hits.length, 16);
  assert.ok(doc.notes.length > 0 && doc.notes.length < 16, 'chords group several tiles');
  assert.equal(doc.notes.at(-1).auto, true);
  assert.equal(doc.notes[0].auto, false);
  assert.ok(doc.refs.standard.size > 0 && doc.refs.alternate.size > 0);
  assert.equal(doc.beats.length, 5);
  assert.equal(doc.path.length, 16);
  assert.equal(doc.path[0].tile, 1);
  assert.equal(doc.fileName, 'Sample Artist - Sample Song.json');
  assert.ok(doc.notes.some(n => n.lanes.length === 3), 'the sample has a triple');
  assert.ok(doc.notes.some(n => n.lanes.some(l => l[1] > 0)), 'the sample has a hold');
});

test('a session carries the other key counts and opens any of them', () => {
  // 2026-11-22 用户：编辑器左边显示几 K，能切换。会话本身是游戏里显示的键数，variants 是同一关的其他键数
  const four = M.fromSession(session());
  assert.equal(four.keys, 4);
  assert.deepEqual(four.keyChoices, [4, 6]);
  const six = M.fromSession(session(), 6);
  assert.equal(six.keys, 6);
  assert.deepEqual(six.keyChoices, [4, 6]);
  assert.match(six.sessionId, /-6k$/);
  assert.equal(six.fileName, 'Sample Artist - Sample Song 6K.json');
  assert.deepEqual(six.keyLabels, ['S', 'D', 'F', 'J', 'K', 'L']);
  assert.equal(six.notes.length, four.notes.length); // 提示怎么合不随键数变
  assert.ok(six.refs.standard.size > 0);
  assert.equal(six.audio, four.audio === null ? null : six.audio); // 音乐、轨道几种键数共用
  assert.deepEqual(six.path, four.path);
  assert.equal(M.fromSession(session(), 8).keys, 4); // 会话里没有的键数：照旧打开游戏里显示的那种
  const old = session();
  delete old.variants; // 旧版 Mod 写的会话只有一种
  assert.deepEqual(M.fromSession(old).keyChoices, [4]);
});

test('keeps timing for a manually selected recording and defaults old sessions to zero calibration', () => {
  const input = session();
  input.audio = { file: '', offset: -1.5, calibration: -0.09 };
  assert.equal(M.fromSession(input).audio.offset, -1.5);
  assert.equal(M.fromSession(input).audio.calibration, -0.09);
  input.audio = { file: 'legacy.ogg', offset: -1.5 };
  assert.equal(M.fromSession(input).audio.calibration, 0);
});

test('edits keep note count, times and holds; invalid edits change nothing', () => {
  const doc = M.fromSession(session());
  const before = doc.notes.map(n => [n.tile, n.lastTile, n.time, n.lanes.length, n.lanes.reduce((s, l) => s + l[1], 0)]);
  const h = new M.History(doc);
  const single = doc.notes.find(n => n.lanes.length === 1);
  const chord = doc.notes.find(n => n.lanes.length === 2);

  assert.ok(h.apply(M.ops.toLane(doc, [single], 0)));
  assert.deepEqual(lanesOf(single), [0]);
  assert.ok(h.apply(M.ops.mirror(doc, [chord])));
  assert.equal(M.ops.shift(doc, [chord], 5), null, 'out of range');
  const [a] = lanesOf(chord);
  assert.equal(M.ops.moveKey(doc, chord, a, lanesOf(chord)[1]), null, 'target already used');
  const cross = M.ops.set(doc, chord, [1, 2]);
  assert.ok(cross && h.apply(cross), 'cross-hand chords are allowed in custom fingering');
  assert.deepEqual(lanesOf(chord), [1, 2]);
  assert.equal(M.ops.set(doc, chord, [1]), null, 'must keep the key count');

  assert.deepEqual(doc.notes.map(n => [n.tile, n.lastTile, n.time, n.lanes.length, n.lanes.reduce((s, l) => s + l[1], 0)]), before);
  assert.ok(h.dirty);
  h.undo(); h.undo(); h.undo();
  assert.ok(!h.dirty);
  assert.deepEqual(doc.notes.map(n => n.lanes), doc.notes.map(n => n.orig));
  h.redo();
  assert.deepEqual(lanesOf(single), [0]);
});

test('a different edit after saving and undoing still counts as unsaved', () => {
  // 复现（2026-11-19 评审）：只数改了几步时，改 A → 保存 → 撤销 → 改 B 回到保存时的步数，显示已保存，其实游戏里还是 A
  const doc = M.fromSession(session());
  const h = new M.History(doc);
  const [a, b] = doc.notes.filter(n => n.lanes.length === 1);
  const next = n => (lanesOf(n)[0] + 1) % doc.keys;

  assert.ok(h.apply(M.ops.toLane(doc, [a], next(a))));
  h.markSaved();
  assert.ok(!h.dirty);
  h.undo();
  assert.ok(h.dirty, 'undoing the saved edit');
  h.redo();
  assert.ok(!h.dirty, 'redo comes back to exactly what was saved');
  h.undo();
  assert.ok(h.apply(M.ops.toLane(doc, [b], next(b))));
  assert.ok(h.dirty, 'same number of steps, different edit');
});

test('references apply only where the grouping matches', () => {
  const doc = M.fromSession(session());
  const change = M.ops.reference(doc, doc.notes, 'alternate');
  assert.ok(change instanceof Map);
  for (const [id, lanes] of change) {
    const ref = M.refLanes(doc, doc.notes[id], 'alternate');
    assert.deepEqual(lanes.map(l => l[0]), ref.map(l => l[0]));
  }
});

test('saved file round-trips and matches the fixture the C# tests read', () => {
  const doc = M.fromSession(session());
  const h = new M.History(doc);
  h.apply(M.ops.toLane(doc, [doc.notes[0]], 0));
  h.apply(M.ops.mirror(doc, [doc.notes.find(n => n.lanes.length === 2)]));
  doc.meta.author = 'Fixture Author';
  const text = M.toFileText(doc, new Date('2026-11-18T00:00:00Z'));

  const back = M.parseFingering(JSON.parse(text));
  assert.equal(back.notes.length, doc.notes.length);
  assert.deepEqual(back.notes.map(lanesOf), doc.notes.map(lanesOf));
  assert.equal(back.meta.author, 'Fixture Author');

  const fixture = path.join(fixtures, 'saved.json');
  if (process.env.RHYTHMHINT_UPDATE_FIXTURES === '1') fs.writeFileSync(fixture, text + '\n');
  assert.equal(fs.readFileSync(fixture, 'utf8').replace(/\r\n/g, '\n').trimEnd(), text);
});

test('rejects files that are not fingering files', () => {
  assert.throws(() => M.fromFileText('{'), e => e.code === 'notJson');
  assert.throws(() => M.fromFileText('{"format":"something"}'), e => e.code === 'notFingering');
  assert.throws(() => M.fromFileText('{"format":"rhythmhint-fingering","schemaVersion":9,"hits":[[1,1,0,null]],"notes":[[1,1,[[2,0]]]]}'), e => e.code === 'tooNew');
  assert.throws(() => M.fromFileText('{"format":"rhythmhint-fingering","schemaVersion":1,"hits":[[1,1,0,null],[2,2,0,null]],"notes":[[1,1,[[2,0]]]]}'), e => e.code === 'badFile');
  assert.throws(() => M.fromFileText('{"format":"rhythmhint-fingering","schemaVersion":1,"hits":[[1,1,0,null]],"notes":[[1,1,[[7,0]]]]}'), e => e.code === 'badFile');
});

test('jacks and note values', () => {
  const doc = M.fromFileText(JSON.stringify({
    format: 'rhythmhint-fingering', schemaVersion: 1,
    hits: [[1, 1.0, 0, 90], [2, 1.125, 0, 45], [3, 1.5, 0, 90]],
    notes: [[1, 1, [[2, 0]]], [2, 2, [[2, 0]]], [3, 3, [[1, 0]]]],
  }));
  doc.tempo = [{ time: 0, bpm: 120 }];
  assert.deepEqual([...M.jacks(doc, 0.15)].sort(), [0, 1]);
  assert.equal(M.noteValue(doc, 1.125, 0.125), '1/16');
  assert.equal(M.noteValue(doc, 1.5, 0.25), '1/8');
  assert.equal(M.noteValue(doc, 1.5, 0.375), '3/16');
  assert.equal(M.noteValue(doc, 1.5, 0.5), '1/4');
  assert.equal(M.noteValue(doc, 1.5, 0.31), '');
});

test('note values follow the beat lines, not the level BPM', () => {
  // 2026-11-19：很多谱的关卡 BPM 是谱师为转角设的数（工坊 2802147583 是 1011、32352），按它标几分音符没有意义；
  // Mod 推算的节拍线是音乐的拍子，有节拍线就按相邻两条线之间的拍长算
  const doc = M.fromFileText(JSON.stringify({
    format: 'rhythmhint-fingering', schemaVersion: 1, hits: [[1, 1.0, 0, 90]], notes: [[1, 1, [[2, 0]]]],
  }));
  doc.tempo = [{ time: 0, bpm: 1011 }];
  doc.beats = [0, 0.5, 1, 1.5, 2, 2.5].map((time, i) => ({ time, measure: i % 4 === 0 }));
  assert.equal(M.noteValue(doc, 1.5, 0.25), '1/8');
  assert.equal(M.noteValue(doc, 1.2, 0.125), '1/16');
  assert.equal(M.noteValue(doc, 9, 0.5), '1/4', 'after the last line: the last beat length');
});

test('drafts restore only onto the same level', () => {
  const doc = M.fromSession(session());
  const h = new M.History(doc);
  h.apply(M.ops.toLane(doc, [doc.notes[0]], 0));
  const draft = JSON.parse(JSON.stringify(M.draftOf(doc, false, 1000)));
  assert.equal(draft.changed, 1);
  assert.equal(draft.saved, false);
  assert.equal(draft.time, 1000);

  const fresh = M.fromSession(session());
  const change = M.draftChange(fresh, draft);
  assert.equal(change.size, 1);
  assert.equal(M.draftChange(fresh, { ...draft, signature: 'other', session: 'other' }), null);
  const indexOnly = { ...draft, tiles: undefined, session: undefined };
  assert.equal(M.draftChange(fresh, { ...indexOnly, lanes: draft.lanes.slice(1) }), null, 'an old draft (by note number) for a different note count does not apply');
});

/** 把会话里一个跨两块砖的多押拆成两个单押（模拟 Mod 更新后多押分组规则变了）。 */
function splitFirstChord(s) {
  const notes = s.fingering.notes;
  const i = notes.findIndex(n => n[1] > n[0] && n[2].length === 2);
  const [first, last, lanes] = notes[i];
  notes.splice(i, 1, [first, first, [lanes[0]]], [last, last, [lanes[1]]]);
  if (Array.isArray(s.fromAuto)) s.fromAuto.splice(i, 1, s.fromAuto[i], s.fromAuto[i]);
  return { first, last };
}

test('a draft still restores when the game groups the notes differently', () => {
  // 复现（2026-11-19 评审）：草稿只按第几个音符记，Mod 更新后多押分组一变整份作废、不提示，改一下就被新草稿盖掉
  const doc = M.fromSession(session());
  const edited = doc.notes.filter(n => n.lanes.length === 1).at(-1);
  const target = (lanesOf(edited)[0] + 1) % doc.keys;
  new M.History(doc).apply(M.ops.toLane(doc, [edited], target));
  const draft = JSON.parse(JSON.stringify(M.draftOf(doc, false)));

  const regrouped = session();
  splitFirstChord(regrouped);
  const fresh = M.fromSession(regrouped);
  assert.equal(fresh.notes.length, doc.notes.length + 1);
  const change = M.draftChange(fresh, draft);
  assert.ok(change, 'restores by tile');
  assert.equal(change.missed, 0);
  assert.ok(new M.History(fresh).apply(change));
  assert.deepEqual(lanesOf(fresh.notes.find(n => n.tile === edited.tile)), [target]);
});

test('notes whose grouping changed are counted, the rest still restore', () => {
  const doc = M.fromSession(session());
  const chord = doc.notes.find(n => n.lastTile > n.tile && n.lanes.length === 2);
  const single = doc.notes.filter(n => n.lanes.length === 1).at(-1);
  const h = new M.History(doc);
  h.apply(M.ops.mirror(doc, [chord]));
  h.apply(M.ops.toLane(doc, [single], (lanesOf(single)[0] + 1) % doc.keys));
  const draft = JSON.parse(JSON.stringify(M.draftOf(doc, false)));

  const regrouped = session();
  splitFirstChord(regrouped);
  const change = M.draftChange(M.fromSession(regrouped), draft);
  assert.equal(change.missed, 1, 'the split chord');
  assert.equal(change.size, 1, 'the single note');
});

test('a draft made right-handed is mirrored onto a left-handed export', () => {
  // 草稿不记惯用手时，切成左撇子再打开会把右手的键位套上去，存出 leftHanded=true、键位却是右手的文件
  const doc = M.fromSession(session());
  const single = doc.notes.filter(n => n.lanes.length === 1).at(-1);
  new M.History(doc).apply(M.ops.toLane(doc, [single], 0));
  const draft = JSON.parse(JSON.stringify(M.draftOf(doc, false)));
  assert.equal(draft.leftHanded, false);

  const left = session();
  left.fingering.leftHanded = true;
  left.fingering.notes.forEach(n => n[2].forEach(l => { l[0] = 3 - l[0]; }));
  const fresh = M.fromSession(left);
  new M.History(fresh).apply(M.draftChange(fresh, draft));
  assert.deepEqual(fresh.notes.map(n => lanesOf(n)), doc.notes.map(n => lanesOf(n).map(l => 3 - l).sort((a, b) => a - b)));
});

test('a draft brings back the whole level even when the game exports it differently', () => {
  // 2026-11-18 用户：改到一半关掉网页，下次接着改。游戏重新导出时原来的排法可能变了（换了排法、用上了保存的文件），草稿照样还原成上次的样子
  const doc = M.fromSession(session());
  const target = (doc.notes[0].lanes[0][0] + 1) % doc.keys;
  new M.History(doc).apply(M.ops.toLane(doc, [doc.notes[0]], target));
  const draft = JSON.parse(JSON.stringify(M.draftOf(doc, true)));

  const changedExport = session();
  const single = changedExport.fingering.notes.findIndex((n, i) => i > 0 && n[2].length === 1);
  changedExport.fingering.notes[single][2][0][0] = (changedExport.fingering.notes[single][2][0][0] + 2) % 4;
  const fresh = M.fromSession(changedExport);
  const change = M.draftChange(fresh, draft);
  assert.equal(change.size, 2);
  assert.ok(new M.History(fresh).apply(change));
  assert.deepEqual(fresh.notes.map(lanesOf), doc.notes.map(lanesOf));
});

test('drafts from earlier test builds (only changed notes) still restore', () => {
  const doc = M.fromSession(session());
  const target = (doc.notes[0].lanes[0][0] + 1) % doc.keys;
  const change = M.draftChange(doc, { signature: doc.signature, lanes: { 0: [target] } });
  assert.equal(change.size, 1);
  assert.deepEqual(change.get(0).map(l => l[0]), [target]);
});

test('the session says which level it is in the history', () => {
  const doc = M.fromSession(session());
  assert.match(doc.sessionId, /^16-[0-9a-f]+$/);
  assert.equal(M.fromFileText(M.toFileText(doc)).sessionId, '', 'a fingering file opened on its own has no history entry');
});

test('reads the history list the mod writes and ignores anything odd', () => {
  const recent = JSON.parse(fs.readFileSync(path.join(fixtures, 'recent.json'), 'utf8'));
  const items = M.parseRecent(recent);
  assert.deepEqual(items.map(i => i.id), [recent.items[1].id, recent.items[0].id], 'newest first');
  assert.equal(M.plain(items[0].song), 'Second Song');
  assert.equal(items[1].audio, recent.items[0].audio);
  assert.equal(items[0].audio, '');

  const odd = { ...recent, items: [
    { ...recent.items[0], session: '../index.html' },
    { ...recent.items[0], audio: '../../x.ogg' },
    { ...recent.items[0], id: '../x' },
    recent.items[1],
  ] };
  assert.deepEqual(M.parseRecent(odd).map(i => i.id), [recent.items[1].id]);
  assert.deepEqual(M.parseRecent(undefined), []);
  assert.deepEqual(M.parseRecent({ format: 'other', items: [] }), []);
});

test('saving does not overwrite another chart\'s fingering with the same name', async () => {
  // 复现（2026-11-19 评审）：建议名只有「曲师 - 曲名」，同一首歌另一位谱师的手序会被直接覆盖。规则和 Mod 的 FingeringInbox.SaveName 一样
  const doc = M.fromSession(session());
  doc.usedFile = '';
  const mine = M.toFileText(doc);
  const other = mine.replace(/"charts":\[[^\]]*\]/, '"charts":["9-other"]');
  const folder = files => async name => (name in files ? files[name] : null);
  const base = doc.fileName, second = base.replace(/\.json$/, ' (2).json'), third = base.replace(/\.json$/, ' (3).json');

  assert.equal(await M.saveName(doc, folder({})), base);
  assert.equal(await M.saveName(doc, folder({ [base]: mine })), base, 'same level: overwrite');
  assert.equal(await M.saveName(doc, folder({ [base]: other })), second, 'another chart: keep it');
  assert.equal(await M.saveName(doc, folder({ [base]: '{ broken' })), second, 'unreadable: keep it');
  assert.equal(await M.saveName(doc, folder({ [base]: other, [second]: mine })), second, 'saved as (2) before: overwrite that one');
  assert.equal(await M.saveName(doc, folder({ [base]: other, [second]: other })), third);
  doc.usedFile = base;
  assert.equal(await M.saveName(doc, folder({ [base]: '{ broken' })), base, 'the file the game uses for this level');
  // 同一关、别的键数的手序也不覆盖（2026-11-22：4K、6K 各一份）
  doc.usedFile = '';
  const sixKeys = mine.replace(/"keys":4/, '"keys":6');
  assert.notEqual(sixKeys, mine);
  assert.equal(await M.saveName(doc, folder({ [base]: sixKeys })), second, 'same level, other key count: keep it');
});

test('file names are safe and readable', () => {
  assert.equal(M.suggestedFileName({ song: '<color=#f00>A/B</color>', artist: 'X:Y' }), 'X_Y - A_B.json');
  assert.equal(M.suggestedFileName({ song: '', artist: '' }), 'fingering.json');
});

test('the song script is only taken from the history folder', () => {
  const input = session();
  const scriptOf = script => { input.audio = { file: 'recent/a.ogg', script }; return M.fromSession(input).audio.script; };
  assert.equal(scriptOf('recent/a.ogg.js'), 'recent/a.ogg.js');
  assert.equal(scriptOf(undefined), ''); // 1.3.0 导出的历史：没有，用 <audio> 播
  assert.equal(scriptOf('recent/../../evil.js'), '');
  assert.equal(scriptOf('https://example.com/x.js'), '');
  assert.equal(scriptOf('recent/a.ogg'), '');
});

test('a game in a protected folder makes the editor save to the documents inbox', () => {
  const input = session();
  assert.equal(M.fromSession(input).saveStartIn, ''); // 游戏不在系统文件夹：直接存进游戏的手序文件夹
  input.saveStartIn = 'documents';
  input.saveFolder = 'C:\\Users\\A\\Documents\\RhythmHint\\Fingerings';
  const doc = M.fromSession(input);
  assert.equal(doc.saveStartIn, 'documents');
  assert.equal(doc.saveFolder, input.saveFolder);
  input.saveStartIn = 'C:\\Windows'; // 只认浏览器的固定名字
  assert.equal(M.fromSession(input).saveStartIn, '');
});

// ---------- 2026-11-19 评审后的修复 ----------

const FORMAT = 'rhythmhint-fingering';
/** 一个带长按的双押（D 长按、J 短按）加一个单押。 */
const chordFile = (extra = {}) => ({
  format: FORMAT, schemaVersion: 1, keys: 4,
  hits: [[1, 1, 0.5, 90], [2, 1, 0, 0], [3, 2, 0, 90]],
  notes: [[1, 2, [[0, 0.5], [2, 0]]], [3, 3, [[1, 0]]]],
  ...extra,
});
const chordDoc = extra => M.fromFileText(JSON.stringify(chordFile(extra)));
/** 第 3 个和最后一个音符是自动补齐的会话。 */
function autoSession() {
  const s = session();
  s.fromAuto = s.fromAuto.map((_, i) => (i === 3 || i === s.fromAuto.length - 1 ? 1 : 0));
  return s;
}

test('next to review goes on from the selected note and wraps around', () => {
  // 复现：原来从画面时间往后找，跳过去时画面停在音符前 0.6 秒，下一次找到的还是它，按 N 永远停在第一处
  const doc = M.fromSession(autoSession());
  const last = doc.notes.length - 1;
  const list = M.reviewList(doc, new Set());
  assert.deepEqual(list, [3, last]);
  assert.equal(M.nextReview(list, -1), 3);
  assert.equal(M.nextReview(list, 3), last);
  assert.equal(M.nextReview(list, last), 3, 'wraps around');
  assert.equal(M.nextReview([], 0), -1);
});

test('auto-filled notes no longer need review once changed or confirmed', () => {
  const s = autoSession();
  const doc = M.fromSession(s);
  const a = doc.notes[3], b = doc.notes.at(-1);
  assert.ok(a.auto && a.filled && b.auto);
  new M.History(doc).apply(M.ops.toLane(doc, [a], (lanesOf(a)[0] + 1) % doc.keys));
  M.markReview(doc);
  assert.equal(a.auto, false, 'changed');
  assert.equal(a.filled, true, 'still shows where it came from');
  assert.equal(M.confirm([b]), 1);
  assert.equal(M.confirm([b, doc.notes[0]]), 0, 'already confirmed; a note from the file needs no confirming');
  M.markReview(doc);
  assert.equal(b.auto, false, 'confirmed');
  assert.deepEqual(M.reviewList(doc, new Set()), []);
  assert.equal(M.stats(doc).auto, 2, 'the auto-filled count stays');

  // 确认过的进草稿（旧版编辑器不认这一项，照样能用）
  const draft = JSON.parse(JSON.stringify(M.draftOf(doc, false)));
  const fresh = M.fromSession(s);
  assert.deepEqual(M.draftConfirmed(fresh, draft), [b.id]);
  assert.deepEqual(M.draftConfirmed(fresh, { ...draft, confirmed: undefined }), [], 'drafts from 1.3.x');
});

test('the file name chosen in the editor comes back with the draft', () => {
  const doc = M.fromSession(session());
  assert.ok(M.untouched(doc));
  doc.fileName = 'My take.json';
  assert.ok(!M.untouched(doc), 'a renamed file still needs its draft');
  const draft = JSON.parse(JSON.stringify(M.draftOf(doc, true)));
  const fresh = M.fromSession(session());
  assert.equal(M.draftMeta(fresh, draft).fileName, 'My take.json');
  assert.equal(M.draftMeta(fresh, { ...draft, fileName: '../evil.json' }).fileName, fresh.fileName);
  assert.equal(M.draftMeta(fresh, { ...draft, fileName: 'x.txt' }).fileName, fresh.fileName);
  assert.equal(M.draftMeta(fresh, { ...draft, fileName: undefined }).fileName, fresh.fileName, 'drafts from 1.3.x have no file name');
  assert.equal(M.draftMeta(fresh, { ...draft, author: 5 }).author, fresh.meta.author);
});

test('holds follow their key', () => {
  // 复现：多押按轨道排好后重新分长按，镜像 [[0,.5],[2,0]] 得 [[1,.5],[3,0]]，应是 K 长按（同 Mod 的镜像）
  const holdsOf = n => n.lanes.map(l => l.slice());
  let doc = chordDoc();
  const chord = doc.notes[0];
  new M.History(doc).apply(M.ops.mirror(doc, [chord]));
  assert.deepEqual(holdsOf(chord), [[1, 0], [3, 0.5]], 'mirror');

  doc = chordDoc();
  new M.History(doc).apply(M.ops.moveKey(doc, doc.notes[0], 0, 3));
  assert.deepEqual(holdsOf(doc.notes[0]), [[2, 0], [3, 0.5]], 'move one key');

  doc = chordDoc();
  new M.History(doc).apply(M.ops.set(doc, doc.notes[0], [2, 3]));
  assert.deepEqual(holdsOf(doc.notes[0]), [[2, 0], [3, 0.5]], 'the kept key keeps its hold');

  doc = chordDoc({ notes: [[1, 2, [[0, 0.5], [3, 0]]], [3, 3, [[1, 0]]]] });
  const change = M.ops.mirror(doc, [doc.notes[0]]);
  assert.equal(change.size, 1, 'D+K mirrored moves the hold from D to K');
  new M.History(doc).apply(change);
  assert.deepEqual(holdsOf(doc.notes[0]), [[0, 0], [3, 0.5]]);
  assert.ok(M.isChanged(doc.notes[0]));

  doc = chordDoc();
  doc.refs.standard = new Map([[1, { lastTile: 2, lanes: [[1, 0], [3, 0.5]] }]]);
  new M.History(doc).apply(M.ops.reference(doc, doc.notes, 'standard'));
  assert.deepEqual(holdsOf(doc.notes[0]), [[1, 0], [3, 0.5]], 'a reference keeps its own hold on its own key');
});

test('a draft brings holds back on the same key', () => {
  const doc = chordDoc();
  new M.History(doc).apply(M.ops.mirror(doc, [doc.notes[0]])); // F + K 长按
  const draft = JSON.parse(JSON.stringify(M.draftOf(doc, false)));
  const fresh = chordDoc();
  new M.History(fresh).apply(M.draftChange(fresh, draft));
  assert.deepEqual(fresh.notes[0].lanes, [[1, 0], [3, 0.5]]);

  // 换成左撇子：Mod 镜像时长按跟着键（D 长按变 K 长按），草稿也镜像过去：F + K 长按 → D 长按 + J
  const left = chordDoc({ leftHanded: true, notes: [[1, 2, [[1, 0], [3, 0.5]]], [3, 3, [[2, 0]]]] });
  new M.History(left).apply(M.draftChange(left, draft));
  assert.deepEqual(left.notes[0].lanes, [[0, 0.5], [2, 0]]);
});

test('reading a file rejects what the mod rejects and accepts what it accepts', () => {
  const bad = (obj, detail) => assert.throws(() => M.parseFingering(obj), e => e.code === 'badFile' && e.detail === detail, detail);
  bad(chordFile({ hits: [[3000000000, 1, 0, null]], notes: [[3000000000, 3000000000, [[0, 0]]]] }), 'hit');
  bad(chordFile({ song: 5 }), 'song');
  bad(chordFile({ author: { name: 'x' } }), 'author');
  bad(chordFile({ comment: ['a'] }), 'comment');
  const many = Array.from({ length: 200001 }, (_, i) => [i, i, 0, null]);
  bad(chordFile({ hits: many, notes: many.map(h => [h[0], h[0], [[0, 0]]]) }), 'hits');

  assert.equal(M.parseFingering(chordFile({ song: null, charter: undefined })).meta.song, '');
  assert.equal(M.parseFingering(chordFile({ author: '2026-11-19T08:00:00Z' })).meta.author, '2026-11-19T08:00:00Z');
  assert.equal(M.parseFingering(chordFile({ artist: 'x'.repeat(500) })).meta.artist.length, 300, 'names are cut like the mod does');
  // 整数写成 1.0 照样当整数
  const doc = M.fromFileText('{"format":"rhythmhint-fingering","schemaVersion":1.0,"keys":4.0,"hits":[[1.0,1,0,null],[2,2,0,null]],"notes":[[1.0,1.0,[[2.0,0]]],[2,2,[[1,0.0]]]]}');
  assert.deepEqual(doc.notes.map(lanesOf), [[2], [1]]);
});

test('fields this editor does not know survive opening and saving', () => {
  // Mod 把原文件里不认识的顶层字段原样放进会话的 fingering；编辑器原样读、原样写回（以后的版本加的字段不丢）
  const text = JSON.stringify(chordFile({ 'x-future': { a: [1, 2], b: null }, license: 'CC BY 4.0' }))
    .replace('"hits"', '"__proto__":{"p":1},"hits"');
  const back = JSON.parse(M.toFileText(M.fromFileText(text)));
  assert.deepEqual(back['x-future'], { a: [1, 2], b: null });
  assert.equal(back.license, 'CC BY 4.0');
  assert.deepEqual(Object.getOwnPropertyDescriptor(back, '__proto__').value, { p: 1 });
  // 原文件里的顺序是 __proto__、x-future、license
  assert.deepEqual(Object.keys(back).slice(-5), ['__proto__', 'x-future', 'license', 'hits', 'notes'], 'known fields first, unknown ones in their order before hits and notes');
  assert.deepEqual(Object.keys(back).slice(0, 3), ['format', 'schemaVersion', 'keys']);

  const s = session();
  s.fingering.license = 'CC0';
  assert.equal(JSON.parse(M.toFileText(M.fromSession(s))).license, 'CC0');
  assert.ok(!M.toFileText(M.fromSession(session())).includes('license'), 'nothing added when there is nothing to keep');
});

test('file names avoid control characters, reserved names and half emoji', () => {
  assert.equal(M.suggestedFileName({ song: 'a\u0007b\u007f', artist: '' }), 'ab.json');
  // 规则和 Mod 的 CustomFingering.SuggestedFileName 一样（保留名前面加 _，按 UTF-16 截到 80、不切开 emoji）
  assert.equal(M.suggestedFileName({ song: 'CON', artist: '' }), '_CON.json');
  assert.equal(M.cleanFileName('lpt9'), '_lpt9');
  assert.equal(M.cleanFileName('com1.backup'), '_com1.backup');
  assert.equal(M.cleanFileName('CONSOLE'), 'CONSOLE');
  assert.equal(M.cleanFileName(' song.. '), 'song');
  assert.equal(M.cleanFileName('\u0001'), '');
  const name = M.suggestedFileName({ song: 'a'.repeat(79) + '\u{1F600}\u{1F600}', artist: '' });
  assert.equal(name, 'a'.repeat(79) + '.json');
  assert.ok(!/[\uD800-\uDBFF](?![\uDC00-\uDFFF])/.test(name), 'no half emoji');
});

test('old drafts are dropped saved ones first, oldest first', () => {
  const list = [
    { key: 'a', saved: false, time: 1 }, { key: 'b', saved: true, time: 5 }, { key: 'c', saved: true, time: 2 },
    { key: 'd', saved: false, time: 9 }, { key: 'cur', saved: true, time: 0 },
  ];
  assert.deepEqual(M.draftsToDrop(list, 5, 'cur'), []);
  assert.deepEqual(M.draftsToDrop(list, 3, 'cur'), ['c', 'b']);
  assert.deepEqual(M.draftsToDrop(list, 2, 'cur'), ['c', 'b', 'a']);
  assert.deepEqual(M.draftsToDrop(list, 0, 'cur'), ['c', 'b', 'a', 'd'], 'never the one being edited');
});

test('key names from the game work in the editor', () => {
  // Mod 的键位是 Unity 的 KeyCode 名字（KeyBindings）：按浏览器的 KeyboardEvent.code 认
  assert.equal(M.keyCodeOf('Keypad4'), 'Numpad4');
  assert.equal(M.keyCodeOf('LeftShift'), 'ShiftLeft');
  assert.equal(M.keyCodeOf('Alpha1'), 'Digit1');
  assert.equal(M.keyCodeOf('KeypadPlus'), 'NumpadAdd');
  assert.equal(M.keyCodeOf('D'), '');
  const labels = ['Keypad4', 'LeftShift', 'J', 'Alpha1'];
  assert.equal(M.laneOfKey(labels, { key: '4', code: 'Numpad4' }), 0);
  assert.equal(M.laneOfKey(labels, { key: '4', code: 'Digit4' }), -1, 'the number row 4 is not Keypad4');
  assert.equal(M.laneOfKey(labels, { key: 'Shift', code: 'ShiftLeft' }), 1);
  assert.equal(M.laneOfKey(labels, { key: 'j', code: 'KeyJ' }), 2);
  assert.equal(M.laneOfKey(labels, { key: '1', code: 'Digit1' }), 3);
  assert.equal(M.laneOfKey(['D', 'F', 'J', 'K'], { key: 'k', code: 'KeyK' }), 3);
  assert.ok(M.isModifier('RightControl') && !M.isModifier('Keypad4') && !M.isModifier('K'));
  assert.equal(M.keyDisplay('Keypad4'), 'Num4');
  assert.equal(M.keyDisplay('LeftShift'), 'LShift');
  assert.equal(M.keyDisplay('UpArrow'), '\u2191');
  assert.equal(M.keyDisplay('None'), '');
  assert.equal(M.keyDisplay('d'), 'D');
});

test('lane keys that are also editor shortcuts are reported', () => {
  assert.deepEqual(M.shortcutClashes(['D', 'F', 'J', 'K']), []);
  assert.deepEqual(M.shortcutClashes(['Z', 'X', 'n', 'M']), ['N', 'M']);
  assert.deepEqual(M.shortcutClashes(['[', 'Space', 'LeftArrow', 'Home']), ['[', 'Space', '\u2190', 'Home']);
});

// ---------- 2026-11-19 \u7b2c\u4e8c\u8f6e\u8bc4\u5ba1\uff1a\u8349\u7a3f\u8ba4\u300c\u662f\u5728\u54ea\u4efd\u624b\u5e8f\u4e0a\u6539\u7684\u300d ----------

test('the fingerprint of a fingering changes with its keys, author, comment and file name, but not with handedness', () => {
  const base = s => M.fromSession(s).base;
  const s0 = session(), b0 = base(s0);
  assert.match(b0, /^b[0-9a-z]+$/);
  assert.equal(base(session()), b0, 'the same export gives the same fingerprint');
  const author = session(); author.fingering.author = 'Friend';
  const comment = session(); comment.fingering.comment = 'x';
  const name = session(); name.fileName = 'friend.json';
  const lanes = session(); lanes.fingering.notes[0][2] = [[0, 0]];
  for (const s of [author, comment, name, lanes]) assert.notEqual(base(s), b0);
  const left = session(); // \u6e38\u620f\u91cc\u6539\u6210\u5de6\u6487\u5b50\uff1a\u6574\u4f53\u955c\u50cf
  left.fingering.leftHanded = true;
  left.fingering.notes = left.fingering.notes.map(n => [n[0], n[1], n[2].map(([l, hold]) => [3 - l, hold])]);
  assert.equal(base(left), b0);
  // \u4fdd\u5b58\u65f6\u5148\u8bb0\u5185\u5bb9\u3001\u5199\u5b8c\u624d\u77e5\u9053\u5b58\u6210\u4ec0\u4e48\u540d\u5b57
  const doc = M.fromSession(session()), later = M.baseFor(doc);
  doc.notes[0].lanes = [[0, 0]];
  assert.equal(later(doc.fileName), b0, 'the content is taken when baseFor is called');
  assert.equal(M.fileDraftId(M.fromSession(name)), M.fileDraftId(M.fromSession(session())), 'a file draft does not depend on the file name');
});

test('a draft matches the fingering it was made on, or the one last saved to the game', () => {
  const doc = M.fromSession(session());
  new M.History(doc).apply(M.ops.toLane(doc, [doc.notes[0]], 0));
  const draft = JSON.parse(JSON.stringify(M.draftOf(doc, false)));
  assert.equal(draft.base, doc.base);
  assert.equal('savedBase' in draft, false, 'only written once saved');
  assert.ok(M.draftMatches(M.fromSession(session()), draft));
  const friend = session(); friend.fingering.author = 'Friend';
  assert.ok(!M.draftMatches(M.fromSession(friend), draft));
  doc.savedBase = M.fromSession(friend).base;
  assert.ok(M.draftMatches(M.fromSession(friend), JSON.parse(JSON.stringify(M.draftOf(doc, true)))));
});

test('a draft from an older editor matches when the notes it did not change are the same', () => {
  const doc = M.fromSession(session());
  new M.History(doc).apply(M.ops.toLane(doc, [doc.notes[0]], 0));
  const draft = JSON.parse(JSON.stringify(M.draftOf(doc, false)));
  delete draft.base;
  assert.ok(M.draftMatches(M.fromSession(session()), draft));
  const other = session(); other.fingering.notes[1][2] = [[3, 0]]; // \u6ca1\u6539\u8fc7\u7684\u97f3\u7b26\u4e0d\u4e00\u6837\u4e86
  assert.ok(!M.draftMatches(M.fromSession(other), draft));
  const changedOnly = session(); changedOnly.fingering.notes[0][2] = [[3, 0]]; // \u53ea\u6709\u6539\u8fc7\u7684\u90a3\u4e2a\u4e0d\u4e00\u6837\uff1a\u8ba4\u5f97
  assert.ok(M.draftMatches(M.fromSession(changedOnly), draft));
  const renamed = session(); renamed.fileName = 'friend.json';
  assert.ok(!M.draftMatches(M.fromSession(renamed), draft));
  assert.ok(!M.draftMatches(M.fromSession(session()), { ...draft, tiles: undefined }), 'one kept only by note number cannot be told');
});

test('undoing a restore leaves notes edited after it alone', () => {
  const doc = M.fromSession(session());
  const before = new Map([[0, doc.notes[0].lanes], [1, doc.notes[1].lanes]]);
  const restored = new Map([[0, [[0, 0]]], [1, [[3, 0]]]]);
  const h = new M.History(doc);
  h.apply(restored);
  h.apply(M.ops.toLane(doc, [doc.notes[1]], 2)); // \u6062\u590d\u540e\u53c8\u6539\u4e86\u7b2c 1 \u4e2a
  const back = M.undoRestore(doc, restored, before);
  assert.deepEqual([...back.keys()], [0]);
  assert.deepEqual(back.get(0), before.get(0));
});

test('saving marks the step that was saved, not the one reached meanwhile', () => {
  const doc = M.fromSession(session());
  const h = new M.History(doc);
  h.apply(M.ops.toLane(doc, [doc.notes[0]], 0));
  const step = h.current;
  h.apply(M.ops.toLane(doc, [doc.notes[1]], 3));
  h.markSaved(step);
  assert.equal(h.dirty, true);
  h.undo();
  assert.equal(h.dirty, false);
});

test('6K and 8K sessions get their own key names, history ids and file names', () => {
  // 2026-11-22：6K、8K 默认键位同游戏（S D F J K L、A S D F J K L ;）；历史、草稿按「谱面指纹-6k」分开，4K 的编号不变
  assert.deepEqual(M.defaultLabels(4), ['D', 'F', 'J', 'K']);
  assert.deepEqual(M.defaultLabels(6), ['S', 'D', 'F', 'J', 'K', 'L']);
  assert.deepEqual(M.defaultLabels(8), ['A', 'S', 'D', 'F', 'J', 'K', 'L', ';']);
  assert.deepEqual(M.defaultLabels(2), ['1', '2']);
  const item = (id, keys) => ({ id, session: id + '.js', song: 'S', exported: 1, keys });
  const recent = M.parseRecent({ format: 'rhythmhint-editor-recent', items: [item('9-abc', undefined), item('9-abc-6k', 6), item('9-abc-x', 6)] });
  assert.deepEqual(recent.map(r => [r.id, r.keys]), [['9-abc', 4], ['9-abc-6k', 6]]);
});
