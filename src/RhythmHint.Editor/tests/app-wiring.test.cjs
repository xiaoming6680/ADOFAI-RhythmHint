// 编辑器的接线：页面照 index.html 建（dom.cjs），点左侧栏、参考面板、检查器里真的按钮，看界面文字、面板和音乐载入。
// 原来的替身 querySelectorAll 一律返回空，这些按钮从没被测过（2026-11-19 第二轮评审）。
// 运行：node --test src/RhythmHint.Editor/tests/
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { boot, fakeFile, fakeBuffer, session, lanesOf, allLanes, otherLevel, recentOf, T, hasToast, ED } = require('./app-harness.cjs');
const { pageScripts } = require('./dom.cjs');

const panelShown = h => h.document.querySelectorAll('[data-panel-body]').filter(el => !el.hidden).map(el => el.dataset.panelBody);

// ---------- 页面载入的脚本 ----------

test('index.html loads every script in the editor folder, the interface layer last and app.js at the very end', () => {
  // 自动更新只覆盖包里有的文件、不删旧文件：页面少载了一个、载了一个不存在的，只有在玩家那里打开才看得出来
  const scripts = pageScripts();
  // 和打包（RhythmHint.Mod.csproj 的 EditorFile）一样不算游戏运行时写的 session.js、session-audio*
  const onDisk = fs.readdirSync(ED).filter(f => f.endsWith('.js') && f !== 'session.js' && !f.startsWith('session-audio')).sort();
  assert.deepEqual([...scripts].sort(), onDisk);
  const app = scripts.filter(f => /^app(-[a-z]+)?\.js$/.test(f));
  assert.ok(app.length > 1);
  assert.deepEqual(scripts.slice(-app.length), app, 'the interface layer comes after the libraries it uses');
  assert.equal(app[0], 'app-common.js', 'app-common.js sets up the namespace first');
  assert.equal(app[app.length - 1], 'app.js', 'app.js starts the editor once everything is loaded');
});

test('the interface layer adds no global besides its namespace', () => {
  const h = boot({ session: session() });
  assert.deepEqual(Array.from(h.appGlobals), ['EditorApp']);
});

test('the rail opens one panel at a time and closes it again', () => {
  const h = boot({ session: session(), prefs: { wayBackTold: true } });
  h.frames(2);
  assert.ok(h.el('drawer').hidden);
  assert.deepEqual(panelShown(h), []);
  h.railButton('file').click();
  assert.equal(h.el('drawer').hidden, false);
  assert.deepEqual(panelShown(h), ['file']);
  assert.equal(h.el('drawerTitle').textContent, T(h, 'rail.file'));
  assert.ok(h.railButton('file').classList.contains('on'));
  assert.equal(h.el('fSong').textContent, 'Sample Song');
  h.railButton('refs').click();
  assert.deepEqual(panelShown(h), ['refs']);
  assert.ok(!h.railButton('file').classList.contains('on'));
  h.railButton('refs').click(); // 再点一次：滑出去再藏起来
  assert.ok(h.el('drawer').classList.contains('closing'));
  h.el('drawer').dispatch('animationend');
  assert.ok(h.el('drawer').hidden);
  h.railButton('keys').click();
  h.el('drawerClose').click();
  h.el('drawer').dispatch('animationend');
  assert.ok(h.el('drawer').hidden);
});

test('every text, title and placeholder on the page is filled in and follows the language', () => {
  const h = boot({ session: session() });
  h.frames(2);
  const check = () => {
    for (const [attr, prop, key] of [['data-i18n', 'textContent', 'i18n'], ['data-i18n-title', 'title', 'i18nTitle'], ['data-i18n-placeholder', 'placeholder', 'i18nPlaceholder']]) {
      const els = h.document.querySelectorAll('[' + attr + ']');
      assert.ok(els.length > 0, attr);
      els.forEach(el => assert.equal(el[prop], T(h, el.dataset[key]), attr + '=' + el.dataset[key]));
    }
  };
  check();
  assert.equal(h.el('saveBtn').querySelector('span').textContent, 'Save to game'); // 样例会话是游戏用英文导出的
  const lang = h.el('langSelect');
  assert.deepEqual(Array.from(lang.options, o => o.value), ['zh-Hans', 'zh-Hant', 'en', 'ko', 'ja']);
  lang.value = 'zh-Hans';
  lang.dispatch('change');
  check();
  assert.equal(h.el('saveBtn').querySelector('span').textContent, '保存到游戏');
  assert.equal(h.document.documentElement.lang, 'zh-Hans');
  assert.equal(h.el('undoBtn').title, '撤销（Ctrl+Z）');
});

/** 参考排法「交替优先」把单押都排在第 0 轨（和当前手序不一样）的会话。 */
function withAlternate() {
  const s = session();
  s.references.alternate = s.references.alternate.map(n => (n[2].length === 1 ? [n[0], n[1], [[0, n[2][0][1]]]] : n));
  return s;
}

