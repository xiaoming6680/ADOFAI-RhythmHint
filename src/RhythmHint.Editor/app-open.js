/* 打开和音乐：打开游戏导出的关卡、选的或拖进来的手序文件（都走 openDoc / loadDoc），载入这一关的音乐、整首解码、算波形，音量和打拍声开关。 */
(function (App) {
  'use strict';

  const { M, $, prefs, savePrefs, state, view, track, player, toast, errorText } = App;

  // ---------- 打开 ----------
  /** 打开一份：make 每次调用都重新读出一份（另一个标签页改了这一关时用它重新打开）。读不出来时抛出，原来的照旧。 */
  function openDoc(make, source) {
    const doc = make();
    state.make = make;
    loadDoc(doc, source);
    return doc;
  }

  function loadDoc(doc, source) {
    App.openSeq++;
    player.pause();
    state.doc = doc;
    state.history = new M.History(doc);
    state.source = source;
    App.resetView();
    App.applyNoteStyle();
    M.remember(doc); // 打开文件时文件名是在读出来之后才换的
    // 草稿按「历史」里的 Id 存；打开的是手序文件时按谱面签名加文件内容（两份不同的文件各有各的草稿）
    doc.draftKey = App.DRAFT_PREFIX + (doc.sessionId || M.fileDraftId(doc));
    state.time = doc.notes.length ? doc.notes[0].time - 1.2 : 0;
    view.setDoc(doc);
    track.setDoc(doc);
    $('empty').hidden = true;
    $('trackCard').hidden = !prefs.track || doc.path.length < 2; // 没有轨道形状（打开的是文件或离线数据）就不显示空卡片
    state.waveform = doc.audio && doc.audio.peaks.length ? { peaks: doc.audio.peaks, perSecond: doc.audio.peaksPerSecond } : null;
    const extra = prefs.songOffsets[doc.signature] || 0;
    $('offset').value = String(extra);
    $('offsetOut').textContent = String(extra);
    manualAudio = '';
    audioSeq++; // 上一关还在载入、解码的音乐（包括手动选的）回来时不再用
    player.hasAudio = false;
    player.setRate(prefs.speed, doc.audio?.pitch ?? 1);
    player.baseOffset = doc.audio ? doc.audio.offset : 0;
    player.calibration = doc.audio ? doc.audio.calibration : 0;
    player.userOffsetMs = extra;
    player.seek(state.time);
    state.audioOffset = player.offset;
    if (doc.fromGame) player.setHitSound(doc.hitSound); // 打开的是手序文件时没有打击声，留着上一关的
    player.load(doc.audio && doc.audio.file ? doc.audio.file + '?v=' + Date.now() : '')
      .then(renderAudioName)
      .catch(e => { renderAudioName(); toast(state.t('audio.failed', e.message), 'error'); });
    if (doc.audio && doc.audio.script) loadDecoded(doc);
    App.renderKeymap();
    App.renderLoop();
    App.renderPlayButton();
    App.edited(true);
    App.renderFilePanel(); // 文件面板、参考面板（有没有自动排键参考）跟着这一关（原来只在开着文件面板时更新，参考面板的卡片一直藏着）
    App.restoreDraft();
    if (App.openPanel === 'history') App.renderHistory();
    App.tellClashes();
  }

  /**
   * 打开游戏给的会话（session.js 或历史里的一关）。keys：换成会话里带的另一种键数（左边「文件」里切换，2026-11-22）；
   * 记下会话，切换键数时从它重新打开。读不出来时抛出，原来的照旧。
   */
  function openSession(s, keys) {
    const previous = state.gameSession;
    state.gameSession = s; // 先记下：打开时画的文件面板要知道能不能切换
    try {
      return openDoc(() => M.fromSession(s, keys), 'game');
    } catch (e) {
      state.gameSession = previous;
      throw e;
    }
  }

  function openFromText(text, name) {
    try {
      const doc = openDoc(() => {
        const d = M.fromFileText(text);
        d.fileName = name || d.fileName;
        return d;
      }, 'file');
      state.gameSession = null; // 手序文件的键数是固定的，不能切换
      App.renderFilePanel();
      toast(state.t('toast.opened', doc.opened.fileName), 'ok'); // 打开的那个文件（草稿里记的文件名可能不一样）
    } catch (e) {
      toast(state.t('toast.openFailed', errorText(e)), 'error');
    }
  }

  /** 打开选的或拖进来的手序文件：和 Mod 一样超过 20 MB 不读；读不出来告诉玩家。 */
  async function openFile(file) {
    const limit = M.MAX_FILE_BYTES / 1024 / 1024;
    if (file.size > M.MAX_FILE_BYTES) { toast(state.t('toast.tooBig', file.name, limit), 'error', null, 8000); return; }
    let text;
    try {
      text = await file.text();
    } catch (e) {
      toast(state.t('toast.openFailed', errorText(e)), 'error');
      return;
    }
    openFromText(text, file.name);
  }

  function askOpen() {
    if (state.history && state.history.dirty && !window.confirm(state.t('toast.confirmOpen'))) return;
    $('openInput').click();
  }
  $('openInput').addEventListener('change', e => {
    const file = e.target.files[0];
    e.target.value = '';
    if (file) openFile(file);
  });
  document.querySelectorAll('[data-action="open"]').forEach(b => b.addEventListener('click', askOpen));

  // 拖进来：手序文件就打开，音乐就载入
  window.addEventListener('dragover', e => e.preventDefault());
  window.addEventListener('drop', e => {
    e.preventDefault();
    const file = e.dataTransfer.files[0];
    if (!file) return;
    if (/\.json$/i.test(file.name)) {
      if (state.history && state.history.dirty && !window.confirm(state.t('toast.confirmOpen'))) return;
      openFile(file);
    } else if (file.type.startsWith('audio/') || /\.(ogg|mp3|wav|flac|m4a)$/i.test(file.name)) loadAudioFile(file);
  });

  // ---------- 音乐 ----------
  let manualAudio = '';
  // 每换一次音乐（打开关卡、手动选歌）加一：还在载入、解码的旧音乐回来时认得出自己过时了
  // （2026-11-19 评审：刚打开关卡时拖入一首歌，关卡的歌先解完就把手动那首换掉了，没有提示）
  let audioSeq = 0;

  // 整首解码一次只解一首（一首 5 分钟立体声解出来约 110 MB 采样）：正在解的时候换了关卡，新关卡排队等它解完；
  // 排队时又换了，还没开始的直接丢掉。正在解的没法中途停下，解完的结果作废、不留引用（2026-11-19 评审：连点几关几首同时解码）
  let decodeBusy = false, decodeNext = null;
  /**
   * Mod 另存的歌曲数据（file:// 下读不了音乐文件的字节，只能用 <script> 载入）：整首解码后换给播放器，跳转才准。
   * 解码好之前先用 <audio> 播；旧版本导出的历史没有这份数据、解码失败时一直用 <audio>。
   */
  function loadDecoded(doc) {
    decodeNext = { doc, seq: audioSeq };
    if (!decodeBusy) runDecode();
  }
  async function runDecode() {
    decodeBusy = true;
    while (decodeNext) {
      const job = decodeNext;
      decodeNext = null;
      const current = () => state.doc === job.doc && audioSeq === job.seq; // 还是这一关、也没手动换过音乐
      if (!current()) continue;
      try {
        const buffer = await decodeSong(job.doc, current);
        if (buffer && current()) { // 过时的结果直接丢掉
          player.useBuffer(buffer);
          if (!state.waveform) startWaveform(buffer); // 自制关卡游戏里读不到采样，这里补上波形
          renderAudioName();
        }
      } catch (e) {
        console.warn('RhythmHint: decoding the song failed, keeping <audio>', e);
      }
    }
    decodeBusy = false;
  }
  /** 载入这一关的歌曲脚本、整首解码；脚本不在了、载入期间换了关卡为 null。 */
  function decodeSong(doc, current) {
    return new Promise((resolve, reject) => {
      const key = doc.audio.file, tag = document.createElement('script');
      tag.src = doc.audio.script + '?v=' + Date.now();
      tag.onload = () => {
        tag.remove();
        const all = window.RHYTHMHINT_AUDIO || {};
        let text = all[key];
        delete all[key]; // 字节交给解码后就不再留着这份文本
        if (typeof text !== 'string' || !current()) { resolve(null); return; }
        try {
          let bytes = window.EditorPlayer.bytesFromBase64(text);
          text = null; // 换成字节后不再留着几十 MB 的文本，解码期间就能回收
          const decoding = window.EditorPlayer.decode(bytes, player.ctx && player.ctx.sampleRate);
          bytes = null;
          decoding.then(resolve, reject);
        } catch (e) {
          reject(e);
        }
      };
      tag.onerror = () => { tag.remove(); resolve(null); }; // 文件不在了：继续用 <audio>
      document.head.appendChild(tag);
    });
  }
  async function loadAudioFile(file) {
    const seq = ++audioSeq; // 之后关卡自己的歌解完也不再换上去
    try {
      const url = URL.createObjectURL(file);
      if (!await player.load(url) || seq !== audioSeq) return; // 载入期间又换了音乐（换关卡、又选了一首）
      manualAudio = file.name;
      state.audioOffset = player.offset;
      renderAudioName();
      const buffer = await window.EditorPlayer.decode(new Uint8Array(await file.arrayBuffer()), player.ctx && player.ctx.sampleRate);
      if (seq !== audioSeq) return; // 解码期间又换了音乐
      player.useBuffer(buffer);
      startWaveform(buffer);
    } catch (e) {
      if (seq === audioSeq) toast(state.t('audio.failed', errorText(e)), 'error');
    }
  }
  // 解码好的音乐分段算波形，每帧算一点、算好的先画（整首一次算完时长歌会让画面冻住零点几秒、打拍声漏掉）
  const WAVE_MS = 3; // 每帧最多花在算波形上的毫秒数
  let waveJob = null;
  function startWaveform(buffer) {
    waveJob = window.EditorPlayer.waveform(buffer, 100);
    state.waveform = waveJob;
  }
  function stepWaveform() {
    if (!waveJob) return;
    // 换了关卡或音乐（波形换成了别的）就不用再算
    if (state.waveform !== waveJob || waveJob.step(performance.now() + WAVE_MS)) waveJob = null;
  }
  $('loadAudio').addEventListener('click', () => $('audioInput').click());
  $('audioInput').addEventListener('change', e => { const f = e.target.files[0]; e.target.value = ''; if (f) loadAudioFile(f); });
  function renderAudioName() {
    $('audioName').textContent = !player.hasAudio ? state.t('audio.none') : manualAudio || state.t('audio.fromGame');
    const cal = player.hasAudio ? Math.round(player.calibration * 1000) : 0;
    $('calibrationNote').hidden = cal === 0;
    $('calibrationNote').textContent = cal === 0 ? '' : state.t('audio.calibrated', cal);
  }
  $('offset').addEventListener('input', e => {
    const v = Number(e.target.value);
    $('offsetOut').textContent = String(v);
    player.userOffsetMs = v;
    state.audioOffset = player.offset;
    if (state.doc) { prefs.songOffsets[state.doc.signature] = v; savePrefs(); }
    if (player.playing) player.cancelClicks();
    else player.seek(state.time);
  });
  // 音量：音乐抽屉里和底部播放条上各一个，两边同步
  function setVolume(v) {
    prefs.volume = v;
    $('volume').value = String(v);
    $('volumeQuick').value = String(v);
    $('volumeOut').textContent = String(v);
    player.setVolume(v / 100);
    savePrefs();
  }
  $('volume').addEventListener('input', e => setVolume(Number(e.target.value)));
  $('volumeQuick').addEventListener('input', e => setVolume(Number(e.target.value)));
  setVolume(prefs.volume);
  function setHitsound(on) {
    player.cancelClicks();
    state.hitsound = on;
    prefs.hitsoundOn = on;
    savePrefs();
    $('hitsound').checked = on;
    $('hitBtn').classList.toggle('on', on);
  }
  $('hitsound').addEventListener('change', e => setHitsound(e.target.checked));
  $('hitBtn').addEventListener('click', () => setHitsound(!state.hitsound));
  setHitsound(state.hitsound);

  Object.assign(App, { openDoc, openSession, askOpen, stepWaveform, renderAudioName });
})(window.EditorApp);
