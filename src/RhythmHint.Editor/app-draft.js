/* 历史和草稿：左边「历史」里游戏最近导出的几关；改到一半的进度存在浏览器里（按关卡），换关卡、关网页、别的标签页开着同一关时都不丢。 */
(function (App) {
  'use strict';

  const { M, $, esc, readJson, writeText, removeKey, state, toast, errorText, copy } = App;

  // ---------- 历史 ----------
  // 2026-11-18 用户：很多人一次改不完整张谱就把网页关了，不开游戏很难再找到网页和之前改的东西。
  // Mod 每次「编辑本关手序」把那一关另存到 recent/（SessionLibrary，最多 10 关），这里列出来，点一下带着音乐、轨道预览接着改；
  // 改到一半的音符在草稿里（下面），换关卡、关网页都不丢。
  const recent = M.parseRecent(window.RHYTHMHINT_RECENT);
  const DATE_FORMAT = { month: 'numeric', day: 'numeric', hour: '2-digit', minute: '2-digit' };
  const finite = v => typeof v === 'number' && Number.isFinite(v);
  const whenText = ms => new Date(ms).toLocaleString(state.t.language, DATE_FORMAT);

  /** 一关现在的状态和最后改动的时间：正在编辑的看编辑器里的，别的看浏览器里存的草稿。 */
  function progressOf(item) {
    const doc = state.doc;
    if (doc && doc.sessionId === item.id) {
      const unsaved = state.history.dirty || App.metaDirty, changed = M.stats(doc).changed;
      const draft = readJson(DRAFT_PREFIX + item.id);
      return { unsaved: unsaved ? Math.max(1, changed) : 0, saved: !unsaved && changed > 0, when: draft && finite(draft.time) ? draft.time : item.exported };
    }
    const draft = readJson(DRAFT_PREFIX + item.id);
    if (!draft || !Array.isArray(draft.lanes)) return { unsaved: 0, saved: false, when: item.exported };
    return { unsaved: draft.saved ? 0 : Math.max(1, draft.changed || 0), saved: !!draft.saved, when: finite(draft.time) ? draft.time : item.exported };
  }

  function renderHistory() {
    const list = $('historyList');
    $('copyPage').title = pagePath(); // 悬停看完整路径
    if (!recent.length) {
      list.innerHTML = '<p class="muted">' + esc(state.t('history.empty')) + '</p>';
      return;
    }
    list.innerHTML = recent.map((item, i) => {
      const current = state.doc && state.doc.sessionId === item.id;
      const p = progressOf(item);
      const badge = p.unsaved ? '<span class="state unsaved">' + esc(state.t('history.unsaved', p.unsaved)) + '</span>'
        : p.saved ? '<span class="state saved">' + esc(state.t('history.saved')) + '</span>' : '';
      const sub = M.plain(item.artist) + (item.keys !== 4 ? (item.artist ? ' · ' : '') + item.keys + 'K' : '');
      return '<button class="history-item' + (current ? ' current' : '') + '" data-index="' + i + '"' + (current ? ' aria-current="true"' : '') + '>'
        + '<span class="row1"><span class="title">' + esc(M.plain(item.song) || item.fileName) + '</span>' + badge + '</span>'
        + '<span class="row2"><span class="sub">' + esc(sub) + '</span><span class="when">' + esc(whenText(p.when)) + '</span></span>'
        + '</button>';
    }).join('');
  }

  /** 这个网页在电脑上的位置（不开游戏时从这里打开）。 */
  function pagePath() {
    try {
      const path = decodeURIComponent(location.pathname);
      return location.protocol === 'file:' ? path.replace(/^\/([A-Za-z]:)/, '$1').replace(/\//g, '\\') : location.href;
    } catch (e) {
      return location.href; // 地址里有解不开的 %：原样给出
    }
  }

  /** 载入 recent/ 里的一关（file:// 下只能用 <script> 读本地文件）。 */
  function loadRecent(item) {
    return new Promise((resolve, reject) => {
      window.RHYTHMHINT_SESSION = undefined;
      const el = document.createElement('script');
      el.src = 'recent/' + item.session + '?v=' + Date.now();
      el.onload = () => { el.remove(); window.RHYTHMHINT_SESSION ? resolve(window.RHYTHMHINT_SESSION) : reject(new Error(item.session)); };
      el.onerror = () => { el.remove(); reject(new Error(item.session)); };
      document.head.appendChild(el);
    });
  }

  $('historyList').addEventListener('click', async e => {
    const button = e.target.closest('button[data-index]');
    const item = button && recent[Number(button.dataset.index)];
    if (!item) return;
    // 只认最后一次点击（2026-11-19 评审：快速连点两关时后点的先载完，最后显示的却是先点的那关）
    const seq = ++App.openSeq;
    if (state.doc && state.doc.sessionId === item.id) return;
    if (draftTimer) writeDraft(); // 这一关刚改的先存进草稿，换过去再换回来还在
    let s;
    try {
      s = await loadRecent(item);
    } catch (err) {
      if (seq === App.openSeq) toast(state.t('history.missing'), 'error', null, 8000);
      return;
    }
    if (seq !== App.openSeq) return;
    try {
      App.openSession(s);
    } catch (err) {
      toast(state.t('toast.openFailed', errorText(err)), 'error');
    }
  });
  $('copyPage').addEventListener('click', e => copy(pagePath(), e.currentTarget));

  // ---------- 草稿 ----------
  // 这一关在编辑器里现在的样子，随时存在这台电脑的浏览器里（按「历史」里的 Id；打开的是手序文件时按谱面签名加文件内容，见 loadDoc）。
  // 关掉网页、换到别的关卡都不丢，下次打开这一关自动接着改；保存到游戏后也留着（记成已保存）。
  // 草稿记着是在哪份手序上改的（M.draftMatches）：游戏换了一份手序、打开了别人分享的文件时不自动套，先问玩家（2026-11-19 第二轮评审：
  // 原来只按谱面认，打开朋友的手序显示的是自己的旧键位，文件名、作者也变回自己的）
  const DRAFT_PREFIX = 'rhythmhint.editor.draft.';
  const MAX_DRAFTS = 30; // 浏览器里最多留几关的草稿（2026-11-19 评审：原来从不删，越攒越多，写满了就存不进去）
  const draftKeyOf = doc => doc.draftKey || DRAFT_PREFIX + (doc.sessionId || doc.signature);
  let draftTimer = 0;
  let draftBlocked = false; // 另一个标签页改了这一关的草稿：这一页先不写，等玩家选用哪一边
  let otherTabToast = null, askToast = null, storageFullTold = false; // askToast：正在问要不要载入上次的进度（askDraft）

  /**
   * 把现在的样子写进草稿，写好了（或者不用记、删掉了）为 true。time：草稿里记的最后改动时间（默认现在）；
   * quiet：别的标签页删了这一关的草稿、这里重写时，写不下也不删别的关没保存的进度（两个标签页会轮流删对方的）。
   */
  function writeDraft(time, quiet) {
    clearTimeout(draftTimer);
    draftTimer = 0;
    const doc = state.doc;
    if (!doc) return false;
    if (draftBlocked) { warnOtherTab(); return false; }
    const key = draftKeyOf(doc);
    const draft = M.draftOf(doc, !(state.history.dirty || App.metaDirty), time);
    let ok = true;
    if (draft.saved && M.untouched(doc)) removeKey(key); // 和打开时一样、也没有要保存的：不用记
    // 存不进去（写满了）：腾了地方还不行就告诉玩家一次（原来空 catch 吞掉，界面还说已自动保留）
    else if (!(ok = storeDraft(key, JSON.stringify(draft), !quiet))) tellStorageFull();
    if (App.openPanel === 'history') renderHistory();
    return ok;
  }
  function scheduleDraft() {
    clearTimeout(draftTimer);
    draftTimer = 0;
    if (draftBlocked) { warnOtherTab(); return; }
    draftTimer = setTimeout(() => writeDraft(), 600); // 有意的延时：连续改动时少写几次
  }

  function tellStorageFull() {
    if (storageFullTold) return;
    storageFullTold = true;
    toast(state.t('draft.storageFull'), 'error', null, 15000);
  }

  /**
   * 存一份草稿；写满了先删别的关已保存的草稿（游戏里有文件），还不够、dropUnsaved 时再从最久没改的删别的关没保存的，
   * 每删一份试一次，删了几关告诉玩家（2026-11-19 评审：原来只删已保存的，别的关没保存的旧草稿一直占着，这一关存不进去）。
   * 正在编辑的这一关不删。腾完还是存不进去为 false。
   */
  function storeDraft(key, text, dropUnsaved) {
    if (writeText(key, text)) return true;
    if (pruneDrafts(true) && writeText(key, text)) return true;
    if (!dropUnsaved) return false;
    let dropped = 0;
    for (const old of M.draftsToDrop(listDrafts(), 0, key)) {
      removeKey(old);
      dropped++;
      if (writeText(key, text)) {
        toast(state.t('draft.dropped', dropped), 'warn', null, 15000);
        return true;
      }
    }
    return false;
  }

  /** 浏览器里各关的草稿 [{ key, saved, time }]（读不出来的按没保存、很久以前算）。 */
  function listDrafts() {
    const list = [];
    try {
      for (let i = 0; i < localStorage.length; i++) {
        const key = localStorage.key(i);
        if (!key || !key.startsWith(DRAFT_PREFIX)) continue;
        const d = readJson(key);
        list.push({ key, saved: !!(d && d.saved === true), time: d && typeof d.time === 'number' ? d.time : 0 });
      }
    } catch (e) {
      console.warn('RhythmHint: cannot list drafts', e); // localStorage 用不了：也就没有草稿可删
    }
    return list;
  }

  /** 草稿只留最近的 MAX_DRAFTS 关（先删已保存的）；onlySaved：存不进去时把已保存的都删掉。正在编辑的不删。返回删了没有。 */
  function pruneDrafts(onlySaved) {
    const list = listDrafts(), protect = state.doc ? draftKeyOf(state.doc) : '';
    const drop = onlySaved ? list.filter(d => d.saved && d.key !== protect).map(d => d.key) : M.draftsToDrop(list, MAX_DRAFTS, protect);
    drop.forEach(removeKey);
    return drop.length > 0;
  }

  // 多个标签页开着同一关时，各自拿着旧的样子写草稿会互相覆盖（2026-11-19 评审）。storage 事件只在别的标签页改了才来：
  // 来了就先不写，让玩家选载入那边的进度，还是以这一页为准（写一次，那边也会收到同样的提示）
  window.addEventListener('storage', e => {
    if (!e.key || !e.key.startsWith(DRAFT_PREFIX)) return;
    if (App.openPanel === 'history') renderHistory();
    if (!state.doc || e.key !== draftKeyOf(state.doc)) return;
    if (e.newValue === null) {
      // 别的标签页删了这一关的草稿（启动时清理旧草稿、在那边点了「放弃」）：不是改了进度，不用问，按本页现状重写一份
      // （2026-11-19 评审：原来照样提示「另一个标签页改了这一关」，点「载入另一页的进度」后本页的改动全丢）
      draftBlocked = false;
      if (otherTabToast) otherTabToast();
      writeDraft(undefined, true);
      return;
    }
    draftBlocked = true;
    clearTimeout(draftTimer);
    draftTimer = 0;
    warnOtherTab();
  });
  function warnOtherTab() {
    if (otherTabToast) return;
    otherTabToast = toast(state.t('draft.otherTab'), 'warn', [
      [state.t('draft.useOther'), reopenDoc, true],
      [state.t('draft.keepMine'), () => { allowDraft(); writeDraft(); }],
    ], 60000, () => { otherTabToast = null; });
  }
  /** 以这一页为准（换了一关、刚存进游戏、选了「以本页为准」）：不再等别的标签页，也不再问要不要载入上次的进度。 */
  function allowDraft() {
    draftBlocked = false;
    if (otherTabToast) otherTabToast();
    if (askToast) askToast();
  }
  /** 重新打开这一关：套上另一页存的草稿。 */
  function reopenDoc() {
    if (!state.make) return;
    try { App.openDoc(state.make, state.source); } catch (e) { toast(state.t('toast.openFailed', errorText(e)), 'error'); }
  }

  /**
   * 这一关的草稿；打开的是手序文件、新的名字下没有时，看看这一版以前的编辑器按谱面签名存的那份（legacyKey，两份文件共用过它）。
   * 游戏的关卡只按 Id 读：早期测试版按签名存过游戏关卡的草稿，没发布过，不再回头读（2026-11-19 评审：
   * 回头读会把打开文件时留下的草稿套到游戏的这一关上，「放弃」又只删 Id 那一份，下次打开再冒出来）。
   */
  function readDraft(doc) {
    const draft = readJson(draftKeyOf(doc));
    if (draft || doc.sessionId) return { draft, legacyKey: '' };
    const key = DRAFT_PREFIX + doc.signature, old = readJson(key);
    return old && !old.session ? { draft: old, legacyKey: key } : { draft: null, legacyKey: '' };
  }
  const metaDiffers = (doc, meta) => meta.author !== doc.opened.author || meta.comment !== doc.opened.comment || meta.fileName !== doc.opened.fileName;

  /** 打开一关时套上上次的进度（可撤销）：是在这份手序上改的直接套上，不是的先问；「放弃」回到游戏这次给的样子。 */
  function restoreDraft() {
    const doc = state.doc;
    const { draft, legacyKey } = readDraft(doc);
    const change = draft && M.draftChange(doc, draft);
    if (!change) return;
    if (M.draftMatches(doc, draft)) { applyDraft(doc, draft, change, legacyKey, !!legacyKey); return; }
    // 游戏换了一份手序、打开的是别人的文件，或者旧版的草稿认不出来：和这次打开的一样就不用问
    if (!change.size && !metaDiffers(doc, M.draftMeta(doc, draft))) return;
    askDraft(doc, draft, legacyKey);
  }

  /**
   * 问玩家要不要载入不是在这份手序上改的进度。提示条开着时都能载入（载入的是打开时读到的那份）；没回答就接着改的照常写草稿，
   * 不为了留着那份进度而不存新改的（换关卡、关网页时新改的会丢）。到时间收起来、也没接着改：那份进度留着，下次打开再问。
   */
  function askDraft(doc, draft, legacyKey) {
    askToast = toast(state.t('draft.found'), 'warn', [
      [state.t('draft.load'), () => {
        if (state.doc !== doc) return;
        const change = M.draftChange(doc, draft);
        if (change) applyDraft(doc, draft, change, legacyKey, true);
      }, true],
      [state.t('draft.skip'), () => { if (state.doc === doc) writeDraft(); }], // 以这次打开的为准：没改过就删掉那份进度，改过了就换成这一页的
    ], 60000, () => { askToast = null; });
  }

  /**
   * 套上草稿（可撤销）。rebase：玩家选了载入、或接上的是旧版编辑器的草稿：马上按这次打开的重写一份（时间照旧），
   * 下次打开同样的这一份直接接上；旧版按谱面签名存的那份删掉。
   */
  function applyDraft(doc, draft, change, legacyKey, rebase) {
    const opened = Object.assign({}, doc.opened), meta = M.draftMeta(doc, draft);
    const metaChanged = metaDiffers(doc, meta);
    const confirmed = M.draftConfirmed(doc, draft);
    confirmed.forEach(id => { doc.notes[id].confirmed = true; }); // 看过的待检查照样算看过
    doc.savedBase = typeof draft.savedBase === 'string' ? draft.savedBase : ''; // 上次存进游戏的那份：游戏下次用它导出时也认得
    if (change.missed) toast(state.t('draft.missed', change.missed), 'error', null, 15000);
    // 旧版的那份在新的名字下写好了才删（存储写满时留着）
    const finish = () => { if (rebase && writeDraft(draft.time) && legacyKey) removeKey(legacyKey); };
    if (!change.size && !metaChanged) { if (confirmed.length) App.edited(true); finish(); return; }
    const before = new Map([...change.keys()].map(id => [id, doc.notes[id].lanes]));
    if (change.size) state.history.apply(change);
    setMeta(doc, meta); // 文件名也沿用（2026-11-19 评审：从历史接着改时存成默认名，同一关两份文件，游戏认不出用哪份）
    const saved = draft.saved && !change.missed; // 有没接上的改动时，和保存过的文件已经不一样
    if (saved) state.history.markSaved(); // 和保存到游戏的文件一样：不算没保存
    App.metaDirty = !saved && metaChanged;
    App.edited(true); // 不重写草稿：草稿里记的是上次改动的时间
    App.renderFilePanel();
    finish();
    toast(state.t(saved ? 'draft.restoredSaved' : 'draft.restored', change.size), 'ok', [
      [state.t('draft.discard'), () => discardDraft(doc, change, before, opened, confirmed)],
    ], 12000);
  }

  function setMeta(doc, meta) {
    doc.meta.author = meta.author;
    doc.meta.comment = meta.comment;
    doc.fileName = meta.fileName;
  }

  /**
   * 「放弃」恢复的进度：恢复时改过、之后没再动过的音符改回去，作者、说明、文件名回到打开时的，看过的待检查不再算看过。
   * 和打开时一样了就算已保存、删掉草稿；恢复后又改了别处的，那些改动留着、照旧没保存，草稿照现状写
   * （2026-11-19 评审：原来无条件记成已保存、删掉草稿，那处改动关网页时不提醒、丢了）。
   */
  function discardDraft(doc, restored, before, opened, confirmed) {
    if (state.doc !== doc) return;
    const back = M.undoRestore(doc, restored, before);
    if (back.size) state.history.apply(back);
    setMeta(doc, opened);
    confirmed.forEach(id => { doc.notes[id].confirmed = false; });
    doc.savedBase = '';
    const clean = M.untouched(doc);
    if (clean) state.history.markSaved();
    App.metaDirty = false;
    App.edited(true);
    if (clean) removeKey(draftKeyOf(doc));
    else writeDraft();
    App.renderFilePanel();
    if (App.openPanel === 'history') renderHistory();
  }

  window.addEventListener('beforeunload', e => {
    if (draftTimer) writeDraft(); // 刚改的还没存进草稿：关网页前存上
    if (state.history && (state.history.dirty || App.metaDirty)) { e.preventDefault(); e.returnValue = ''; }
  });

  Object.assign(App, { recent, renderHistory, DRAFT_PREFIX, writeDraft, scheduleDraft, pruneDrafts, allowDraft, restoreDraft });
})(window.EditorApp);