test('the reference panel applies a style to the selection or to the whole song', () => {
  const h = boot({ session: withAlternate() });
  h.frames(2);
  const doc = h.t.state.doc;
  h.railButton('refs').click();
  assert.equal(h.el('refsNone').hidden, true);
  assert.ok(h.document.querySelectorAll('.ref-card').every(c => c.hidden === (c.dataset.ref === 'steady')));
  h.select(1);
  h.document.querySelector('[data-apply="alternate"][data-scope="selection"]').click();
  assert.deepEqual(lanesOf(doc.notes[1]), [0]);
  assert.deepEqual(lanesOf(doc.notes[3]), [2], 'only the selection');
  assert.ok(hasToast(h, 'toast.applied', 1));
  h.document.querySelector('[data-apply="alternate"][data-scope="all"]').click(); // 整首：先问一下
  assert.ok(doc.notes.filter(n => n.lanes.length === 1).every(n => lanesOf(n)[0] === 0));
  h.document.querySelector('[data-apply="standard"][data-scope="all"]').click();
  assert.deepEqual(allLanes(doc), Array.from(doc.notes, n => Array.from(n.orig, l => l[0])));
});

test('applying a style to the whole song asks first, and nothing changes when it is declined', () => {
  const h = boot({ session: withAlternate() });
  h.frames(2);
  let asked = 0;
  h.ctx.confirm = () => { asked++; return false; };
  h.document.querySelector('[data-apply="alternate"][data-scope="all"]').click();
  assert.equal(asked, 1);
  assert.equal(h.t.state.history.canUndo, false);
});

test('the steady reference can be shown and applied to a selected note', () => {
  const data = withAlternate();
  data.references.steady = data.references.alternate;
  const h = boot({ session: data });
  h.frames(2);
  h.railButton('refs').click();
  assert.equal(h.document.querySelector('[data-ref="steady"]').hidden, false);
  const box = h.document.querySelector('[data-show-ref="steady"]');
  box.checked = true;
  box.dispatch('change');
  assert.equal(h.t.state.show.steady, true);
  h.select(1);
  h.document.querySelector('[data-apply="steady"][data-scope="selection"]').click();
  assert.deepEqual(lanesOf(h.t.state.doc.notes[1]), [0]);
});

test('the reference columns can be hidden, and that is remembered', () => {
  const storage = new Map();
  let h = boot({ session: session(), storage });
  h.frames(2);
  const box = h.document.querySelector('[data-show-ref="alternate"]');
  assert.equal(box.checked, true);
  box.checked = false;
  box.dispatch('change');
  assert.equal(h.t.state.show.alternate, false);
  h = boot({ session: session(), storage });
  h.frames(2);
  assert.equal(h.t.state.show.alternate, false);
  assert.equal(h.document.querySelector('[data-show-ref="alternate"]').checked, false);
  assert.equal(h.t.state.show.standard, true);
});

test('the inspector buttons change the selected note', () => {
  const h = boot({ session: session() });
  h.frames(2);
  const doc = h.t.state.doc;
  h.key('ArrowUp'); // 选中第一个音符，检查器换成它
  assert.deepEqual([...h.t.state.selection], [0]);
  const insp = h.el('inspector');
  insp.querySelector('[data-lane="0"]').click();
  assert.deepEqual(lanesOf(doc.notes[0]), [0]);
  h.t.state.selection = new Set([0]);
  h.key('Escape');
  h.key('ArrowUp');
  insp.querySelector('[data-insp="mirror"]').click();
  assert.deepEqual(lanesOf(doc.notes[0]), [3]);
  insp.querySelector('[data-insp="revert"]').click();
  assert.deepEqual(lanesOf(doc.notes[0]), [2]);
  // 双押：点够两个键才改
  h.t.state.selection = new Set([2]);
  h.key('ArrowDown');
  h.key('ArrowUp');
  assert.deepEqual([...h.t.state.selection], [2]);
  insp.querySelector('[data-lane="0"]').click();
  assert.deepEqual(lanesOf(doc.notes[2]), [2, 3]);
  insp.querySelector('[data-lane="1"]').click();
  assert.deepEqual(lanesOf(doc.notes[2]), [0, 1]);
});

test('the open buttons open the file chooser', () => {
  const h = boot({});
  let opened = 0;
  h.el('openInput').addEventListener('click', () => { opened++; });
  assert.equal(h.el('empty').hidden, false);
  h.el('empty').querySelector('[data-action="open"]').click();
  assert.equal(opened, 1);
  h.railButton('file').click();
  h.document.querySelector('[data-panel-body="file"] [data-action="open"]').click();
  assert.equal(opened, 2);
});

