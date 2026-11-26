/* 手序编辑器的入口（最后载入）：显示设置、左边的抽屉（文件面板、快捷键表）、顶栏、关于，最后启动（「开始」）。
 * 界面层的其余部分在 app-*.js，载入顺序和分工见 app-common.js 开头。 */
(function (App) {
  'use strict';

  const { M, I18N, reduceMotion, $, esc, prefs, savePrefs, session, state, pickLanguage, applyLanguage, langSelect, toast, errorText, restart, copy } = App;

  // ---------- 显示设置 ----------
  [['vBeats', 'beats'], ['vWave', 'wave'], ['vGaps', 'gaps'], ['vGhost', 'ghost']].forEach(([id, key]) => {
    $(id).checked = state.show[key];
    $(id).addEventListener('change', e => { state.show[key] = e.target.checked; prefs[key] = e.target.checked; savePrefs(); });
  });
  // 音符样式：默认和游戏里一样（游戏设置里的横条 / 圆形），这里也能临时换
  function applyNoteStyle() {
    state.round = prefs.noteStyle === 'round' || (prefs.noteStyle !== 'bar' && !!(state.doc && state.doc.roundNotes));
  }
  $('vStyle').value = prefs.noteStyle || 'game';
  $('vStyle').addEventListener('change', e => {
    prefs.noteStyle = e.target.value === 'game' ? null : e.target.value;
    savePrefs();
    applyNoteStyle();
  });
  $('vTrack').checked = prefs.track;
  $('trackCard').hidden = !prefs.track;
  $('vTrack').addEventListener('change', e => {
    prefs.track = e.target.checked;
    savePrefs();
    $('trackCard').hidden = !prefs.track || !state.doc || state.doc.path.length < 2;
    App.resize();
  });
  $('jackMs').value = String(prefs.jackMs);
  $('jackOut').textContent = String(prefs.jackMs);
  $('jackMs').addEventListener('input', e => {
    prefs.jackMs = Number(e.target.value);
    $('jackOut').textContent = String(prefs.jackMs);
    savePrefs();
    App.edited(false);
  });
  $('refScale').value = String(prefs.refScale);
  $('refScaleOut').textContent = prefs.refScale + '%';
  $('refScale').addEventListener('input', e => {
    prefs.refScale = Number(e.target.value);
    state.refScale = prefs.refScale / 100;
    $('refScaleOut').textContent = prefs.refScale + '%';
    savePrefs();
  });
  document.querySelectorAll('[data-show-ref]').forEach(box => {
    const key = box.dataset.showRef;
    box.checked = state.show[key];
    box.addEventListener('change', () => { state.show[key] = box.checked; prefs[key] = box.checked; savePrefs(); });
  });

  // ---------- 抽屉 ----------
  function togglePanel(name) {
    const drawer = $('drawer'), was = App.openPanel;
    App.openPanel = App.openPanel === name ? null : name;
    document.querySelectorAll('#rail button').forEach(b => b.classList.toggle('on', b.dataset.panel === App.openPanel));
    if (!App.openPanel && was && !reduceMotion) {
      // 关的时候滑出去再藏起来
      drawer.classList.add('closing');
      drawer.addEventListener('animationend', () => { drawer.classList.remove('closing'); if (!App.openPanel) drawer.hidden = true; }, { once: true });
    } else {
      drawer.classList.remove('closing');
      drawer.hidden = !App.openPanel;
      if (App.openPanel && was && was !== App.openPanel) restart(drawer, 'swap');
    }
    document.querySelectorAll('[data-panel-body]').forEach(el => { el.hidden = el.dataset.panelBody !== App.openPanel; });
    if (App.openPanel) $('drawerTitle').textContent = state.t('rail.' + App.openPanel);
    if (App.openPanel === 'file') renderFilePanel();
    if (App.openPanel === 'history') App.renderHistory();
    App.resize();
  }
  document.querySelectorAll('#rail button').forEach(b => b.addEventListener('click', () => togglePanel(b.dataset.panel)));
  $('drawerClose').addEventListener('click', () => togglePanel(App.openPanel));

  function renderFilePanel() {
    const doc = state.doc;
    const meta = doc ? doc.meta : { song: '', artist: '', charter: '', author: '', comment: '' };
    $('fSong').textContent = M.plain(meta.song) || '—';
    $('fArtist').textContent = M.plain(meta.artist) || '—';
    $('fCharter').textContent = M.plain(meta.charter) || '—';
    // 键数：从游戏打开的关卡带着 4K、6K、8K 几种，点一下切换（2026-11-22 用户）；手序文件只有它自己的键数
    const choices = doc ? (doc.keyChoices.length ? doc.keyChoices : [doc.keys]) : [];
    const fixed = choices.length < 2 || !state.gameSession;
    $('fKeys').title = doc ? state.t(fixed ? 'file.keysFixed' : 'file.keysSwitch') : '';
    $('fKeys').innerHTML = choices.length ? choices.map(k => '<button class="chip' + (k === doc.keys ? ' on' : '') + '" data-keys="' + k + '"'
      + (fixed || k === doc.keys ? ' disabled' : '') + ' aria-pressed="' + (k === doc.keys) + '">' + k + 'K</button>').join('') : '—';
    if (document.activeElement !== $('fAuthor')) $('fAuthor').value = meta.author;
    if (document.activeElement !== $('fComment')) $('fComment').value = meta.comment;
    if (document.activeElement !== $('fName')) $('fName').value = doc ? doc.fileName.replace(/\.json$/i, '') : '';
    const folder = doc && doc.saveFolder ? doc.saveFolder : '…/Mods/RhythmHint/Fingerings';
    const parts = folder.split(/[\\/]/).filter(Boolean);
    $('fFolderName').textContent = parts[parts.length - 1] || folder;
    $('fFolder').textContent = folder;
    $('changeFolder').hidden = !window.showDirectoryPicker || !doc; // 别的浏览器是下载，没有保存位置可换
    if (doc) {
      const s = M.stats(doc, state.jacks);
      const stat = (n, label, hot) => '<div class="stat' + (hot ? ' hot' : '') + '"><b>' + n + '</b><span title="' + esc(label) + '">' + esc(label) + '</span></div>';
      $('fStats').innerHTML = stat(s.notes, state.t('file.statNotes')) + stat(s.changed, state.t('file.statChanged'), s.changed > 0)
        + stat(s.auto, state.t('file.statAuto')) + stat(App.countJackPairs(), state.t('file.statJacks'));
    } else $('fStats').innerHTML = '';
    const hasRefs = doc && Object.values(doc.refs).some(ref => ref.size);
    $('refsNone').hidden = !!hasRefs || !doc;
    document.querySelectorAll('.ref-card').forEach(c => { c.hidden = !doc?.refs[c.dataset.ref]?.size; });
  }
  $('fAuthor').addEventListener('input', e => { if (state.doc) { state.doc.meta.author = e.target.value; markMetaDirty(); } });
  $('fComment').addEventListener('input', e => { if (state.doc) { state.doc.meta.comment = e.target.value; markMetaDirty(); } });
  $('fName').addEventListener('change', e => {
    if (!state.doc) return;
    // 输入框里只写名字，.json 固定显示在框里；控制字符、路径符号、Windows 保留名照建议名一样处理
    const name = M.cleanFileName(e.target.value.trim().replace(/\.json$/i, '')) || M.suggestedFileName(state.doc.meta, state.doc.keys).replace(/\.json$/i, '');
    e.target.value = name;
    if (state.doc.fileName === name + '.json') return;
    state.doc.fileName = name + '.json';
    markMetaDirty(); // 换了名字还没存：游戏里还是原来那个文件。文件名也进草稿，从历史接着改时沿用
  });
  $('fKeys').addEventListener('click', e => {
    const b = e.target.closest('button[data-keys]');
    const keys = b ? Number(b.dataset.keys) : 0;
    if (!keys || !state.doc || keys === state.doc.keys || !state.gameSession) return;
    App.writeDraft(); // 这一种键数改到一半的先存进草稿，切回来接着改
    try {
      App.openSession(state.gameSession, keys);
      toast(state.t('toast.keys', keys), 'ok');
    } catch (err) {
      toast(state.t('toast.openFailed', errorText(err)), 'error');
    }
  });
  function markMetaDirty() { App.metaDirty = true; App.metaEdits++; renderHeader(); App.scheduleDraft(); }
  $('copyFolder').addEventListener('click', e => copy($('fFolder').textContent, e.currentTarget));

  function renderShortcuts() {
    const labels = App.shownLabels();
    const rows = [
      ['keys.play', '<kbd>Space</kbd>'], ['keys.lane', labels.map(k => '<kbd>' + esc(k) + '</kbd>').join('')],
      ['keys.drag', ''], ['keys.shift', '<kbd>←</kbd><kbd>→</kbd>'], ['keys.mirror', '<kbd>M</kbd>'],
      ['keys.undo', '<kbd>Ctrl</kbd><kbd>Z</kbd> / <kbd>Ctrl</kbd><kbd>Shift</kbd><kbd>Z</kbd>'], ['keys.save', '<kbd>Ctrl</kbd><kbd>S</kbd>'],
      ['keys.home', '<kbd>Home</kbd>'], ['keys.next', '<kbd>N</kbd>'], ['keys.all', '<kbd>Ctrl</kbd><kbd>A</kbd> / <kbd>Esc</kbd>'],
      ['keys.loop', '<kbd>[</kbd><kbd>]</kbd>'], ['keys.scroll', esc(state.t('keys.wheel'))], ['keys.zoom', '<kbd>Ctrl</kbd> + ' + esc(state.t('keys.wheel'))],
      ['keys.pan', esc(state.t('keys.spaceDrag'))], ['keys.box', esc(state.t('keys.emptyDrag'))],
    ];
    $('shortcuts').innerHTML = rows.map(([k, v]) => '<dt>' + esc(state.t(k)) + '</dt><dd>' + v + '</dd>').join('');
  }

  // ---------- 顶栏 ----------
  function renderHeader() {
    const doc = state.doc;
    $('docSource').textContent = doc ? state.t('doc.song') : '';
    $('docTitle').textContent = doc ? [M.plain(doc.meta.artist), M.plain(doc.meta.song)].filter(Boolean).join(' – ') || doc.fileName : state.t('doc.untitled');
    $('docDirty').classList.toggle('on', !!(state.history && (state.history.dirty || App.metaDirty)));
    $('undoBtn').disabled = !(state.history && state.history.canUndo);
    $('redoBtn').disabled = !(state.history && state.history.canRedo);
    $('saveBtn').disabled = !doc;
    $('saveAsBtn').disabled = !doc;
    const check = doc ? App.checkList().length : 0;
    $('docCheck').hidden = check === 0;
    $('docCheck').textContent = state.t('doc.check', check);
  }
  $('docCheck').addEventListener('click', () => App.nextCheck());

  // ---------- 关于 ----------
  // 作者、贡献者、仓库、交流群（about.js；和 Mod 设置窗口的「关于」是同一份，about.test.cjs 核对）
  (function renderAbout() {
    const about = window.EditorAbout;
    // 身份（作者 / 贡献者）的文字由 applyLanguage 按 data-i18n 填
    const person = (name, url, role, tone) => '<a class="person" href="' + esc(url) + '" target="_blank" rel="noopener">'
      + '<span class="avatar' + tone + '" aria-hidden="true">' + esc(name.charAt(0).toUpperCase()) + '</span>'
      + '<span class="loc-text"><b>' + esc(name) + '</b><small data-i18n="' + role + '"></small></span>'
      + '<svg class="ext"><use href="#i-external"/></svg></a>';
    $('aboutPeople').innerHTML = person(about.author, about.authorUrl, 'about.author', '')
      + about.contributors.map(c => person(c.name, c.url, 'about.contributor', ' blue')).join('');
    $('aboutRepo').href = about.repo;
    $('aboutRepoText').textContent = about.repo.replace(/^https:\/\/github\.com\//, '');
    $('aboutQq').textContent = about.qq;
    $('copyQq').addEventListener('click', e => copy(about.qq, e.currentTarget));
  })();

  // 别的文件要用的先挂上去：下面启动时就会用到
  Object.assign(App, { applyNoteStyle, togglePanel, renderFilePanel, renderShortcuts, renderHeader });

  // ---------- 开始 ----------
  langSelect.value = I18N.LANGUAGES.some(([c]) => c === pickLanguage()) ? pickLanguage() : 'en';
  applyLanguage(langSelect.value);
  $('zoomLabel').textContent = Math.round(state.pxPerSec / 240 * 100) + '%';
  App.zoomSlider.value = String(App.sliderOf(state.pxPerSec));
  document.querySelectorAll('[data-panel-body]').forEach(el => { el.hidden = true; });
  if (session) {
    try { App.openSession(session); } catch (e) { toast(state.t('toast.openFailed', errorText(e)), 'error'); $('empty').hidden = false; }
  } else {
    $('empty').hidden = false;
    renderHeader();
    if (App.recent.length) togglePanel('history');
  }
  App.pruneDrafts(false); // 只留最近一批草稿（正在编辑的这一关不删）
  if (session && !prefs.wayBackTold) {
    prefs.wayBackTold = true;
    savePrefs();
    toast(state.t('history.tip'), '', [[state.t('history.gotIt'), () => {}, true], [state.t('history.show'), () => { if (App.openPanel !== 'history') togglePanel('history'); }]], 30000);
  }
  App.renderInspector();
  renderShortcuts();
  App.resize();
  requestAnimationFrame(App.frame);
})(window.EditorApp);
