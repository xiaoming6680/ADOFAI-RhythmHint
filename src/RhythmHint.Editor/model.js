/* 手序编辑器的数据和编辑操作（不碰界面，Node 能直接测）。
 * 文件格式和 src/RhythmHint.Core/CustomFingering.cs 一致：
 *   hits:  [砖号, 时间, 长按, 转角或 null]
 *   notes: [第一块砖号, 最后一块砖号, [[轨道, 长按], …]]
 * 编辑只改每个音符按哪几个键，不增删音符、不改时间和长按（时间以游戏为准）。 */
(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.FingeringModel = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';

  const FILE_FORMAT = 'rhythmhint-fingering';
  const SESSION_FORMAT = 'rhythmhint-editor-session';
  const SCHEMA_VERSION = 1;
  // 和 CustomFingering 一样的上限：Mod 读不了的文件编辑器也不收，免得存出去游戏里用不上
  const MAX_FILE_BYTES = 20 * 1024 * 1024; // 打开文件时超过就不读（CustomFingering.MaxFileBytes）
  const MAX_HITS = 200000; // CustomFingering.MaxHits
  const MAX_NAME = 300, MAX_COMMENT = 4000; // CustomFingering 的 MaxNameLength、MaxCommentLength：超出的截掉
  // 手序文件里认得的顶层字段。别的字段原样留着、保存时写回（2026-11-19 评审：以后的版本加的字段不能被这一版编辑器弄丢）
  const KNOWN_FIELDS = new Set(['format', 'schemaVersion', 'keys', 'song', 'artist', 'charter', 'author', 'comment', 'leftHanded', 'charts', 'updatedAt', 'hits', 'notes']);

  class FingeringError extends Error {
    constructor(code, detail) {
      super(code + (detail ? ': ' + detail : ''));
      this.code = code;
      this.detail = detail || '';
    }
  }

  const finite = v => typeof v === 'number' && Number.isFinite(v);
  // 整数：JSON 里写成 1.0 的读出来就是 1，照样算（Mod 那边也放宽）
  const int = v => Number.isInteger(v);
  const int32 = v => int(v) && v >= -2147483648 && v <= 2147483647; // 砖号：Mod 读成 int
  function need(ok, code, detail) { if (!ok) throw new FingeringError(code, detail); }

  /** 按轨道排好的 [[轨道, 长按]]。 */
  const sortLanes = lanes => lanes.slice().sort((a, b) => a[0] - b[0]);
  const laneKey = lanes => sortLanes(lanes).map(l => l[0]).join('+');
  /** 按哪几个键、哪个键长按：比较改没改用它（D 长按 + K 镜像成 D + K 长按也算改了）。 */
  const noteKey = lanes => sortLanes(lanes).map(l => l[0] + ':' + l[1]).join('+');

  /** 文字字段：没有或 null 当空，不是文字整份拒绝（Mod 也拒绝，不能悄悄变成空的再存出去）。 */
  function textField(obj, name, max) {
    const v = obj[name];
    if (v === undefined || v === null) return '';
    need(typeof v === 'string', 'badFile', name);
    return v.slice(0, max);
  }

  /** 读一份手序（文件里的 fingering 段，或单独的手序文件）。 */
  function parseFingering(obj) {
    need(obj && typeof obj === 'object' && !Array.isArray(obj), 'notFingering');
    need(obj.format === FILE_FORMAT, 'notFingering');
    need(int(obj.schemaVersion) && obj.schemaVersion >= 1, 'badFile', 'schemaVersion');
    need(obj.schemaVersion <= SCHEMA_VERSION, 'tooNew');
    const keys = obj.keys === undefined ? 4 : obj.keys;
    need(int(keys) && keys >= 1 && keys <= 16, 'badFile', 'keys');
    need(Array.isArray(obj.hits) && obj.hits.length > 0 && obj.hits.length <= MAX_HITS, 'badFile', 'hits');
    need(Array.isArray(obj.notes) && obj.notes.length > 0, 'badFile', 'notes');
    need(!obj.updatedAt || typeof obj.updatedAt !== 'object', 'badFile', 'updatedAt'); // 同 Mod：写成对象、数组整份拒绝
    const meta = {
      song: textField(obj, 'song', MAX_NAME), artist: textField(obj, 'artist', MAX_NAME), charter: textField(obj, 'charter', MAX_NAME),
      author: textField(obj, 'author', MAX_NAME), comment: textField(obj, 'comment', MAX_COMMENT), leftHanded: obj.leftHanded === true,
      // 这份手序认得的谱面指纹（C# CustomFingering.Charts）：原样带回去，Mod 保存时会加上新版的
      charts: Array.isArray(obj.charts) ? obj.charts.filter(c => typeof c === 'string' && c.length <= 100) : [],
    };
    const { hits, index } = parseHits(obj.hits);
    // 不认识的顶层字段按原来的顺序记成 [名字, 值]（不放进普通对象：名字可能是 __proto__）
    const otherFields = Object.keys(obj).filter(k => !KNOWN_FIELDS.has(k)).map(k => [k, obj[k]]);
    return { keys, meta, hits, notes: parseNotes(obj.notes, hits, index, keys), otherFields };
  }

  function parseHits(list) {
    const hits = [];
    const index = new Map();
    let lastTile = -Infinity, lastTime = -Infinity;
    list.forEach(h => {
      need(Array.isArray(h) && h.length >= 3 && int32(h[0]) && finite(h[1]) && finite(h[2]) && h[2] >= 0, 'badFile', 'hit');
      need(h[0] > lastTile && h[1] >= lastTime, 'badFile', 'hitOrder');
      const angle = h.length > 3 && h[3] !== null ? h[3] : null;
      need(angle === null || finite(angle), 'badFile', 'angle');
      index.set(h[0], hits.length);
      hits.push({ tile: h[0], time: h[1], hold: h[2], angle });
      lastTile = h[0]; lastTime = h[1];
    });
    return { hits, index };
  }

  function parseNotes(list, hits, index, keys) {
    const notes = [];
    let next = 0;
    list.forEach(n => {
      need(Array.isArray(n) && n.length === 3 && int(n[0]) && int(n[1]) && Array.isArray(n[2]), 'badFile', 'note');
      const first = index.get(n[0]), last = index.get(n[1]);
      need(first === next && last !== undefined && last >= first, 'badFile', 'coverage');
      need(n[2].length >= 1 && n[2].length <= keys, 'badFile', 'laneCount');
      const seen = new Set();
      const lanes = n[2].map(l => {
        need(Array.isArray(l) && l.length === 2 && int(l[0]) && l[0] >= 0 && l[0] < keys && !seen.has(l[0]) && finite(l[1]) && l[1] >= 0, 'badFile', 'lane');
        seen.add(l[0]);
        return [l[0], l[1]];
      });
      notes.push({ id: notes.length, first, last, tile: n[0], lastTile: n[1], time: hits[first].time, lanes: sortLanes(lanes) });
      next = last + 1;
    });
    need(next === hits.length, 'badFile', 'coverage');
    return notes;
  }

  function emptyExtras() {
    return { keyLabels: ['D', 'F', 'J', 'K'], keyChoices: [], refs: { standard: new Map(), alternate: new Map() }, beats: [], tempo: [], path: [], fx: null,
      audio: null, fileName: '', saveFolder: '', saveStartIn: '', usedFile: '', language: '', roundNotes: false, sessionId: '', hitSound: '', fromGame: false };
  }

  /**
   * 每个音符：orig 是打开时按的键；filled 是不是自动补齐的（来源，不变）；confirmed 是玩家看过、按了它现在的键；
   * auto 是还要不要检查（markReview 算，画布按它描黄色虚线）。
   */
  function finish(fingering, extras) {
    const doc = Object.assign(emptyExtras(), extras, fingering);
    doc.notes.forEach(n => { n.orig = n.lanes.map(l => l.slice()); n.filled = n.filled === true; n.confirmed = false; n.auto = n.filled; });
    if (!doc.fileName) doc.fileName = suggestedFileName(doc.meta, doc.keys);
    doc.signature = signature(doc.hits);
    remember(doc);
    return doc;
  }

  /**
   * 记下打开时的作者、说明、文件名（草稿还要不要留着、放弃时回到哪里），和打开时这份手序的指纹 base（草稿认「是在哪份手序上改的」）。
   * 打开文件后改了文件名要再记一次。
   */
  function remember(doc) {
    doc.opened = { author: doc.meta.author, comment: doc.meta.comment, fileName: doc.fileName };
    doc.base = baseOf(doc);
    doc.savedBase = '';
  }

  // ---------- 手序的指纹（草稿用） ----------
  // 2026-11-19 第二轮评审：草稿原来只按谱面认，打开朋友分享的同一张谱的手序、游戏换了一份手序时，都被套上自己的旧进度。
  // 现在草稿记着「打开时那份手序」的指纹，对得上才自动套，对不上先问玩家。

  /** 53 位的文字指纹（cyrb53 算法）：只用来认手序，不用于安全。 */
  function hashText(text) {
    let h1 = 0xdeadbeef, h2 = 0x41c6ce57;
    for (let i = 0; i < text.length; i++) {
      const ch = text.charCodeAt(i);
      h1 = Math.imul(h1 ^ ch, 2654435761);
      h2 = Math.imul(h2 ^ ch, 1597334677);
    }
    h1 = Math.imul(h1 ^ (h1 >>> 16), 2246822507);
    h1 ^= Math.imul(h2 ^ (h2 >>> 13), 3266489909);
    h2 = Math.imul(h2 ^ (h2 >>> 16), 2246822507);
    h2 ^= Math.imul(h1 ^ (h1 >>> 13), 3266489909);
    return (4294967296 * (2097151 & h2) + (h1 >>> 0)).toString(36);
  }

  /** 一份手序的内容：键数、作者、说明、每个音符的砖号和键（左撇子的先镜像成右手：游戏里切换惯用手不算换了一份）。 */
  function fingeringText(doc) {
    const mirror = doc.meta.leftHanded === true;
    const notes = doc.notes.map(n => n.tile + ',' + n.lastTile + ':' + n.lanes.map(l => (mirror ? doc.keys - 1 - l[0] : l[0])).sort((a, b) => a - b).join('+'));
    return JSON.stringify([doc.keys, doc.meta.author, doc.meta.comment]) + '|' + notes.join(';');
  }

  /**
   * 现在这份手序的指纹，文件名后给（保存时先记下内容，存成什么名字要等写完才知道）：内容加文件名。
   * 同一张谱的两份手序，键位、作者、说明、文件名有一样不同就是两份。
   */
  function baseFor(doc) {
    const text = fingeringText(doc);
    return fileName => 'b' + hashText(text + '|' + fileName);
  }
  const baseOf = doc => baseFor(doc)(doc.fileName);

  /** 打开的手序文件的草稿名：谱面签名加文件内容的指纹（两份不同的文件不共用一份草稿；同一份文件换了名字再打开还能找到）。 */
  const fileDraftId = doc => doc.signature + '.' + hashText(fingeringText(doc));

  /**
   * 会话里带的几种键数（2026-11-22 用户：编辑器里能切换 4K、6K、8K）：会话本身是游戏里正在显示的那种，
   * variants 是同一关的其他键数（各有自己的手序、文件名、键位、自动排法；轨道、音乐这些共用）。按键数从小到大。
   */
  function sessionVariants(s) {
    const keysOf = f => (f && typeof f === 'object' ? (f.keys === undefined ? 4 : f.keys) : 0);
    const list = [{ keys: keysOf(s.fingering), value: s }];
    for (const v of Array.isArray(s.variants) ? s.variants : []) {
      const keys = v && typeof v === 'object' ? keysOf(v.fingering) : 0;
      if (int(keys) && keys > 0 && !list.some(x => x.keys === keys)) list.push({ keys, value: v });
    }
    return list.sort((a, b) => a.keys - b.keys);
  }

  /** 读游戏给的会话（session.js 里的 window.RHYTHMHINT_SESSION）。keys：打开会话里带的另一种键数（不给就是游戏里正在显示的）。 */
  function fromSession(s, keys) {
    need(s && s.format === SESSION_FORMAT, 'notSession');
    const variants = sessionVariants(s);
    const k = (keys && variants.find(v => v.keys === keys) || variants.find(v => v.value === s)).value; // 和键数有关的字段从这里读
    const fingering = parseFingering(k.fingering);
    const auto = Array.isArray(k.fromAuto) ? k.fromAuto : [];
    fingering.notes.forEach((n, i) => { n.filled = auto[i] === 1 || auto[i] === true; });
    const refs = { standard: refMap(k.references && k.references.standard), alternate: refMap(k.references && k.references.alternate) };
    return finish(fingering, {
      keyChoices: variants.map(v => v.keys),
      keyLabels: Array.isArray(k.keyLabels) && k.keyLabels.length === fingering.keys ? k.keyLabels.map(String) : defaultLabels(fingering.keys),
      refs,
      beats: Array.isArray(s.beats) ? s.beats.filter(b => Array.isArray(b) && finite(b[0])).map(b => ({ time: b[0], measure: b[1] === 1 })) : [],
      tempo: Array.isArray(s.tempo) ? s.tempo.filter(b => Array.isArray(b) && finite(b[0]) && finite(b[1]) && b[1] > 0).map(b => ({ time: b[0], bpm: b[1] })) : [],
      path: Array.isArray(s.path) ? s.path.filter(p => Array.isArray(p) && finite(p[0]) && finite(p[1]) && finite(p[2])).map((p, i) => ({ x: p[0], y: p[1], time: p[2], hit: p[3] === 1, tile: int(p[4]) ? p[4] : i,
        entry: finite(p[5]) ? p[5] : null, exit: finite(p[6]) ? p[6] : null })) : [], // 进出方向：数学角、弧度，游戏里读的或由离线读谱的原始方向算出
      audio: s.audio && typeof s.audio.file === 'string' ? {
        file: s.audio.file,
        // 同一首歌另存的脚本（整首解码后播，跳转才准）：只认 recent/ 下的 .js，别的地方的脚本不载入
        script: typeof s.audio.script === 'string' && /^recent\/[^\\/]+\.js$/.test(s.audio.script) ? s.audio.script : '',
        offset: finite(s.audio.offset) ? s.audio.offset : 0,
        pitch: finite(s.audio.pitch) && s.audio.pitch > 0 ? s.audio.pitch : 1,
        calibration: finite(s.audio.calibration) ? s.audio.calibration : 0, // 游戏的画面校准（秒）：编辑器照样补
        peaksPerSecond: int(s.audio.peaksPerSecond) ? s.audio.peaksPerSecond : 0, peaks: decodePeaks(s.audio.peaks),
      } : null,
      fileName: typeof k.fileName === 'string' ? k.fileName : '',
      saveFolder: typeof s.saveFolder === 'string' ? s.saveFolder : '',
      // 选文件夹时从哪打开：游戏装在浏览器不让写的地方时，Mod 让编辑器存到文档里的中转文件夹
      saveStartIn: s.saveStartIn === 'documents' ? 'documents' : '',
      usedFile: typeof k.usedFile === 'string' ? k.usedFile : '',
      language: typeof s.language === 'string' ? s.language : '',
      modVersion: typeof s.modVersion === 'string' ? s.modVersion : '', // 导出这份会话的 Mod 版本（关于里显示）
      roundNotes: s.roundNotes === true,
      fx: s.fx && typeof s.fx === 'object' && !Array.isArray(s.fx) ? s.fx : null, // 轨道和镜头事件（trackfx.js 重放）
      sessionId: typeof k.id === 'string' ? k.id : '', // 这一关在「历史」里的 Id（谱面指纹，6K、8K 带 -6k、-8k），草稿按它存
      hitSound: typeof s.hitSound === 'string' ? s.hitSound : '',
      fromGame: true,
    });
  }

  /** 读别人分享的手序文件（JSON 文本）。 */
  function fromFileText(text) {
    let obj;
    try { obj = JSON.parse(text); } catch (e) { throw new FingeringError('notJson', e.message); }
    return finish(parseFingering(obj), { keyLabels: defaultLabels(obj.keys || 4) });
  }

  // 游戏里各键数的默认键位（同 Mod 的 Settings.KeyLabels、KeyLabels6、KeyLabels8）
  const DEFAULT_LABELS = { 4: ['D', 'F', 'J', 'K'], 6: ['S', 'D', 'F', 'J', 'K', 'L'], 8: ['A', 'S', 'D', 'F', 'J', 'K', 'L', ';'] };

  function defaultLabels(keys) {
    return DEFAULT_LABELS[keys] ? DEFAULT_LABELS[keys].slice() : Array.from({ length: keys }, (_, i) => String(i + 1));
  }

  /** 参考排法：按「第一块砖」查；只有分组和当前音符一样（同一组砖）时才能对照、套用。 */
  function refMap(list) {
    const map = new Map();
    if (!Array.isArray(list)) return map;
    list.forEach(n => {
      if (Array.isArray(n) && n.length === 3 && int(n[0]) && int(n[1]) && Array.isArray(n[2]))
        map.set(n[0], { lastTile: n[1], lanes: sortLanes(n[2].filter(l => Array.isArray(l) && int(l[0])).map(l => [l[0], finite(l[1]) ? l[1] : 0])) });
    });
    return map;
  }

  function refLanes(doc, note, which) {
    const r = doc.refs[which] && doc.refs[which].get(note.tile);
    return r && r.lastTile === note.lastTile && r.lanes.length === note.lanes.length ? r.lanes : null;
  }

  function decodePeaks(b64) {
    if (typeof b64 !== 'string' || !b64) return new Uint8Array(0);
    if (typeof atob === 'function') {
      const s = atob(b64); const out = new Uint8Array(s.length);
      for (let i = 0; i < s.length; i++) out[i] = s.charCodeAt(i);
      return out;
    }
    return new Uint8Array(Buffer.from(b64, 'base64'));
  }

  function plain(text) { return String(text || '').replace(/<[^>]*>/g, '').trim(); }

  /** 「曲师 - 曲名.json」；不是 4 键的后面加「 6K」「 8K」（同一首歌的 4K、6K 手序不撞名，同 CustomFingering.SuggestedFileName）。 */
  function suggestedFileName(meta, keys = 4) {
    const song = plain(meta.song), artist = plain(meta.artist);
    return (cleanFileName(artist && song ? artist + ' - ' + song : song) || 'fingering') + (keys !== 4 ? ' ' + keys + 'K' : '') + '.json';
  }

  const RESERVED_NAME = /^(con|prn|aux|nul|com[1-9]|lpt[1-9])\s*(\.|$)/i; // Windows 的保留名（带扩展名也不行），同 CustomFingering.ReservedName

  /**
   * 能当 Windows 文件名的名字（不含 .json），和 Mod 的 CustomFingering.SuggestedFileName 逐步一致：去掉控制字符，
   * 路径符号换成 _，去首尾空白和结尾的点，保留名前面加 _，超过 80 个 UTF-16 字符截到 80（第 80 个是 emoji 的前半个时截到 79）。
   * 全被去掉时为空串。
   */
  function cleanFileName(text) {
    let name = String(text || '').replace(/[\u0000-\u001f\u007f-\u009f]/g, '').replace(/[\\/:*?"<>|]/g, '_').trim().replace(/\.+$/, '');
    if (!name) return '';
    if (RESERVED_NAME.test(name)) name = '_' + name;
    return name.length > 80 ? name.slice(0, /[\ud800-\udbff]/.test(name[79]) ? 79 : 80) : name;
  }

  /** 草稿里记的文件名能不能直接用：.json 结尾、没有路径和控制字符（游戏给的名字可能比 80 个字符长，不再截）。 */
  const usableFileName = name => typeof name === 'string' && name.length > 5 && name.length <= 255 && /\.json$/i.test(name)
    && !/[\\/:*?"<>|\u0000-\u001f]/.test(name) && !RESERVED_NAME.test(name);

  const MAX_NUMBERED = 99; // 同 Mod 的 FingeringInbox.MaxNumbered

  /** 第 k 个候选文件名：Song.json、Song (2).json、Song (3).json……（同 FingeringInbox.Numbered） */
  function numberedFileName(name, k) {
    return k <= 1 ? name : name.replace(/\.json$/i, '') + ' (' + k + ').json';
  }

  /** 已有的这份文件是不是这一关的手序：认得同一份谱面指纹（charts 有重合）、键数也一样（没写当 4）。读不出来的不算。 */
  function sameLevelFile(doc, text) {
    let obj;
    try { obj = JSON.parse(text); } catch (e) { return false; } // 读不出来的不是这一关的：不覆盖
    const mine = new Set(doc.meta.charts || []);
    return !!obj && obj.format === FILE_FORMAT && Array.isArray(obj.charts) && obj.charts.some(c => mine.has(c))
      && (obj.keys === undefined ? 4 : obj.keys) === doc.keys;
  }

  /**
   * 存进手序文件夹用哪个文件名（和 Mod 的 FingeringInbox.SaveName 同一规则，2026-11-19 评审：同一首歌不同谱师的手序原来会被直接覆盖）：
   * 没人用、是游戏这一关正用着的那份、或认得同一份谱面指纹就用它；别的谱的或读不出来的，依次试「名字 (2).json」……。
   * readExisting(name) 给出文件夹里这个文件的文字，没有这个文件给 null。都被占了返回 null。
   */
  async function saveName(doc, readExisting) {
    for (let k = 1; k <= MAX_NUMBERED; k++) {
      const name = numberedFileName(doc.fileName, k);
      const text = await readExisting(name);
      if (text === null || (doc.usedFile && name === doc.usedFile) || sameLevelFile(doc, text)) return name;
    }
    return null;
  }

  /** 这一关砖块的指纹（草稿按它存）。 */
  function signature(hits) {
    let h = 2166136261 >>> 0;
    const mix = v => { h ^= v; h = Math.imul(h, 16777619) >>> 0; };
    hits.forEach(x => { mix(x.tile); mix(Math.round(x.time * 1000)); mix(x.hold > 0 ? 1 : 0); });
    return hits.length + '-' + h.toString(36);
  }

  const round6 = v => Math.round(v * 1e6) / 1e6;

  /** 存成手序文件（和 C# 写的格式一样，一项一行，方便看改动）。 */
  function toFileText(doc, now) {
    const head = {
      format: FILE_FORMAT, schemaVersion: SCHEMA_VERSION, keys: doc.keys,
      song: doc.meta.song, artist: doc.meta.artist, charter: doc.meta.charter,
      author: doc.meta.author, comment: doc.meta.comment, leftHanded: doc.meta.leftHanded,
      charts: doc.meta.charts || [],
      updatedAt: (now || new Date()).toISOString(),
    };
    const hits = doc.hits.map(h => JSON.stringify([h.tile, round6(h.time), round6(h.hold), h.angle === null ? null : Math.round(h.angle * 1000) / 1000]));
    const notes = doc.notes.map(n => JSON.stringify([n.tile, n.lastTile, sortLanes(n.lanes).map(l => [l[0], round6(l[1])])]));
    // 不认识的字段原样跟在认得的后面、hits 前面；一个个拼，名字是数字的也不会被 JS 挪到最前面
    const others = (doc.otherFields || []).map(([k, v]) => ',' + JSON.stringify(k) + ':' + JSON.stringify(v)).join('');
    const json = JSON.stringify(head);
    return json.slice(0, -1) + others + ',"hits":[\n' + hits.join(',\n') + '],"notes":[\n' + notes.join(',\n') + ']}';
  }

  // ---------- 编辑 ----------

  /** count 个互不相同、在范围里的轨道。 */
  function validLanes(doc, lanes, count) {
    if (!Array.isArray(lanes) || lanes.length !== count) return false;
    const seen = new Set();
    return lanes.every(l => int(l) && l >= 0 && l < doc.keys && !seen.has(l) && seen.add(l));
  }

  /**
   * 把一个音符改成按 targets 这几个轨道（个数不变）：targets[i] 是现在第 i 个键（按轨道排）挪去的轨道，长按跟着键走
   * （2026-11-19 评审：原来按轨道顺序重新分长按，镜像后 D 长按没有变成 K 长按；Mod 的镜像是跟着键的）。不合法返回 null。
   */
  function withLanes(doc, note, targets) {
    const from = sortLanes(note.lanes);
    if (!validLanes(doc, targets, from.length)) return null;
    return sortLanes(targets.map((lane, i) => [lane, from[i][1]]));
  }

  /**
   * 按 pairs（[[轨道, 想要的长按]]）排这个音符，长按用音符自己的：按长短一一对上，最长的给想要最长的那个键。
   * 参考排法、草稿记下的长按和这次的一样时就是原样；对不太上时也不会改动长按本身（时间以游戏为准）。
   */
  function withHolds(doc, note, pairs) {
    if (!Array.isArray(pairs) || !pairs.every(Array.isArray)) return null;
    const lanes = pairs.map(p => p[0]);
    if (!validLanes(doc, lanes, note.lanes.length)) return null;
    const want = pairs.map(p => (finite(p[1]) ? p[1] : 0));
    const holds = note.lanes.map(l => l[1]).sort((a, b) => b - a);
    const order = want.map((_, i) => i).sort((a, b) => want[b] - want[a] || a - b);
    return sortLanes(order.map((i, rank) => [lanes[i], holds[rank]]));
  }

  /** 一次编辑：{ 音符 id: 新的 lanes }。任何一个不合法就整次不做，返回 null。 */
  function plan(doc, notes, fn) {
    const change = new Map();
    for (const note of notes) {
      const lanes = fn(note);
      if (lanes === undefined) continue;
      if (lanes === null) return null;
      if (noteKey(lanes) !== noteKey(note.lanes)) change.set(note.id, lanes);
    }
    return change;
  }

  /** 检查器里直接点选的几个键：还在的键带着自己的长按，换掉的键按轨道顺序换到新点的键上。 */
  function pickedLanes(note, targets) {
    const from = sortLanes(note.lanes).map(l => l[0]);
    if (!Array.isArray(targets) || targets.length !== from.length) return null;
    const added = targets.filter(l => !from.includes(l)).sort((a, b) => a - b);
    let k = 0;
    return from.map(l => (targets.includes(l) ? l : added[k++]));
  }

  const ops = {
    /** 单押改到某条轨道。 */
    toLane: (doc, notes, lane) => plan(doc, notes, n => n.lanes.length === 1 ? withLanes(doc, n, [lane]) : undefined),
    /** 整体左右挪 delta 条轨道。 */
    shift: (doc, notes, delta) => plan(doc, notes, n => withLanes(doc, n, n.lanes.map(l => l[0] + delta))),
    /** 左右镜像。 */
    mirror: (doc, notes) => plan(doc, notes, n => withLanes(doc, n, n.lanes.map(l => doc.keys - 1 - l[0]))),
    /** 多押里单独挪一个键。 */
    moveKey: (doc, note, from, to) => plan(doc, [note], n => {
      if (!n.lanes.some(l => l[0] === from)) return null;
      if (from === to) return undefined;
      if (n.lanes.some(l => l[0] === to)) return null;
      return withLanes(doc, n, n.lanes.map(l => l[0] === from ? to : l[0]));
    }),
    /** 直接指定轨道（检查器里点选）。 */
    set: (doc, note, targets) => plan(doc, [note], n => { const mapped = pickedLanes(n, targets); return mapped && withLanes(doc, n, mapped); }),
    /** 套用参考排法（分组不一样的音符跳过）：参考在哪个键长按，套过来也在那个键。 */
    reference: (doc, notes, which) => plan(doc, notes, n => {
      const r = refLanes(doc, n, which);
      return r ? withHolds(doc, n, r) : undefined;
    }),
    /** 恢复成打开时的样子。 */
    revert: (doc, notes) => plan(doc, notes, n => n.orig.map(l => l.slice())),
  };

  /**
   * 撤销、重做：每一步记下改了哪些音符、改之前和改之后。
   * 每一步有自己的编号（step.id），「现在的样子」= 撤销栈顶那一步；保存时记下它，之后比的是同一步而不是步数
   * （只数步数时，改 A → 保存 → 撤销 → 改 B 会显示已保存）。
   */
  class History {
    constructor(doc) { this.doc = doc; this.undoStack = []; this.redoStack = []; this.lastId = 0; this.savedId = 0; }
    apply(change) {
      if (!change || change.size === 0) return false;
      const step = [];
      change.forEach((lanes, id) => {
        const note = this.doc.notes[id];
        step.push([id, note.lanes, lanes]);
        note.lanes = lanes;
      });
      step.id = ++this.lastId;
      this.undoStack.push(step);
      this.redoStack = [];
      return true;
    }
    undo() { return this.move(this.undoStack, this.redoStack, 1); }
    redo() { return this.move(this.redoStack, this.undoStack, 2); }
    move(from, to, pick) {
      const step = from.pop();
      if (!step) return null;
      step.forEach(([id, before, after]) => { this.doc.notes[id].lanes = pick === 1 ? before : after; });
      to.push(step);
      return step.map(s => s[0]);
    }
    /** 现在停在哪一步（0 = 打开时的样子）。 */
    get current() { return this.undoStack.length ? this.undoStack[this.undoStack.length - 1].id : 0; }
    get canUndo() { return this.undoStack.length > 0; }
    get canRedo() { return this.redoStack.length > 0; }
    get dirty() { return this.current !== this.savedId; }
    /** 记成已保存；step：存进去的是哪一步（保存要等文件夹权限，期间又改了时只算开始保存时的那一步）。 */
    markSaved(step = this.current) { this.savedId = step; }
  }

  // ---------- 参考信息 ----------

  /** 同一条轨道在 window 秒内连按两下的音符（两边都标出来）。 */
  function jacks(doc, window) {
    const last = new Array(doc.keys).fill(null);
    const out = new Set();
    for (const n of doc.notes) {
      for (const [lane] of n.lanes) {
        const prev = last[lane];
        if (prev && n.time - prev.time < window - 1e-6) { out.add(n.id); out.add(prev.id); }
        last[lane] = n;
      }
    }
    return out;
  }

  const FRACTIONS = [[1, 1], [1, 2], [1, 3], [1, 4], [1, 6], [1, 8], [1, 12], [1, 16], [1, 24], [1, 32], [2, 1], [3, 2], [3, 4], [3, 8], [2, 3]];

  /** 某个时间的一拍多少秒。 */
  function secondsPerBeat(doc, time) {
    let bpm = doc.tempo.length ? doc.tempo[0].bpm : 0;
    for (const t of doc.tempo) { if (t.time <= time + 1e-6) bpm = t.bpm; else break; }
    return bpm > 0 ? 60 / bpm : 0;
  }

  /**
   * 音乐在某个时间的一拍多少秒：有节拍线（Mod 按变速和砖块推算的音乐拍子）就取所在那一拍的长度，过了最后一条取最后一拍；
   * 没有节拍线时退回关卡 BPM。关卡 BPM 常是谱师为转角设的数（工坊 2802147583 是 1011、32352），不能直接当音乐的拍子。
   */
  function musicBeat(doc, time) {
    const lines = doc.beats || [];
    if (lines.length < 2) return secondsPerBeat(doc, time);
    let lo = 0, hi = lines.length - 2;
    while (lo < hi) { const mid = (lo + hi + 1) >> 1; if (lines[mid].time <= time + 1e-6) lo = mid; else hi = mid - 1; }
    const spb = lines[lo + 1].time - lines[lo].time;
    return spb > 0 ? spb : secondsPerBeat(doc, time);
  }

  /** 两下之间的间隔是几分音符（按一拍是四分音符算）：1/4 拍 = 1/16。认不出返回空串。 */
  function noteValue(doc, time, gap) {
    const spb = musicBeat(doc, time);
    if (!(spb > 0) || !(gap > 0)) return '';
    const beats = gap / spb;
    for (const [a, b] of FRACTIONS) {
      if (Math.abs(beats - a / b) <= 0.03 * (a / b)) {
        // 一拍 = 四分音符：a/b 拍 = a/(4b) 个全音符，能约成 1/n 就写 1/n（八分音符 1/8），否则写成分数（附点四分 3/8）
        const value = 4 * b / a;
        return Number.isInteger(value) ? '1/' + value : a + '/' + (4 * b);
      }
    }
    return '';
  }

  /** auto：自动补齐的音符有几个（来源，看过了也照样算；还要检查几处看 reviewList）。 */
  function stats(doc, jackSet) {
    let changed = 0, auto = 0;
    doc.notes.forEach(n => { if (isChanged(n)) changed++; if (n.filled) auto++; });
    return { notes: doc.notes.length, changed, auto, jacks: jackSet ? jackSet.size : 0 };
  }

  const isChanged = n => noteKey(n.lanes) !== noteKey(n.orig);
  const handOf = (doc, lane) => lane < doc.keys / 2 ? 0 : 1;

  // ---------- 待检查 ----------
  // 2026-11-19 评审：自动补齐的音符改过、或者玩家选中它按了它现在的键（看过了，就这么按），就不用再检查

  /** 重算每个音符还要不要检查（n.auto）。每次改动后调用。 */
  function markReview(doc) {
    doc.notes.forEach(n => { n.auto = n.filled && !n.confirmed && !isChanged(n); });
  }

  /** 这几个音符看过了：自动补齐的记成确认过。返回新确认了几个。 */
  function confirm(notes) {
    let count = 0;
    notes.forEach(n => { if (n.filled && !n.confirmed) { n.confirmed = true; count++; } });
    return count;
  }

  /** 待检查的位置（音符 id，按顺序）：还要检查的自动补齐音符（连着的一段只算一处，跳到开头）和连按。 */
  function reviewList(doc, jackSet) {
    const ids = [];
    let last = false;
    for (const n of doc.notes) {
      if (n.auto && !last) ids.push(n.id);
      else if (!n.auto && jackSet && jackSet.has(n.id)) ids.push(n.id);
      last = n.auto;
    }
    return ids;
  }

  /** 下一处：from（当前选中的音符）后面的第一处，到尾了从头再来；没有为 -1。 */
  function nextReview(list, from) {
    if (!list.length) return -1;
    const after = list.find(id => id > from);
    return after !== undefined ? after : list[0];
  }

  /**
   * 草稿：这一关在编辑器里现在的样子（每个音符按哪几个键），改到一半关掉网页、下次从「历史」打开时接着改。
   * 存全部音符而不只是改过的：游戏重新导出这一关时原来的排法可能变了（比如换了排法、用上了保存的文件），照样能还原。
   * saved：和保存到游戏的文件一样；changed：和打开时相比改了几处；time：最后改动的时间。
   * tiles（2026-11-19 起）：按砖号记 [第一块砖号, 最后一块砖号, 键, 改过的记 1, 各键的长按]，游戏重新导出时多押分组变了也能逐条对上；
   * 后两项可以没有：没改过也没长按的只记前三项，有长按的第 4 项记 1 或 0、第 5 项记长按（套回去时长按跟着原来那个键）。
   * lanes 照旧按第几个音符记，旧版编辑器（1.3.1 及以前）读的是它。session、leftHanded 是哪一关、当时的惯用手。
   * 后来加的（旧版编辑器不认，照样能用）：confirmed 是看过的自动补齐音符 [第一块砖号, 最后一块砖号]，fileName 是文件面板里的文件名；
   * base 是打开时那份手序的指纹，savedBase 是最后一次存进游戏的那份的指纹（游戏下次用它导出时也算对得上），见 draftMatches。
   */
  function draftOf(doc, saved, time) {
    const draft = {
      signature: doc.signature, session: doc.sessionId, leftHanded: doc.meta.leftHanded === true,
      lanes: doc.notes.map(n => n.lanes.map(l => l[0])),
      tiles: doc.notes.map(draftEntry),
      author: doc.meta.author, comment: doc.meta.comment, fileName: doc.fileName,
      confirmed: doc.notes.filter(n => n.confirmed).map(n => [n.tile, n.lastTile]),
      saved: saved === true, changed: stats(doc).changed, time: finite(time) ? time : Date.now(),
    };
    if (doc.base) draft.base = doc.base;
    if (doc.savedBase) draft.savedBase = doc.savedBase;
    return draft;
  }

  /**
   * 这份草稿是不是在现在打开的这份手序上改的（是才自动套上，不是就先问玩家）：
   * 记了指纹的，和打开时的指纹（base）或上次存进游戏的那份（savedBase）一样才算。
   * 旧版编辑器写的草稿没有指纹：按砖号记的、没改过的音符都和这次打开的一样，作者、说明、文件名也一样，才算；只按第几个音符记的认不出来。
   */
  function draftMatches(doc, draft) {
    if (!draft || typeof draft !== 'object') return false;
    if (typeof draft.base === 'string') return draft.base === doc.base || (typeof draft.savedBase === 'string' && draft.savedBase === doc.base);
    if (!Array.isArray(draft.tiles) || draft.tiles.length !== doc.notes.length) return false;
    const o = doc.opened || {};
    const same = (v, want) => v === undefined || v === want; // 更早的测试版没记作者、文件名
    if (!same(draft.author, o.author) || !same(draft.comment, o.comment) || !same(draft.fileName, o.fileName)) return false;
    const mirror = typeof draft.leftHanded === 'boolean' && draft.leftHanded !== (doc.meta.leftHanded === true);
    return draft.tiles.every((e, i) => {
      const n = doc.notes[i];
      if (!Array.isArray(e) || e[0] !== n.tile || e[1] !== n.lastTile || !Array.isArray(e[2])) return false;
      if (e[3] === 1) return true; // 改过的：不知道打开时是什么样
      const lanes = e[2].map(l => (mirror ? doc.keys - 1 - l : l)).sort((a, b) => a - b);
      return lanes.join('+') === laneKey(n.lanes);
    });
  }

  /**
   * 恢复草稿后点「放弃」要改回去的：恢复时改过的音符，之后没再改过的改回恢复前的样子；之后又改过的留着（那是新改的）。
   * restored、before：恢复时 { 音符 id: 恢复成的 lanes }、{ 音符 id: 恢复前的 lanes }。
   */
  function undoRestore(doc, restored, before) {
    const change = new Map();
    restored.forEach((lanes, id) => {
      const n = doc.notes[id];
      if (n && before.has(id) && noteKey(n.lanes) === noteKey(lanes)) change.set(id, before.get(id));
    });
    return change;
  }

  function draftEntry(n) {
    const entry = [n.tile, n.lastTile, n.lanes.map(l => l[0])];
    const holds = n.lanes.some(l => l[1] > 0);
    if (isChanged(n) || holds) entry.push(isChanged(n) ? 1 : 0);
    if (holds) entry.push(n.lanes.map(l => round6(l[1])));
    return entry;
  }

  /** 草稿里的作者、说明、文件名：类型不对或文件名不能用的照这一次打开时的。 */
  function draftMeta(doc, draft) {
    const text = (v, fallback) => (typeof v === 'string' ? v : fallback);
    return {
      author: text(draft && draft.author, doc.meta.author), comment: text(draft && draft.comment, doc.meta.comment),
      fileName: draft && usableFileName(draft.fileName) ? draft.fileName : doc.fileName,
    };
  }

  /** 草稿里记着看过的自动补齐音符（按砖号对上的音符 id）。 */
  function draftConfirmed(doc, draft) {
    if (!draft || !Array.isArray(draft.confirmed)) return [];
    const byTile = new Map(doc.notes.map(n => [n.tile, n]));
    return draft.confirmed.map(e => Array.isArray(e) && byTile.get(e[0]))
      .filter((n, i) => n && n.filled && n.lastTile === draft.confirmed[i][1]).map(n => n.id);
  }

  /** 和打开时完全一样（键位、作者、说明、文件名都没变，也没有看过的待检查）：不用留草稿。 */
  function untouched(doc) {
    const o = doc.opened || {};
    return doc.notes.every(n => !isChanged(n) && !n.confirmed)
      && doc.meta.author === o.author && doc.meta.comment === o.comment && doc.fileName === o.fileName;
  }

  /**
   * 浏览器里的草稿太多时删哪些（list：[{ key, saved, time }]）：留 keep 份。
   * 先删已保存的（游戏里有文件），再删没保存的，各自从最久没改的删起；protect 是正在编辑的那一份，不删。
   */
  function draftsToDrop(list, keep, protect) {
    const extra = list.length - keep;
    if (extra <= 0) return [];
    return list.filter(d => d.key !== protect)
      .sort((a, b) => (b.saved === true) - (a.saved === true) || (finite(a.time) ? a.time : 0) - (finite(b.time) ? b.time : 0))
      .slice(0, extra).map(d => d.key);
  }

  /**
   * 草稿和现在这一份不一样的音符（可撤销地套上去）；不是这一关的草稿为 null。
   * 有 tiles 的按砖号对：分组变了、对不上的音符不套，其中玩家改过的个数记在 change.missed（提示玩家；没改过的对不上不算丢）；
   * 草稿的惯用手和这次导出不同时先镜像。
   * 旧版草稿只有 lanes：按第几个音符整份套（个数不同就不套）；更早的测试版是 { 音符号: 键 } 只记改过的，照样认。
   */
  function draftChange(doc, draft) {
    if (!draft || typeof draft !== 'object') return null;
    if (Array.isArray(draft.tiles) && ((draft.session && draft.session === doc.sessionId) || draft.signature === doc.signature))
      return draftByTile(doc, draft);
    if (draft.signature !== doc.signature || !draft.lanes || typeof draft.lanes !== 'object') return null;
    if (Array.isArray(draft.lanes) && draft.lanes.length !== doc.notes.length) return null;
    const change = new Map();
    change.missed = 0;
    for (const [id, targets] of Object.entries(draft.lanes)) {
      const note = doc.notes[Number(id)];
      if (!note || !Array.isArray(targets)) return null;
      const lanes = withLanes(doc, note, ascending(targets));
      if (!lanes) return null;
      if (noteKey(lanes) !== noteKey(note.lanes)) change.set(note.id, lanes);
    }
    return change;
  }

  /** 只记了键、没记长按的（旧草稿）：照旧按轨道顺序分长按。 */
  const ascending = lanes => lanes.slice().sort((a, b) => a - b);

  function draftByTile(doc, draft) {
    const byTile = new Map(doc.notes.map(n => [n.tile, n]));
    const mirror = typeof draft.leftHanded === 'boolean' && draft.leftHanded !== (doc.meta.leftHanded === true);
    const change = new Map();
    change.missed = 0;
    for (const entry of draft.tiles) {
      const note = Array.isArray(entry) ? byTile.get(entry[0]) : undefined;
      const targets = note && note.lastTile === entry[1] && Array.isArray(entry[2])
        ? entry[2].map(l => (mirror && int(l) ? doc.keys - 1 - l : l)) : null;
      const holds = targets && Array.isArray(entry[4]) && entry[4].length === targets.length ? entry[4] : null;
      const lanes = holds ? withHolds(doc, note, targets.map((l, i) => [l, holds[i]])) : targets && withLanes(doc, note, ascending(targets));
      if (!lanes) { if (Array.isArray(entry) && entry[3] === 1) change.missed++; continue; } // 分组变了（或这一条坏了）：不套；改过的告诉玩家没接上
      if (noteKey(lanes) !== noteKey(note.lanes)) change.set(note.id, lanes);
    }
    return change;
  }

  // ---------- 键位 ----------
  // 游戏里的键位是 Unity 的 KeyCode 名字或符号本身（Mod 的 KeyBindings、KeyPicker）：单个字符按浏览器的 e.key 认（跟着键盘布局），
  // 其他的换成浏览器的 KeyboardEvent.code 认（2026-11-19 评审：Keypad4、LeftShift 这类键在编辑器里按了没反应）
  const UNITY_CODES = {
    LeftShift: 'ShiftLeft', RightShift: 'ShiftRight', LeftControl: 'ControlLeft', RightControl: 'ControlRight',
    LeftAlt: 'AltLeft', RightAlt: 'AltRight', LeftCommand: 'MetaLeft', RightCommand: 'MetaRight', LeftWindows: 'MetaLeft', RightWindows: 'MetaRight',
    Space: 'Space', Tab: 'Tab', Return: 'Enter', Backspace: 'Backspace', CapsLock: 'CapsLock',
    Insert: 'Insert', Delete: 'Delete', Home: 'Home', End: 'End', PageUp: 'PageUp', PageDown: 'PageDown',
    UpArrow: 'ArrowUp', DownArrow: 'ArrowDown', LeftArrow: 'ArrowLeft', RightArrow: 'ArrowRight',
    KeypadPeriod: 'NumpadDecimal', KeypadDivide: 'NumpadDivide', KeypadMultiply: 'NumpadMultiply', KeypadMinus: 'NumpadSubtract',
    KeypadPlus: 'NumpadAdd', KeypadEnter: 'NumpadEnter', KeypadEquals: 'NumpadEqual',
    Semicolon: 'Semicolon', Quote: 'Quote', Comma: 'Comma', Period: 'Period', Slash: 'Slash', Backslash: 'Backslash',
    LeftBracket: 'BracketLeft', RightBracket: 'BracketRight', Minus: 'Minus', Equals: 'Equal', BackQuote: 'Backquote',
  };

  /** Unity 键名对应的 e.code；单个字符（D、;、[）和认不出的为空串。 */
  function keyCodeOf(label) {
    const name = String(label || '');
    let m;
    if ((m = /^Alpha([0-9])$/.exec(name))) return 'Digit' + m[1];
    if ((m = /^Keypad([0-9])$/.exec(name))) return 'Numpad' + m[1];
    if (/^F([1-9]|1[0-5])$/.test(name)) return name;
    return Object.prototype.hasOwnProperty.call(UNITY_CODES, name) ? UNITY_CODES[name] : '';
  }

  /** 按下的键（{ key, code }）是第几条轨道的键；不是为 -1。 */
  function laneOfKey(labels, e) {
    return labels.findIndex(label => {
      const code = keyCodeOf(label);
      if (code) return e.code === code;
      return String(label).length === 1 && typeof e.key === 'string' && e.key.length === 1 && e.key.toUpperCase() === String(label).toUpperCase();
    });
  }

  /** Shift、Ctrl、Alt、Win 这类键：和别的键一起按时是组合键。 */
  const isModifier = label => /^(Shift|Control|Alt|Meta)/.test(keyCodeOf(label));

  /** 键帽上显示的字（同 Mod 的 KeyBindings.Display）：长名字缩写，没指定的为空串，单个字母大写。 */
  function keyDisplay(label) {
    const name = String(label || '');
    const short = {
      KeypadPeriod: 'Num.', KeypadDivide: 'Num/', KeypadMultiply: 'Num*', KeypadMinus: 'Num-', KeypadPlus: 'Num+', KeypadEnter: 'NumEnter',
      KeypadEquals: 'Num=', PageUp: 'PgUp', PageDown: 'PgDn', Insert: 'Ins', Delete: 'Del', Backspace: 'Bksp', CapsLock: 'Caps',
      Mouse2: 'MMB', Mouse3: 'MB4', Mouse4: 'MB5', Mouse5: 'MB6', Mouse6: 'MB7', None: '', LeftShift: 'LShift', RightShift: 'RShift',
      LeftControl: 'LCtrl', RightControl: 'RCtrl', LeftAlt: 'LAlt', RightAlt: 'RAlt', Return: 'Enter',
      UpArrow: '↑', DownArrow: '↓', LeftArrow: '←', RightArrow: '→',
    };
    const m = /^Keypad([0-9])$/.exec(name);
    if (m) return 'Num' + m[1];
    if (Object.prototype.hasOwnProperty.call(short, name)) return short[name];
    return name.length === 1 ? name.toUpperCase() : name;
  }

  // 编辑器自己的单键快捷键（app.js 的键盘处理）：单个字符按 e.key，其余按 e.code
  const SHORTCUT_CHARS = new Set(['M', 'N', '[', ']']);
  const SHORTCUT_CODES = new Set(['Space', 'ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home']);

  /** 和编辑器快捷键重复的轨道键（显示用的名字）：轨道键优先，按下时改键，快捷键用不了，要提示玩家。 */
  function shortcutClashes(labels) {
    return labels.filter(label => {
      const code = keyCodeOf(label);
      return code ? SHORTCUT_CODES.has(code) : SHORTCUT_CHARS.has(String(label).toUpperCase());
    }).map(keyDisplay);
  }

  const RECENT_FORMAT = 'rhythmhint-editor-recent';
  const SAFE_ID = /^[0-9]+-[0-9a-f]+(-[0-9]+k)?$/; // 同 SessionLibrary.IdPattern：6K、8K 的带「-6k」「-8k」

  /**
   * 「历史」里的各关（recent/index.js 里的 window.RHYTHMHINT_RECENT，Mod 的 SessionLibrary 写）：新的在前。
   * 只认 Mod 写的样子，文件名都由 Id 拼出来，不会指到 recent 目录外面。
   */
  function parseRecent(obj) {
    if (!obj || obj.format !== RECENT_FORMAT || !Array.isArray(obj.items)) return [];
    const text = v => (typeof v === 'string' ? v : '');
    return obj.items
      .filter(e => e && typeof e.id === 'string' && SAFE_ID.test(e.id) && e.session === e.id + '.js'
        && (!e.audio || (typeof e.audio === 'string' && e.audio.startsWith(e.id + '-') && !/[\\/]/.test(e.audio))))
      .map(e => ({
        id: e.id, song: text(e.song), artist: text(e.artist), charter: text(e.charter), notes: int(e.notes) ? e.notes : 0,
        keys: int(e.keys) && e.keys > 0 ? e.keys : 4,
        session: e.session, audio: text(e.audio), fileName: text(e.fileName), exported: finite(e.exported) ? e.exported : 0,
      }))
      .sort((a, b) => b.exported - a.exported);
  }

  return {
    FILE_FORMAT, SESSION_FORMAT, MAX_FILE_BYTES, FingeringError, parseFingering, fromSession, fromFileText, toFileText, parseRecent,
    defaultLabels, suggestedFileName, cleanFileName, numberedFileName, sameLevelFile, saveName, signature, ops, History, jacks, noteValue, secondsPerBeat, stats, refLanes, laneKey,
    isChanged, handOf, draftOf, draftChange, draftMeta, draftConfirmed, draftsToDrop, draftMatches, undoRestore, untouched, remember, plain,
    baseFor, baseOf, fileDraftId,
    markReview, confirm, reviewList, nextReview, keyCodeOf, laneOfKey, isModifier, keyDisplay, shortcutClashes,
  };
});