test('the history list shows the levels with their progress and opens the one clicked', async () => {
  const a = session(), b = otherLevel('16-abcdef', 0.5);
  const h = boot({ session: a, recent: recentOf(b, a), sessions: { [b.id]: { data: b } } });
  h.frames(2);
  h.railButton('history').click();
  const items = h.el('historyList').querySelectorAll('button[data-index]');
  assert.equal(items.length, 2);
  assert.ok(items[1].classList.contains('current'));
  assert.equal(items[0].querySelector('.title').textContent, b.fingering.song);
  items[0].querySelector('.title').click(); // 点在按钮里的字上也算
  await h.until(() => h.t.state.doc.meta.song === b.fingering.song, 'level b');
  assert.ok(h.el('historyList').querySelectorAll('button[data-index]')[0].classList.contains('current'));
});

// ---------- 音乐 ----------

const SONG = 'recent/16-5cea7782a36abca967cf2ecf-song.ogg';
/** 带音乐和 Mod 另存的歌曲脚本的关卡。 */
function withSong(s = session(), name = '16-5cea7782a36abca967cf2ecf-song') {
  s.audio = { file: 'recent/' + name + '.ogg', script: 'recent/' + name + '.js', offset: 0.5, pitch: 1, calibration: 0, peaksPerSecond: 0, peaks: '' };
  return s;
}
const scriptOf = (s, delay = 0) => ({ [s.audio.script]: { file: s.audio.file, text: Buffer.from('bytes of ' + s.id).toString('base64'), delay } });

test('music picked by hand is not replaced by the level\'s own song when that finishes decoding later', async () => {
  // 复现：刚打开关卡、整首解码还没完时拖入一首歌，解码先完成就换成了关卡的歌，手动那首被取消、没有提示
  const s = withSong();
  const h = boot({ session: s, audioScripts: scriptOf(s), holdLoads: true, holdDecodes: true });
  h.frames(2);
  await h.until(() => h.audio.decodes.length === 1, 'the level song decoding');
  const file = fakeFile('mine.ogg', 'my own song', 'audio/ogg');
  h.el('audioInput').dispatch('change', { target: { files: [file], value: '' } });
  const manual = h.audio.loads.at(-1);
  assert.match(manual.url, /^blob:/);
  const levelSong = fakeBuffer(40);
  h.audio.decodes[0].resolve(levelSong); // 关卡的歌先解完
  await h.wait(5);
  manual.finish();
  await h.until(() => h.audio.decodes.length === 2, 'the picked song decoding');
  const picked = fakeBuffer(50);
  h.audio.decodes[1].resolve(picked);
  await h.until(() => h.audio.used.length > 0, 'a decoded song in use');
  await h.wait(5);
  assert.deepEqual(h.audio.used, [picked]);
  assert.equal(h.el('audioName').textContent, 'mine.ogg');
});

test('switching levels quickly decodes one song at a time and skips the ones never started', async () => {
  // 复现：快速切关卡时上一关的整首解码不取消，连点几关几首同时解码（一首 5 分钟立体声约 110 MB 采样）
  const a = withSong(session(), '16-5cea7782a36abca967cf2ecf-a');
  const b = withSong(otherLevel('16-bbbbbb', 0.5), '16-bbbbbb-b'), c = withSong(otherLevel('16-cccccc', 1), '16-cccccc-c');
  const h = boot({
    session: a, holdDecodes: true, recent: recentOf(b, c, a), sessions: { [b.id]: { data: b }, [c.id]: { data: c } },
    audioScripts: { ...scriptOf(a), ...scriptOf(b), ...scriptOf(c) },
  });
  h.frames(2);
  await h.until(() => h.audio.decodes.length === 1, 'level a decoding');
  h.clickHistory(0);
  await h.until(() => h.t.state.doc.meta.song === b.fingering.song, 'level b');
  h.clickHistory(1);
  await h.until(() => h.t.state.doc.meta.song === c.fingering.song, 'level c');
  await h.wait(20);
  assert.equal(h.audio.decodes.length, 1, 'b and c wait until a is done');
  assert.equal(Object.keys(h.ctx.RHYTHMHINT_AUDIO || {}).length, 0, 'no song text is held while waiting');
  h.audio.decodes[0].resolve(fakeBuffer(10));
  await h.until(() => h.audio.decodes.length === 2, 'level c decoding');
  assert.deepEqual(h.audio.used, [], 'a\'s song is not used on c');
  const song = fakeBuffer(12);
  h.audio.decodes[1].resolve(song);
  await h.until(() => h.audio.used.length === 1, 'c\'s song in use');
  assert.equal(h.audio.used[0], song);
  await h.wait(20);
  assert.equal(h.audio.decodes.length, 2, 'b is never decoded');
});

test('the level\'s own song is used once decoded', async () => {
  const s = withSong();
  const h = boot({ session: s, audioScripts: scriptOf(s) });
  h.frames(2);
  await h.until(() => h.audio.used.length === 1, 'decoded');
  assert.equal(h.audio.player.buffer, h.audio.used[0]);
  assert.equal(h.el('audioName').textContent, T(h, 'audio.fromGame'));
  assert.equal(h.ctx.RHYTHMHINT_AUDIO[SONG], undefined, 'the song text is let go');
});
