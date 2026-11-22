// 手序文件格式的两边约定（编辑器 model.js 这一侧）：和 C# 的 FingeringFormatLockTests 跑同一批样例。
//   fixtures/conformance/accept、reject：两边都要接受 / 拒绝的写法，接受的读出来和 expected.json 一致
//   fixtures/frozen/1.3.x：1.3.x 实际写出的文件，只读，没有重新生成的开关
// 样例的期望改了就是破坏性更新（玩家手里的手序认不出来），只能加样例；拒绝的以后放宽了可以挪进接受，接受的不能挪进拒绝。
// KNOWN 里是两边有意不一致的样例：Mod（Newtonsoft）的宽松读法，编辑器用 JSON.parse 不跟（第三方工具也不该依赖）。
// 这几份断言编辑器拒绝（不再跳过）：哪天编辑器悄悄放宽或改了报错，这里会先知道。
// 运行：node --test src/RhythmHint.Editor/tests/
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const M = require('../model.js');

const fixtures = path.join(__dirname, 'fixtures');
const conformance = path.join(fixtures, 'conformance');
const frozen = name => path.join(fixtures, 'frozen', '1.3.x', name);
const read = file => fs.readFileSync(file, 'utf8');
const expected = JSON.parse(read(path.join(conformance, 'expected.json')));

const KNOWN = {
  'accept/90-lenient-comments.json': 'Mod（Newtonsoft）接受注释，JSON.parse 不认',
  'accept/91-lenient-trailing-commas.json': 'Mod（Newtonsoft）接受结尾多余的逗号，JSON.parse 不认',
  'accept/92-lenient-single-quotes.json': 'Mod（Newtonsoft）接受单引号字符串，JSON.parse 不认',
  'accept/93-lenient-unquoted-names.json': 'Mod（Newtonsoft）接受不加引号的字段名，JSON.parse 不认',
};

function samples(folder) {
  return fs.readdirSync(path.join(conformance, folder)).filter(n => n.endsWith('.json')).sort();
}

/** 和 C# 一样按 expected.json 里列出的项比（数字一律当 double 比）。 */
function check(name, doc) {
  const want = expected[name];
  if (!want) return;
  const first = doc.hits[0];
  const actual = {
    keys: doc.keys, leftHanded: doc.meta.leftHanded, hits: doc.hits.length, notes: doc.notes.length,
    song: doc.meta.song, artist: doc.meta.artist, charter: doc.meta.charter, author: doc.meta.author, comment: doc.meta.comment,
    authorLength: doc.meta.author.length, commentLength: doc.meta.comment.length,
    firstHit: [first.tile, first.time, first.hold, first.angle],
    noteLanes: doc.notes.map(n => n.lanes.map(l => l[0])),
  };
  for (const [key, value] of Object.entries(want)) {
    assert.ok(key in actual, `expected.json 里有不认识的项 ${key}`);
    assert.deepEqual(actual[key], value, `${name} 的 ${key}`);
  }
}

/** 编辑器有意不收的宽松写法：读的时候报「不是有效的 JSON」。 */
function rejectsLenient(file) {
  assert.throws(() => M.fromFileText(read(file)), e => e instanceof M.FingeringError && e.code === 'notJson');
}

for (const name of samples('accept')) {
  const id = 'accept/' + name, file = path.join(conformance, 'accept', name);
  if (KNOWN[id]) test(`Mod 接受、编辑器拒绝 ${name}（有意不一致：${KNOWN[id]}）`, () => rejectsLenient(file));
  else test(`接受 ${name}`, () => check(name, M.fromFileText(read(file))));
}

for (const name of samples('reject')) {
  const id = 'reject/' + name;
  assert.ok(!KNOWN[id], 'KNOWN 只放 Mod 接受、编辑器拒绝的宽松写法：' + id);
  test(`拒绝 ${name}`, () => {
    assert.throws(() => M.fromFileText(read(path.join(conformance, 'reject', name))), M.FingeringError);
  });
}

test('KNOWN 里列的样例都还在', () => {
  for (const id of Object.keys(KNOWN)) assert.ok(fs.existsSync(path.join(conformance, id)), id);
});

