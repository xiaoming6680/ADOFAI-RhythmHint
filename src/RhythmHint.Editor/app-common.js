/* 手序编辑器界面层的公共部分：命名空间 window.EditorApp（下面叫 App）、偏好、状态、语言、提示条和几个小工具。
 * 界面层把页面、画布、播放和数据接起来，数据和编辑操作在 model.js（可测）。界面层分成几个普通脚本（file:// 下不能用 ES 模块），
 * 按 index.html 的顺序载入：app-common.js → app-open.js → app-edit.js → app-play.js → app-save.js → app-draft.js → app.js（入口，最后载入、负责启动）。
 * 每个文件把别的文件要用的东西挂到 App 上：不会重新赋值的（state、prefs、播放器、画布……）后面的文件开头取出来直接用；
 * 会重新赋值的状态（openSeq、openPanel、metaDirty、metaEdits）只放在 App 上，各文件都写 App.xxx 读写；
 * 别的文件的函数写 App.xxx() 在用到时才取，启动（app.js 的「开始」）之前不调用后面文件的函数。 */
(function (App) {
  'use strict';

  const M = window.FingeringModel, I18N = window.EditorI18n;
  // 系统开了「减少动态效果」就不做动画（滑动、缩放、换轨都直接到位）
  const reduceMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  const $ = id => document.getElementById(id);
  const esc = s => String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);

  // ---------- 偏好（只存在这台电脑的浏览器里，读不了就用默认） ----------
  // 打拍声默认打开（2026-11-18 用户：没听到打击声）；换了个键名，旧版存下的「关」不再生效。
  // 每首歌的手动偏移也换了键名（songOffsets）：旧的是在补以前算错的偏移和没算进去的画面校准，留着会补过头
  const PREFS_KEY = 'rhythmhint.editor.prefs';
  const prefs = Object.assign({
    language: '', beats: true, wave: true, gaps: true, ghost: true, track: true, standard: true, alternate: true,
    jackMs: 150, volume: 50, refScale: 50, noteStyle: null, hitsoundOn: true, pxPerSec: 240, speed: 1, songOffsets: {}, laneKeys: null, laneKeysByCount: null, autoNext: true, wayBackTold: false, trackZoom: 1, trackBig: false,
  }, readJson(PREFS_KEY) || {});
  function readJson(key) {
    try { return JSON.parse(localStorage.getItem(key) || 'null'); } catch (e) { return null; } // 隐私模式下 localStorage 不可用：当作没有
  }
  /** 存进 localStorage；存不了（写满了、隐私模式）返回 false，要不要告诉玩家由调用的地方决定。 */
  function writeText(key, text) {
    try {
      localStorage.setItem(key, text);
      return true;
    } catch (e) {
      console.warn('RhythmHint: cannot write ' + key, e);
      return false;
    }
  }
  const writeJson = (key, value) => writeText(key, JSON.stringify(value));
  function removeKey(key) {
    try { localStorage.removeItem(key); } catch (e) { console.warn('RhythmHint: cannot remove ' + key, e); } // 删不掉只是多占点地方
  }
  const savePrefs = () => writeJson(PREFS_KEY, prefs); // 偏好存不了不影响编辑，不提示（草稿存不了才提示）

  // ---------- 状态 ----------
  const session = window.RHYTHMHINT_SESSION;
  const state = {
    doc: null, history: null, t: null, source: '', gameSession: null, // gameSession：从游戏（或历史）打开的会话，切换键数时从它重新打开
    time: 0, pxPerSec: prefs.pxPerSec, playing: false,
    selection: new Set(), hover: null, preview: null, jacks: new Set(),
    show: { beats: prefs.beats, wave: prefs.wave, gaps: prefs.gaps, ghost: prefs.ghost, standard: prefs.standard, alternate: prefs.alternate },
    loop: { a: null, b: null, on: false }, flash: [], press: [], round: false, box: null,
    leftInset: 0, rightInset: 0, waveform: null, audioOffset: 0, hitsound: prefs.hitsoundOn, lastTime: 0, refScale: prefs.refScale / 100,
    timeTarget: null, zoomTarget: null, zoomAnchor: null, anims: new Map(), now: 0, seekWarned: false,
  };

  const canvas = $('chart');
  const view = new window.ChartView(canvas);
  const track = new window.TrackView($('track'));
  const player = new window.EditorPlayer($('audio'));
  player.setVolume(prefs.volume / 100);
  player.setRate(prefs.speed);
  player.onSeekFail = () => {
    if (state.seekWarned) return;
    state.seekWarned = true;
    toast(state.t('audio.noSeek'), 'warn', null, 10000);
  };
  $('speed').value = String(prefs.speed);

  // ---------- 几个文件都要读写、会重新赋值的状态 ----------
  // 都放在 App 上，别的文件一律写 App.xxx 读写，不留副本
  App.openSeq = 0; // 每打开一次加一：还在载入的旧请求回来时认得出自己过时了（历史里快速连点两关）
  App.openPanel = null; // 左边开着的面板（file、history……），没开为 null
  App.metaDirty = false; // 作者、说明、文件名改了还没存进游戏
  App.metaEdits = 0; // 改了几次作者、说明、文件名（保存途中又改了，存完照样算没保存）

  // ---------- 语言 ----------
  function pickLanguage() {
    if (prefs.language) return prefs.language;
    if (session && session.language) return session.language;
    return I18N.fromBrowser(navigator.language);
  }

  function applyLanguage(code) {
    state.t = I18N.create(code);
    document.documentElement.lang = state.t.language;
    document.querySelectorAll('[data-i18n]').forEach(el => { el.textContent = state.t(el.dataset.i18n); });
    document.querySelectorAll('[data-i18n-title]').forEach(el => { el.title = state.t(el.dataset.i18nTitle); });
    document.querySelectorAll('[data-i18n-placeholder]').forEach(el => { el.placeholder = state.t(el.dataset.i18nPlaceholder); });
    document.title = 'RhythmHint · ' + state.t('brand.sub');
    App.renderShortcuts();
    App.renderKeymap();
    App.renderHeader();
    App.renderInspector();
    App.renderFilePanel();
    App.renderAudioName();
    if (App.openPanel === 'history') App.renderHistory();
    if (App.openPanel) $('drawerTitle').textContent = state.t('rail.' + App.openPanel);
    App.trackSizeTitle();
    // 版本号：游戏导出最近一关时写进 session.js 的 Mod 版本（和 F8 里显示的一样）；不开游戏、没有会话时不显示
    const version = session && typeof session.modVersion === 'string' ? session.modVersion : '';
    $('aboutVersion').textContent = version ? state.t('about.version', version) : '';
    window.EditorDropdown.syncAll();
  }

  const langSelect = $('langSelect');
  I18N.LANGUAGES.forEach(([code, name]) => langSelect.add(new Option(name, code)));
  // 下拉菜单用编辑器自己的样式（dropdown.js；原来的 select 留着，change 事件照旧）
  window.EditorDropdown.enhance(langSelect);
  window.EditorDropdown.enhance($('vStyle'));
  window.EditorDropdown.enhance($('speed'));
  langSelect.addEventListener('change', () => { prefs.language = langSelect.value; savePrefs(); applyLanguage(langSelect.value); });

  // ---------- 提示条 ----------
  /** 提示条。actions：[[按钮文字, 点了做什么, 是不是主按钮]]；onClose：提示条关掉时（点了按钮或到时间）。返回关掉它的函数。 */
  function toast(text, kind, actions, ms, onClose) {
    const el = document.createElement('div');
    el.className = 'toast ' + (kind || '');
    const span = document.createElement('span');
    span.textContent = text;
    el.appendChild(span);
    (actions || []).forEach(([label, fn, primary]) => {
      const b = document.createElement('button');
      b.className = 'btn' + (primary ? ' primary' : '');
      b.textContent = label;
      b.addEventListener('click', () => { close(); fn(); });
      el.appendChild(b);
    });
    $('toasts').appendChild(el);
    let timer = setTimeout(close, ms || (actions && actions.length ? 15000 : 4200)); // 有意的延时：提示条显示几秒后自己消失
    let closed = false;
    function close() {
      clearTimeout(timer);
      if (closed) return;
      closed = true;
      if (onClose) onClose();
      el.classList.add('leave');
      el.addEventListener('animationend', () => el.remove(), { once: true });
    }
    return close;
  }

  function errorText(e) {
    if (e instanceof M.FingeringError) {
      const key = 'err.' + e.code;
      return I18N.STRINGS[key] ? state.t(key, e.detail) : e.message;
    }
    return e && e.message ? e.message : String(e);
  }

  // ---------- 小工具 ----------
  /** 重新播放一个 CSS 动画（同一个类连续加两次不会再动）。 */
  function restart(el, cls) {
    if (reduceMotion) return;
    el.classList.remove(cls);
    void el.offsetWidth;
    el.classList.add(cls);
  }

  function fmt(t) {
    const sign = t < 0 ? '-' : '';
    t = Math.abs(t);
    const m = Math.floor(t / 60), s = t - m * 60;
    return sign + m + ':' + s.toFixed(3).padStart(6, '0');
  }

  function copy(text, button) {
    navigator.clipboard.writeText(text).then(() => toast(state.t('file.copied'), 'ok', null, 1600), () => {});
    if (button) button.blur();
  }

  Object.assign(App, { M, I18N, reduceMotion, $, esc, prefs, readJson, writeText, removeKey, savePrefs, session, state, canvas, view, track, player, pickLanguage, applyLanguage, langSelect, toast, errorText, restart, fmt, copy });
})(window.EditorApp = {});
