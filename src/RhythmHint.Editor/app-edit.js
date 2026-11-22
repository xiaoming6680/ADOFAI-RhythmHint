/* 编辑：改键（换轨、镜像、套用参考、撤销重做）、编辑键位、检查器、谱面画布上的选择、拖动、框选和缩放、键盘快捷键；换关卡时清掉上一关的编辑状态（resetView）。 */
(function (App) {
  'use strict';

  const { M, reduceMotion, $, esc, prefs, savePrefs, state, canvas, view, player, toast, restart, fmt } = App;

  // ---------- 编辑 ----------
  function edited(reset) {
    const doc = state.doc;
    if (!doc) return;
    M.markReview(doc); // 改过的自动补齐音符不再待检查（撤销回去又算）
    state.jacks = M.jacks(doc, prefs.jackMs / 1000);
    App.renderHeader();
    renderInspector();
    if (App.openPanel === 'file') App.renderFilePanel();
    if (!reset) App.scheduleDraft();
  }

  function applyChange(change, animated = true) {
    if (change === null) { toast(state.t('toast.cannotMove'), 'warn'); return false; }
    const before = new Map([...change.keys()].map(id => [id, state.doc.notes[id].lanes]));
    if (!state.history.apply(change)) return false;
    if (animated) animate(before);
    edited(false);
    return true;
  }

  /** 换轨动画：记下改之前在哪几条轨道，画的时候从那里滑过去。 */
  function animate(before) {
    if (reduceMotion) return;
    const t0 = performance.now();
    before.forEach((lanes, id) => state.anims.set(id, { from: lanes, t0 }));
  }

  const selectedNotes = () => [...state.selection].sort((a, b) => a - b).map(id => state.doc.notes[id]);

  /** 换一关时上一关留下的东西都清掉（2026-11-19 评审：没保存的标记、平滑滚动的目标、循环按钮、播放图标会留着）。 */
  function resetView() {
    state.selection.clear();
    state.loop = { a: null, b: null, on: false };
    state.flash = [];
    state.press = [];
    state.anims.clear();
    state.hover = null;
    state.preview = null;
    state.box = null;
    state.timeTarget = null;
    state.zoomAnchor = null;
    state.playing = false;
    drag = null;
    pan = null;
    pendingModifier = null;
    App.metaDirty = false;
    inspectorKey = '';
    App.allowDraft();
  }

  // ---------- 编辑键位（用户要求能在编辑器里自定义；默认和游戏里的键位一样） ----------
  let capturing = null; // 正在等新键的轨道
  /**
   * 各条轨道的键（游戏里的键名，可能是 Keypad4、LeftShift 这样的 Unity 键名）。自己改过的按键数分开记：4 键的还是 prefs.laneKeys
   * （以前的版本记在这里），6、8 键的记在 prefs.laneKeysByCount 里，换着打开 4K、6K 的手序不会互相冲掉（2026-11-22）。
   */
  function laneLabels() {
    const doc = state.doc;
    if (!doc) return ['D', 'F', 'J', 'K'];
    const mine = doc.keys === 4 ? prefs.laneKeys : (prefs.laneKeysByCount || {})[doc.keys];
    return Array.isArray(mine) && mine.length === doc.keys ? mine : doc.keyLabels;
  }
  /** 记下这个键数自己改的键（null 是换回游戏里的键位）。 */
  function setLaneLabels(labels) {
    if (state.doc.keys === 4) prefs.laneKeys = labels;
    else {
      const byCount = Object.assign({}, prefs.laneKeysByCount || {});
      if (labels) byCount[state.doc.keys] = labels; else delete byCount[state.doc.keys];
      prefs.laneKeysByCount = byCount;
    }
  }
  /** 键帽上显示的字（和游戏里一样缩写：Keypad4 → Num4）；没指定键的轨道显示 —。 */
  const shownLabels = () => laneLabels().map(k => M.keyDisplay(k) || '—');
  function renderKeymap() {
    const labels = shownLabels();
    const el = $('keymap');
    el.style.gridTemplateColumns = 'repeat(' + labels.length + ', 1fr)';
    el.innerHTML = labels.map((k, lane) => '<button data-key-lane="' + lane + '" class="' + (capturing === lane ? 'capturing' : '') + '">'
      + '<b>' + esc(capturing === lane ? '…' : k) + '</b><small>' + esc(capturing === lane ? state.t('keys.press') : state.t('keys.laneN', lane + 1)) + '</small></button>').join('');
    el.querySelectorAll('[data-key-lane]').forEach(b => b.addEventListener('click', () => {
      const lane = Number(b.dataset.keyLane);
      capturing = capturing === lane ? null : lane;
      renderKeymap();
    }));
    state.labels = labels;
    const clashes = M.shortcutClashes(laneLabels());
    $('keysClash').hidden = !clashes.length;
    $('keysClash').textContent = clashes.length ? state.t('keys.clash', clashes.join(' ')) : '';
  }
  /**
   * 轨道键和编辑器的快捷键（M、N、[、]、空格、方向键、Home）重复时，按下按轨道键处理，快捷键用不了：提示一次
   * （2026-11-19 评审：键位是 Z X N M 时按 N、M 不跳转、不镜像，变成了改键）。同一组键位只在打开时提示一次。
   */
  function tellClashes() {
    const clashes = M.shortcutClashes(laneLabels());
    const which = laneLabels().join(' ');
    if (!clashes.length || prefs.clashTold === which) return;
    prefs.clashTold = which;
    savePrefs();
    toast(state.t('keys.clash', clashes.join(' ')), 'warn', null, 10000);
  }
  /** 等新键时按下的键：合法就记下（和别的轨道重复就互换），Esc 取消；按着 Ctrl、Alt 的组合键不录、接着等。 */
  function captureKey(e) {
    e.preventDefault();
    if (e.ctrlKey || e.altKey || e.metaKey) return;
    const key = e.key;
    if (key !== 'Escape' && key.length === 1 && key !== ' ') {
      const labels = laneLabels().slice();
      const k = key.toUpperCase(), other = labels.findIndex(x => x.toUpperCase() === k);
      if (other >= 0 && other !== capturing) labels[other] = labels[capturing];
      labels[capturing] = k;
      setLaneLabels(labels);
      prefs.clashTold = labels.join(' '); // 在这里提示过了，下次打开不再提示
      savePrefs();
      if (M.shortcutClashes([k]).length) toast(state.t('keys.clash', M.keyDisplay(k)), 'warn', null, 10000);
    }
    capturing = null;
    renderKeymap();
    App.renderShortcuts();
    renderInspector();
  }
  $('keysReset').addEventListener('click', () => { if (state.doc) setLaneLabels(null); savePrefs(); capturing = null; renderKeymap(); App.renderShortcuts(); renderInspector(); tellClashes(); });
  $('autoNext').checked = prefs.autoNext;
  $('autoNext').addEventListener('change', e => { prefs.autoNext = e.target.checked; savePrefs(); });

  function undo() {
    const step = state.history && state.history.undoStack[state.history.undoStack.length - 1];
    if (!step || !state.history.undo()) return;
    animate(new Map(step.map(([id, , after]) => [id, after])));
    edited(false);
  }
  function redo() {
    const step = state.history && state.history.redoStack[state.history.redoStack.length - 1];
    if (!step || !state.history.redo()) return;
    animate(new Map(step.map(([id, before]) => [id, before])));
    edited(false);
  }
  $('undoBtn').addEventListener('click', undo);
  $('redoBtn').addEventListener('click', redo);

  function laneKeyPressed(lane) {
    const notes = selectedNotes();
    if (!notes.length) return;
    if (notes.every(n => n.lanes.length > 1)) { toast(state.t('toast.chordKey'), 'warn'); return; }
    const change = M.ops.toLane(state.doc, notes, lane);
    state.press[lane] = performance.now(); // 底下这条轨道的按键框亮一下，像在游戏里按下
    if (change === null) { applyChange(null); return; }
    // 本来就在这条轨道上的：玩家看过了、就这么按，自动补齐的不再算待检查
    const kept = notes.filter(n => n.lanes.length === 1 && n.lanes[0][0] === lane && !change.has(n.id));
    applyChange(change);
    confirmNotes(kept);
    // 只选中一个音符时，改完自动跳到下一个（用户要求：用 D F J K 一路改下去；本来就在这条轨道也算确认，照样往下走）
    if (prefs.autoNext && notes.length === 1) stepSelection(1);
  }

  /** 按了音符现在的键、没有改动：自动补齐的记成看过了（2026-11-19 评审：原来改过、看过的待检查标记都不消失）。确认状态进草稿。 */
  function confirmNotes(notes) {
    if (M.confirm(notes)) edited(false);
  }

  /** 需要检查的：还要检查的自动补齐音符（一段只算一处，跳到这一段的开头）和连按。 */
  function checkList() { return M.reviewList(state.doc, state.jacks); }
  function countJackPairs() {
    let count = 0;
    const doc = state.doc;
    for (let i = 1; i < doc.notes.length; i++)
      if (state.jacks.has(doc.notes[i].id) && state.jacks.has(doc.notes[i - 1].id)) count++;
    return count;
  }

  /**
   * 下一处待检查：从选中的音符往后找，到尾了从头再来；没选中时从画面上的位置往后找
   * （2026-11-19 评审：原来按画面时间找，跳过去时画面停在音符前 0.6 秒，下一次找到的还是它）。
   */
  function nextCheck() {
    if (!state.doc) return;
    const list = checkList();
    if (!list.length) { toast(state.t('toast.noCheck'), 'ok'); return; }
    const ids = [...state.selection];
    const from = ids.length ? Math.max(...ids) : view.firstVisible(state.timeTarget !== null ? state.timeTarget : state.time) - 1;
    focusNote(M.nextReview(list, from));
  }

  function focusNote(id) {
    const n = state.doc.notes[id];
    state.selection = new Set([id]);
    seek(n.time - 0.6, true);
    renderInspector();
  }

  /** 跳到某个时间。smooth：暂停时画面平滑滑过去，停下来再让播放器对准；播放中总是直接跳。 */
  function seek(t, smooth) {
    if (smooth && !player.playing && !reduceMotion) { state.timeTarget = t; return; }
    state.timeTarget = null;
    state.time = t;
    state.lastTime = t;
    player.seek(t);
  }

  // ---------- 检查器 ----------
  function renderInspector() {
    const el = $('inspector');
    const doc = state.doc;
    if (!doc) { el.hidden = true; return; }
    el.hidden = false;
    const notes = state.doc ? selectedNotes() : [];
    const labels = shownLabels();
    if (!notes.length) {
      el.innerHTML = '<h2>' + esc(state.t('insp.none')) + '</h2><p class="hint">' + esc(state.t('insp.noneHint', labels.join(' '))) + '</p>'
        + (checkList().length ? '<div class="tool-row"><button class="btn" data-insp="next"><svg><use href="#i-flag"/></svg>' + esc(state.t('insp.next')) + '</button></div>' : '');
      bindInspector(el, notes);
      return;
    }
    const one = notes.length === 1 ? notes[0] : null;
    let html = '<h2>' + esc(state.t('insp.count', notes.length)) + '</h2>';
    if (one) {
      const i = one.id;
      const prev = i > 0 ? doc.notes[i - 1] : null, next = i + 1 < doc.notes.length ? doc.notes[i + 1] : null;
      const ms = (a, b) => a && b ? Math.round((b.time - a.time) * 1000) + ' ms' : '—';
      const tags = [];
      if (one.filled) tags.push('<span class="tag auto">' + esc(state.t('source.auto')) + '</span>');
      else if (state.source === 'game' && doc.usedFile) tags.push('<span class="tag custom">' + esc(state.t('source.custom')) + '</span>');
      if (M.isChanged(one)) tags.push('<span class="tag changed">' + esc(state.t('source.changed')) + '</span>');
      html += '<dl class="kv">'
        + '<dt>' + esc(state.t('insp.time')) + '</dt><dd class="mono">' + esc(fmt(one.time)) + '</dd>'
        + '<dt>' + esc(state.t('insp.tile')) + '</dt><dd class="mono">#' + one.tile + (one.lastTile !== one.tile ? '–' + one.lastTile : '') + '</dd>'
        + '<dt>' + esc(state.t('insp.gap')) + '</dt><dd>' + esc(state.t('insp.gapValue', ms(prev, one), ms(one, next))) + '</dd>'
        + '<dt>' + esc(state.t('insp.kind')) + '</dt><dd>' + esc(state.t('kind.' + Math.min(8, one.lanes.length))) + '</dd>'
        + (tags.length ? '<dt>' + esc(state.t('insp.source')) + '</dt><dd>' + tags.join(' ') + '</dd>' : '')
        + '</dl>';
      if (one.auto) html += '<div class="note-callout warn">' + esc(state.t('insp.autoNote')) + '</div>'; // 还要检查时才提醒
      if (state.jacks.has(one.id)) html += '<div class="note-callout danger">' + esc(state.t('insp.jackNote', prefs.jackMs)) + '</div>';
      const cls = one.lanes.length > 2 ? ' c3' : one.lanes.length > 1 ? ' c2' : '';
      html += '<div><div class="hint" style="margin-bottom:6px">' + esc(one.lanes.length > 1 ? state.t('insp.lanesHint', one.lanes.length) : state.t('insp.lanes')) + '</div>'
        + '<div class="lanes" style="grid-template-columns:repeat(' + doc.keys + ',1fr)">'
        + labels.map((k, lane) => '<button data-lane="' + lane + '" class="' + (one.lanes.some(l => l[0] === lane) ? 'on' + cls : '') + (lane === doc.keys / 2 ? ' hand-gap' : '') + '">' + esc(k) + '</button>').join('')
        + '</div></div>';
    }
    html += '<div class="tool-row">'
      + '<button class="btn" data-insp="left" title="←"><svg><use href="#i-left"/></svg>' + esc(state.t('insp.left')) + '</button>'
      + '<button class="btn" data-insp="right" title="→">' + esc(state.t('insp.right')) + '<svg><use href="#i-right"/></svg></button>'
      + '<button class="btn" data-insp="mirror" title="M"><svg><use href="#i-mirror"/></svg>' + esc(state.t('insp.mirror')) + '</button></div>';
    if (doc.refs.standard.size || doc.refs.alternate.size)
      html += '<div class="tool-row"><button class="btn" data-insp="standard"><span class="dot standard"></span>' + esc(state.t('col.standard')) + '</button>'
        + '<button class="btn" data-insp="alternate"><span class="dot alternate"></span>' + esc(state.t('col.alternate')) + '</button></div>';
    html += '<div class="tool-row"><button class="btn" data-insp="revert"><svg><use href="#i-revert"/></svg>' + esc(state.t('insp.revert')) + '</button>'
      + (checkList().length ? '<button class="btn" data-insp="next"><svg><use href="#i-flag"/></svg>' + esc(state.t('insp.next')) + '</button>' : '') + '</div>';
    el.innerHTML = html;
    bindInspector(el, notes);
    const key = notes.map(n => n.id).join(',');
    if (key !== inspectorKey) { inspectorKey = key; restart(el, 'swap'); }
  }
  let inspectorKey = '';

  // 检查器里点选多押的键：先记下点了哪几个，凑够个数再改
  let chordPick = [];
  function bindInspector(el, notes) {
    chordPick = [];
    // 点的就是它现在的键（没有改动）：算看过了
    const pick = (note, lanes) => {
      const change = M.ops.set(state.doc, note, lanes);
      if (change && change.size === 0) confirmNotes([note]);
      else applyChange(change);
    };
    el.querySelectorAll('[data-lane]').forEach(b => b.addEventListener('click', () => {
      const note = notes[0], lane = Number(b.dataset.lane);
      if (note.lanes.length === 1) { pick(note, [lane]); return; }
      if (chordPick.includes(lane)) chordPick = chordPick.filter(x => x !== lane);
      else chordPick.push(lane);
      el.querySelectorAll('[data-lane]').forEach(x => {
        const l = Number(x.dataset.lane);
        x.classList.toggle('on', chordPick.includes(l));
        x.classList.toggle(note.lanes.length > 2 ? 'c3' : 'c2', chordPick.includes(l));
      });
      if (chordPick.length === note.lanes.length) pick(note, chordPick);
    }));
    el.querySelectorAll('[data-insp]').forEach(b => b.addEventListener('click', () => {
      const doc = state.doc;
      switch (b.dataset.insp) {
        case 'left': applyChange(M.ops.shift(doc, notes, -1)); break;
        case 'right': applyChange(M.ops.shift(doc, notes, 1)); break;
        case 'mirror': applyChange(M.ops.mirror(doc, notes)); break;
        case 'revert': applyChange(M.ops.revert(doc, notes)); break;
        case 'standard': case 'alternate': applyReference(b.dataset.insp, notes); break;
        case 'next': nextCheck(); break;
      }
    }));
  }

  function applyReference(which, notes) {
    const change = M.ops.reference(state.doc, notes, which);
    if (!change || change.size === 0) { toast(state.t('toast.nothing'), 'warn'); return; }
    if (applyChange(change)) toast(state.t('toast.applied', change.size), 'ok');
  }
  document.querySelectorAll('[data-apply]').forEach(b => b.addEventListener('click', () => {
    if (!state.doc) return;
    const which = b.dataset.apply;
    if (b.dataset.scope === 'all') {
      if (!window.confirm(state.t('refs.confirmAll', state.t('col.' + which)))) return;
      applyReference(which, state.doc.notes);
    } else applyReference(which, selectedNotes());
  }));

  // ---------- 画布交互 ----------
  let drag = null, pan = null, spaceDown = false, spacePanned = false;
  // 框选出来的那份选择（同一个 Set）：拖其中的多押时整个挪，不再只挪按住的键（2026-11-20 用户截图）。
  // 点音符、按 N、换关卡都会换一个新的 Set，自然就不是它了；直接按住没选中的多押拖，仍然只挪那个键
  let boxPicked = null;
  const pos = e => { const r = canvas.getBoundingClientRect(); return [e.clientX - r.left, e.clientY - r.top]; };

  canvas.addEventListener('mousedown', e => {
    if (!state.doc) return;
    const [x, y] = pos(e);
    if (e.button === 1 || e.button === 2 || spaceDown) {
      pan = { y, time: state.time };
      spacePanned = true;
      canvas.classList.add('grabbing');
      e.preventDefault();
      return;
    }
    if (e.button !== 0) return;
    const hit = view.hit(x, y, state);
    if (hit.kind === 'note') {
      const id = hit.note.id;
      if (e.shiftKey || e.ctrlKey) {
        if (state.selection.has(id)) state.selection.delete(id); else state.selection.add(id);
        renderInspector();
        return;
      }
      if (!state.selection.has(id)) { state.selection = new Set([id]); renderInspector(); }
      drag = { note: hit.note, lane: hit.lane, x, moved: false, change: undefined, whole: state.selection === boxPicked };
    } else if (hit.kind === 'ruler' || hit.kind === 'empty' || hit.kind === 'outside') {
      // 拖动就框选。从左边时间尺起手也一样，框伸进中间谱面就选中框住的音符；在时间尺上单击不拖仍跳到那里
      // （2026-11-18 用户：从时间尺往谱面里框选会变成拖着谱面滚动）。起点按谱面时间记，靠边自动滚动时框跟着谱面走
      state.box = { x0: x, y0: y, x1: x, y1: y, add: e.shiftKey, t0: view.timeAt(y, state), seekTo: hit.kind === 'ruler' ? hit.time : null };
    }
  });
  canvas.addEventListener('contextmenu', e => e.preventDefault());

  window.addEventListener('mousemove', e => {
    if (!state.doc) return;
    const [x, y] = pos(e);
    if (pan) { state.time = pan.time + (y - pan.y) / state.pxPerSec; player.seek(state.time); return; }
    if (drag) {
      if (!drag.moved && Math.abs(x - drag.x) < 6) return;
      drag.moved = true;
      const lane = Math.max(0, Math.min(state.doc.keys - 1, view.laneAt(x)));
      const notes = selectedNotes();
      let change;
      if (notes.length === 1 && notes[0].lanes.length > 1 && !drag.whole) change = M.ops.moveKey(state.doc, notes[0], drag.lane, lane);
      else if (notes.length === 1 && notes[0].lanes.length === 1) change = M.ops.toLane(state.doc, notes, lane);
      else change = M.ops.shift(state.doc, notes, lane - drag.lane);
      drag.change = change;
      state.preview = change;
      canvas.className = 'lane';
      return;
    }
    if (state.box) { state.box.x1 = x; state.box.y1 = y; return; }
    if (e.target !== canvas) return;
    const hit = view.hit(x, y, state);
    state.hover = hit.kind === 'note' ? hit.note.id : null;
    canvas.className = spaceDown ? 'grab' : hit.kind === 'note' ? 'lane' : '';
  });

  window.addEventListener('mouseup', () => {
    if (pan) { pan = null; canvas.classList.remove('grabbing'); return; }
    if (drag) {
      if (drag.moved && drag.change !== undefined) applyChange(drag.change, false);
      drag = null;
      state.preview = null;
      canvas.className = '';
      return;
    }
    if (state.box) {
      const tiny = Math.abs(state.box.x1 - state.box.x0) < 3 && Math.abs(state.box.y1 - state.box.y0) < 3;
      if (tiny && state.box.seekTo !== null) { seek(state.box.seekTo); state.box = null; return; } // 时间尺上单击：跳过去
      const ids = view.notesInBox(state.box, state);
      if (!state.box.add) state.selection = new Set();
      ids.forEach(id => state.selection.add(id));
      if (tiny && !state.box.add) state.selection = new Set();
      boxPicked = state.selection;
      state.box = null;
      renderInspector();
    }
  });

  canvas.addEventListener('wheel', e => {
    if (!state.doc) return;
    e.preventDefault();
    const [, y] = pos(e);
    if (e.ctrlKey) {
      const tCursor = view.timeAt(y, state);
      setZoom((state.zoomTarget || state.pxPerSec) * Math.exp(-e.deltaY * 0.0015), tCursor, y);
    } else if (player.playing) {
      seek(state.time - e.deltaY / state.pxPerSec * 0.9);
    } else {
      const base = state.timeTarget !== null ? state.timeTarget : state.time;
      seek(base - e.deltaY / state.pxPerSec * 0.9, true);
    }
  }, { passive: false });

  /**
   * 缩放（平滑过渡）。暂停时以鼠标所在的时间为准不动；播放中以判定线为准、不跳转
   * （复现：播放时按住 Ctrl 滚轮会从头开始播放——原来缩放时会跳转播放位置）。
   */
  const ZOOM_MIN = 30, ZOOM_MAX = 2000; // 每秒多少像素
  const zoomSlider = $('zoomSlider');
  const sliderOf = px => Math.round(Math.log(px / ZOOM_MIN) / Math.log(ZOOM_MAX / ZOOM_MIN) * 1000);
  const pxOfSlider = v => ZOOM_MIN * Math.pow(ZOOM_MAX / ZOOM_MIN, v / 1000);
  function setZoom(px, tCursor, y, fromSlider) {
    const target = Math.max(ZOOM_MIN, Math.min(ZOOM_MAX, px));
    if (!fromSlider) zoomSlider.value = String(sliderOf(target));
    state.zoomTarget = target;
    state.zoomAnchor = !player.playing && tCursor !== undefined ? { time: tCursor, y } : null;
    if (reduceMotion) { applyZoom(target); state.zoomTarget = null; }
    prefs.pxPerSec = target;
    savePrefs();
    $('zoomLabel').textContent = Math.round(target / 240 * 100) + '%';
  }
  function applyZoom(px) {
    state.pxPerSec = px;
    if (state.zoomAnchor && view.geom && !player.playing) {
      state.time = state.zoomAnchor.time - (view.geom.playY - state.zoomAnchor.y) / px;
      state.timeTarget = null;
    }
  }
  zoomSlider.addEventListener('input', () => setZoom(pxOfSlider(Number(zoomSlider.value)), undefined, undefined, true));
  zoomSlider.addEventListener('wheel', e => { // 2026-11-18 用户：鼠标放在滑动条上用滚轮缩放
    e.preventDefault();
    setZoom((state.zoomTarget || state.pxPerSec) * Math.exp(-e.deltaY * 0.0015));
  }, { passive: false });
  $('zoomIn').addEventListener('click', () => setZoom(state.pxPerSec * 1.25));
  $('zoomOut').addEventListener('click', () => setZoom(state.pxPerSec / 1.25));
  $('zoomFit').addEventListener('click', () => {
    if (!state.doc || !view.geom) return;
    const end = App.songEnd();
    setZoom((view.geom.playY - view.geom.noteTop) / Math.max(1, end));
    seek(0, true);
  });

  // ---------- 键盘 ----------
  // 轨道键优先于编辑器的快捷键（重复时打开关卡、录键时提示过）。游戏里的 Unity 键名（Keypad4、LeftShift……）按 e.code 认。
  // Shift、Ctrl 这类键当轨道键时，单独按下再松开才算：按着它点音符加选、按组合键都不改键
  let pendingModifier = null;
  window.addEventListener('mousedown', () => { pendingModifier = null; }, true);
  const inTextBox = e => !!(e.target && e.target.closest && e.target.closest('input, textarea, select'));
  const isSaveKey = e => (e.ctrlKey || e.metaKey) && !e.altKey && e.key.toLowerCase() === 's';

  /** 按下的是轨道键就改键（或记下按着的 Shift、Ctrl），返回 true；不是返回 false。 */
  function laneKeyDown(e) {
    const labels = laneLabels(), lane = M.laneOfKey(labels, e);
    if (lane < 0) return false;
    if (M.isModifier(labels[lane])) {
      if (!e.repeat) pendingModifier = { code: e.code, lane };
      return true;
    }
    if (e.ctrlKey || e.metaKey || e.altKey) return false; // 组合键：交给快捷键
    e.preventDefault();
    if (document.activeElement && document.activeElement.tagName === 'BUTTON') document.activeElement.blur(); // 空格当轨道键时别再「按」一次刚点过的按钮
    laneKeyPressed(lane);
    return true;
  }

  window.addEventListener('keydown', e => {
    if (pendingModifier && e.code !== pendingModifier.code) pendingModifier = null; // 按着的 Shift、Ctrl 是组合键的一部分
    if (inTextBox(e)) {
      // 输入框里按 Ctrl+S 也是保存，不弹出浏览器的另存网页；先让输入框交出焦点（文件名在 change 时才生效）
      if (isSaveKey(e)) { e.preventDefault(); if ($('modal').hidden) { e.target.blur(); App.saveToGame(); } }
      return;
    }
    if (!$('modal').hidden) { if (e.key === 'Escape') App.closeModal(false); else if (isSaveKey(e)) e.preventDefault(); return; }
    if (capturing !== null) { captureKey(e); return; }
    if (state.doc && laneKeyDown(e)) return;
    const key = e.key, ctrl = e.ctrlKey || e.metaKey;
    if (key === ' ') {
      e.preventDefault();
      // 刚点过的按钮还有焦点时，空格会再「按」一次它：先移走焦点
      if (document.activeElement && document.activeElement.tagName === 'BUTTON') document.activeElement.blur();
      if (!e.repeat) { spaceDown = true; spacePanned = false; }
      return;
    }
    if (!state.doc) { if (ctrl && key.toLowerCase() === 'o') { e.preventDefault(); App.askOpen(); } return; }
    if (ctrl && key.toLowerCase() === 'z') { e.preventDefault(); e.shiftKey ? redo() : undo(); return; }
    if (ctrl && key.toLowerCase() === 'y') { e.preventDefault(); redo(); return; }
    if (ctrl && key.toLowerCase() === 's') { e.preventDefault(); App.saveToGame(); return; }
    if (ctrl && key.toLowerCase() === 'a') { e.preventDefault(); state.selection = new Set(state.doc.notes.map(n => n.id)); renderInspector(); return; }
    if (ctrl && key.toLowerCase() === 'o') { e.preventDefault(); App.askOpen(); return; }
    if (ctrl || e.altKey) return;
    switch (key) {
      case 'ArrowLeft': e.preventDefault(); applyChange(M.ops.shift(state.doc, selectedNotes(), -1)); break;
      case 'ArrowRight': e.preventDefault(); applyChange(M.ops.shift(state.doc, selectedNotes(), 1)); break;
      case 'ArrowUp': case 'ArrowDown': e.preventDefault(); stepSelection(key === 'ArrowUp' ? 1 : -1); break;
      case 'm': case 'M': applyChange(M.ops.mirror(state.doc, selectedNotes())); break;
      case 'n': case 'N': nextCheck(); break;
      case 'Home': seek(state.doc.notes[0].time - 1.2, true); break;
      case 'Escape': state.selection = new Set(); renderInspector(); if (App.openPanel) App.togglePanel(App.openPanel); break;
      case '[': App.setLoop('a'); break;
      case ']': App.setLoop('b'); break;
      default: return;
    }
  });
  window.addEventListener('keyup', e => {
    if (pendingModifier && e.code === pendingModifier.code) {
      const lane = pendingModifier.lane;
      pendingModifier = null;
      if (state.doc && !inTextBox(e) && $('modal').hidden && capturing === null) laneKeyPressed(lane);
      return;
    }
    if (e.key !== ' ' || inTextBox(e)) return;
    e.preventDefault();
    const wasDown = spaceDown; // 空格是轨道键时按下已经改了键，这里不再播放
    spaceDown = false;
    canvas.classList.remove('grab');
    if (wasDown && !spacePanned) App.togglePlay();
  });

  /** 上下键：选中下一个 / 上一个音符。 */
  function stepSelection(dir) {
    const notes = state.doc.notes;
    const ids = [...state.selection];
    let i = ids.length ? (dir > 0 ? Math.max(...ids) + 1 : Math.min(...ids) - 1) : view.firstVisible(state.time);
    i = Math.max(0, Math.min(notes.length - 1, i));
    state.selection = new Set([i]);
    const n = notes[i];
    if (!state.playing && (view.yAt(n.time, state) < view.geom.noteTop + 40 || view.yAt(n.time, state) > view.geom.playY)) seek(n.time - 0.4, true);
    renderInspector();
  }

  Object.assign(App, { edited, resetView, shownLabels, renderKeymap, tellClashes, checkList, countJackPairs, nextCheck, seek, renderInspector, zoomSlider, sliderOf, applyZoom });
})(window.EditorApp);