test('1.3.x 的编辑器存出的手序：读得出来，内容不变', () => {
  const doc = M.fromFileText(read(frozen('fingering.json')));
  assert.equal(doc.keys, 4);
  assert.equal(doc.meta.leftHanded, true);
  assert.equal(doc.meta.author, '左手作者');
  assert.deepEqual(doc.meta.charts, ['20-9deb47fa32ba25f93b1477fc']); // 改了就是破坏性更新
  assert.equal(doc.hits.length, 20);
  assert.equal(doc.hits[14].angle, null);
  assert.ok(doc.notes.some(n => n.lanes.length === 3) && doc.notes.some(n => n.lanes.length === 4));
  assert.deepEqual(doc.notes[3].lanes.map(l => l[0]), [1, 2], '跨手双押');
  assert.deepEqual(doc.notes.find(n => n.tile === 13).lanes, [[1, 1]], '长按');
});

test('1.3.x 的 Mod 写的会话：读得出来', () => {
  const doc = M.fromSession(JSON.parse(read(frozen('session.json'))));
  assert.equal(doc.sessionId, '16-5cea7782a36abca967cf2ecf'); // 改了就是破坏性更新：草稿、历史按它记
  assert.deepEqual(doc.meta.charts, ['16-5cea7782a36abca967cf2ecf']);
  assert.equal(doc.hits.length, 16);
  assert.equal(doc.notes.length, 13);
  assert.equal(doc.notes.at(-1).auto, true);
  assert.equal(doc.fileName, 'Sample Artist - Sample Song.json');
});

test('1.3.x 的 Mod 写的历史列表：读得出来', () => {
  const items = M.parseRecent(JSON.parse(read(frozen('recent.json'))));
  assert.deepEqual(items.map(i => i.id), ['8-0123456789abcdef01234567', '16-5cea7782a36abca967cf2ecf']);
  assert.equal(items[1].audio, '16-5cea7782a36abca967cf2ecf-20261118-090000-000.ogg');
});

test('不认识的顶层字段原样写回（和 Mod 一样写在已知字段后面、hits 前面）', () => {
  const doc = M.fromFileText(read(path.join(conformance, 'accept', '04-unknown-fields.json')));
  const back = JSON.parse(M.toFileText(doc, new Date(0)));
  assert.deepEqual(back['x-future'], { a: [1, 2, { b: null }] });
  assert.equal(back.license, 'CC-BY-4.0');
  assert.deepEqual(back['x-after'], ['z']);
  const names = Object.keys(back);
  assert.ok(names.indexOf('x-future') > names.indexOf('updatedAt') && names.indexOf('x-after') < names.indexOf('hits'));
});

test('建议的文件名和 Mod 同一规则（见 CustomFingering.SuggestedFileName）', () => {
  const name = (song, artist) => M.suggestedFileName({ song, artist });
  assert.equal(name('CON', ''), '_CON.json');
  assert.equal(name('lpt9.remix', ''), '_lpt9.remix.json');
  assert.equal(name('AUX  .x', ''), '_AUX  .x.json');
  assert.equal(name('CONSOLE', ''), 'CONSOLE.json');
  assert.equal(name('Song', 'CON'), 'CON - Song.json');
  assert.equal(name('a\u0001b\u007f\u0085c\td', ''), 'abcd.json');
  assert.equal(name('\u0001\u0002', ''), 'fingering.json');
  assert.equal(name('...', ''), 'fingering.json');
  assert.equal(name('x'.repeat(79) + '\u{1F600}tail', ''), 'x'.repeat(79) + '.json');
  assert.equal(name('x'.repeat(78) + '\u{1F600}tail', ''), 'x'.repeat(78) + '\u{1F600}.json');
  // 不是 4 键的带键数（同 CustomFingering.SuggestedFileName，ManyKeysFingeringTests.FileNamesCarryTheKeyCount）
  assert.equal(M.suggestedFileName({ song: 'Song', artist: 'Artist' }, 6), 'Artist - Song 6K.json');
  assert.equal(M.suggestedFileName({ song: 'CON', artist: '' }, 8), '_CON 8K.json');
  assert.equal(M.suggestedFileName({ song: 'x'.repeat(90), artist: '' }, 6), 'x'.repeat(80) + ' 6K.json');
  assert.equal(M.suggestedFileName({ song: 'Song', artist: 'Artist' }, 4), 'Artist - Song.json');
});
