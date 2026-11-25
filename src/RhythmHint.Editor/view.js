/* 手序编辑器的谱面画布：时间从下往上走，判定线在下面（和游戏里音符往下落一样）。
 * 所有列放在同一块面板里：两边是两种自动排法（和当前手序一样的地方压暗，不一样的亮着），中间是当前手序，
 * 中间那列的音符、判定线和按键框照游戏里画；左边是小节号和波形，右边是两下之间的间隔。
 * 只负责画和点中了什么，改动由 app.js 做。 */
(function (root) {
  'use strict';

  const COLORS = {
    single: '#f57829', double: '#3d9ee0', multi: '#b273f2',
    frame: '#17181c', header: '#1b1d22', border: '#2b2e35', divider: '#24262c', gutter: '#151519',
    text: '#e9ebef', text2: '#b3b8c2', text3: '#7d838e',
    accent: '#f57829', warn: '#f2c14e', danger: '#ef5b5b', ok: '#4cc38a', standard: '#8fd3ff', alternate: '#ffd08f', dot: 'rgba(255,255,255,0.05)',
  };
  const FONT = '"Segoe UI Variable Text", "Segoe UI", "Microsoft YaHei UI", "PingFang SC", "Hiragino Sans", "Malgun Gothic", system-ui, sans-serif';
  const MONO = '"Cascadia Mono", Consolas, ui-monospace, monospace';

  const colorOf = n => n.lanes.length > 2 ? COLORS.multi : n.lanes.length > 1 ? COLORS.double : COLORS.single;

  // 游戏里四轨面板的颜色（Painter.PanelColor、LaneView、BallSkin 里的 RGB 换成十六进制），编辑器照着画
  const GAME = {
    panel: '#121214', sep: '#333338', measure: '#5c5c66', beat: '#33333b',
    keyFill: '#242429', keyFillLit: '#4d4d57', keyBorder: '#595966', keyText: '#d9d9e6',
    judgeLine: '#474752', judgeFill: '#29292e', judgeRim: '#80808c', judgeInner: '#52525c', ballShadow: 'rgba(0,0,0,0.75)',
  };
  const FLASH_MS = 180; // LaneView.FlashSeconds
  const DOT_STEP = 22; // 背景点阵的间距（CSS 像素）

  function hexToRgb(hex) {
    const n = parseInt(hex.slice(1), 16);
    return [n >> 16 & 255, n >> 8 & 255, n & 255];
  }
  const mixAlpha = (hex, a) => { const [r, g, b] = hexToRgb(hex); return 'rgba(' + r + ',' + g + ',' + b + ',' + a + ')'; };
  /** 两个颜色按比例混（Color.Lerp）。 */
  function mixColor(a, b, t) {
    const x = hexToRgb(a), y = hexToRgb(b);
    return 'rgb(' + x.map((v, i) => Math.round(v + (y[i] - v) * t)).join(',') + ')';
  }

  function roundRect(g, x, y, w, h, r) {
    r = Math.max(0, Math.min(r, w / 2, h / 2));
    g.beginPath();
    g.moveTo(x + r, y);
    g.arcTo(x + w, y, x + w, y + h, r);
    g.arcTo(x + w, y + h, x, y + h, r);
    g.arcTo(x, y + h, x, y, r);
    g.arcTo(x, y, x + w, y, r);
    g.closePath();
  }

  function fmtTime(t) {
    const sign = t < 0 ? '-' : '';
    t = Math.abs(t);
    const m = Math.floor(t / 60), s = Math.floor(t % 60);
    return sign + m + ':' + String(s).padStart(2, '0');
  }

  const lanesOf = (n, state) => state.preview && state.preview.has(n.id) ? state.preview.get(n.id) : n.lanes;

  class ChartView {
    constructor(canvas) {
      this.canvas = canvas;
      this.g = canvas.getContext('2d');
      this.doc = null;
      this.geom = null;
      this.dpr = 1;
    }

    setDoc(doc) { this.doc = doc; }

    resize() {
      const r = this.canvas.getBoundingClientRect();
      this.dpr = Math.max(1, window.devicePixelRatio || 1);
      this.canvas.width = Math.round(r.width * this.dpr);
      this.canvas.height = Math.round(r.height * this.dpr);
      this.w = r.width;
      this.h = r.height;
    }

    /** 各列紧挨着排成一块面板，在抽屉和检查器之间居中。 */
    layout(state) {
      const keys = this.doc ? this.doc.keys : 4;
      const showStd = state.show.standard && this.doc && this.doc.refs.standard.size > 0;
      const showAlt = state.show.alternate && this.doc && this.doc.refs.alternate.size > 0;
      const refPad = 0, mainPad = 0, ruler = 56, wave = state.show.wave && state.waveform ? 30 : 0, gaps = state.show.gaps ? 84 : 0; // 没有波形数据就不留这一列
      const left = state.leftInset, right = this.w - state.rightInset;
      // 轨道宽度按窗口高度定；左右放不下（抽屉和检查器都开着、窗口又不宽）就收窄
      const refs = (showStd ? 1 : 0) + (showAlt ? 1 : 0);
      const fixed = mainPad * 2 + refs * refPad * 2 + ruler + wave + gaps + 24;
      const lane = Math.floor(Math.max(28, Math.min(64, (this.h - 200) / 8, (right - left - fixed) / (keys * (1 + refs * state.refScale)))));
      const refLane = Math.max(8, Math.round(lane * state.refScale)); // 参考列和当前手序的宽度比例可调，默认一半
      const refW = keys * refLane + refPad * 2;
      const mainW = keys * lane + mainPad * 2;
      const total = (showStd ? refW : 0) + ruler + wave + mainW + gaps + (showAlt ? refW : 0);
      let x = Math.max(left + 12, left + (right - left - total) / 2);
      const g = { lane, refLane, keys, top: 14, header: 40, playY: this.h - 150, s: lane / 68, sr: refLane / 68, round: !!state.round };
      g.noteTop = g.top + g.header + 6;
      // 游戏里（1080p）轨道宽 68：按键框在判定线下 10、高 52；圆形样式的判定圈半径 29、下面留 18
      g.keyRow = g.playY + 10 * g.s;
      g.bottom = g.round ? g.playY + 47 * g.s : g.keyRow + 58 * g.s;
      g.frame = { x, w: total };
      if (showStd) { g.std = { x, w: refW, lanesX: x + refPad }; x += refW; }
      g.ruler = { x, w: ruler }; x += ruler;
      g.wave = { x, w: wave }; x += wave;
      g.main = { x, w: mainW, lanesX: x + mainPad }; x += mainW;
      g.gaps = { x, w: gaps }; x += gaps;
      if (showAlt) { g.alt = { x, w: refW, lanesX: x + refPad }; }
      this.geom = g;
      return g;
    }

    yAt(t, state) { return this.geom.playY - (t - state.time) * state.pxPerSec; }
    timeAt(y, state) { return state.time + (this.geom.playY - y) / state.pxPerSec; }

    laneAt(x) {
      const g = this.geom;
      return Math.floor((x - g.main.lanesX) / g.lane);
    }

    /** 点中了什么：音符（哪个键）、左边的时间尺、谱面空白处。 */
    hit(x, y, state) {
      const g = this.geom, doc = this.doc;
      if (!g || !doc) return { kind: 'none' };
      if (x >= g.ruler.x && x < g.wave.x + g.wave.w && y > g.noteTop && y < g.bottom) return { kind: 'ruler', time: this.timeAt(y, state) };
      if (x < g.main.x || x > g.main.x + g.main.w || y < g.noteTop || y > g.bottom) return { kind: 'outside' };
      const lane = this.laneAt(x);
      const t = this.timeAt(y, state), tol = Math.max(10, (g.round ? 25 : 8) * g.s) / state.pxPerSec;
      let best = null, bestD = Infinity;
      for (let i = this.firstVisible(t - tol - 0.001); i < doc.notes.length && doc.notes[i].time <= t + tol; i++) {
        const n = doc.notes[i];
        if (!lanesOf(n, state).some(l => l[0] === lane)) continue;
        const d = Math.abs(n.time - t);
        if (d < bestD) { best = n; bestD = d; }
      }
      if (best) return { kind: 'note', note: best, lane };
      return { kind: 'empty', time: t, lane };
    }

    firstVisible(t) {
      const notes = this.doc.notes;
      let lo = 0, hi = notes.length;
      while (lo < hi) { const mid = (lo + hi) >> 1; if (notes[mid].time < t) lo = mid + 1; else hi = mid; }
      return lo;
    }

    draw(state) {
      const g = this.g, dpr = this.dpr;
      g.setTransform(dpr, 0, 0, dpr, 0, 0);
      g.clearRect(0, 0, this.w, this.h);
      this.drawDots(state);
      if (!this.doc) return;
      const geo = this.layout(state);
      const t0 = this.timeAt(geo.bottom + 20, state), t1 = this.timeAt(geo.noteTop - 20, state);

      this.drawFrame(state);
      if (geo.std) this.drawReference(geo.std, 'standard', state, t0, t1);
      if (geo.alt) this.drawReference(geo.alt, 'alternate', state, t0, t1);
      this.drawMain(state, t0, t1);
      this.drawRuler(state, t0, t1);
      if (geo.wave.w) this.drawWave(state);
      if (geo.gaps.w) this.drawGaps(state, t0, t1);
      this.drawPlayhead(state);
      if (state.box) this.drawBox(state.box);
    }

    /** 背景点阵：跟着谱面滚动。一格一个点画成小图块，整块铺一次（原来每帧画约 4000 个小方块）。 */
    drawDots(state) {
      const dots = this.dotTile();
      if (!dots) return;
      const g = this.g, step = DOT_STEP, k = dots.scale;
      const x0 = state.leftInset % step, y0 = ((state.time * state.pxPerSec) % step + step) % step;
      g.save();
      g.translate(x0, y0); // 图块的原点就是一个点
      g.scale(k, k);
      g.fillStyle = dots.pattern;
      g.fillRect(0, -y0 / k, (this.w - x0) / k, this.h / k);
      g.restore();
    }

    /** 点阵的小图块按屏幕像素画（像素比变了重画），铺的时候缩回一格 DOT_STEP 宽。没有离屏画布时不画点阵。 */
    dotTile() {
      if (this.dots && this.dots.dpr === this.dpr) return this.dots;
      const doc = root.document, size = Math.max(1, Math.round(DOT_STEP * this.dpr));
      if (!doc || !doc.createElement) return null;
      const tile = doc.createElement('canvas');
      tile.width = tile.height = size;
      const tg = tile.getContext('2d');
      tg.fillStyle = COLORS.dot;
      tg.fillRect(0, 0, 1.5 * this.dpr, 1.5 * this.dpr);
      this.dots = { dpr: this.dpr, pattern: this.g.createPattern(tile, 'repeat'), scale: DOT_STEP / size };
      return this.dots;
    }

    /** 整块面板：一张卡片、一行标题，轨道列是游戏面板的底色，时间尺、波形、间隔这几列稍亮，列之间细线隔开。 */
    drawFrame(state) {
      const g = this.g, geo = this.geom, x = geo.frame.x, w = geo.frame.w, y = geo.top, h = geo.bottom - geo.top + 10;
      g.save();
      g.shadowColor = 'rgba(0,0,0,0.5)';
      g.shadowBlur = 28;
      g.shadowOffsetY = 10;
      roundRect(g, x, y, w, h, 12);
      g.fillStyle = COLORS.frame;
      g.fill();
      g.restore();

      g.save();
      roundRect(g, x, y, w, h, 12);
      g.clip();
      g.fillStyle = COLORS.header;
      g.fillRect(x, y, w, geo.header);
      const bodyY = y + geo.header;
      for (const col of [geo.std, geo.main, geo.alt]) if (col) { g.fillStyle = GAME.panel; g.fillRect(col.x, bodyY, col.w, h - geo.header); }
      for (const col of [geo.ruler, geo.wave, geo.gaps]) if (col.w) { g.fillStyle = COLORS.gutter; g.fillRect(col.x, bodyY, col.w, h - geo.header); }
      g.fillStyle = COLORS.divider;
      g.fillRect(x, bodyY - 1, w, 1);
      const edges = [geo.std && geo.std.x + geo.std.w, geo.main.x, geo.main.x + geo.main.w, geo.alt && geo.alt.x];
      for (const e of edges) if (e) g.fillRect(Math.round(e), bodyY, 1, h - geo.header);
      g.restore();

      roundRect(g, x + 0.5, y + 0.5, w - 1, h - 1, 12);
      g.strokeStyle = COLORS.border;
      g.stroke();

      // 标题：像 ComfyUI 节点的标题栏，每列一个
      const title = (col, text, dot, sub) => {
        if (!col) return;
        g.save();
        g.beginPath();
        g.rect(col.x, y, col.w, geo.header);
        g.clip();
        g.fillStyle = dot;
        g.beginPath();
        g.arc(col.x + 14, y + geo.header / 2, 3.5, 0, Math.PI * 2);
        g.fill();
        g.fillStyle = COLORS.text;
        g.font = '600 12.5px ' + FONT;
        g.textAlign = 'left';
        g.textBaseline = 'middle';
        g.fillText(text, col.x + 24, y + geo.header / 2);
        if (sub && col.w > 200) {
          g.fillStyle = COLORS.text3;
          g.font = '11px ' + FONT;
          g.textAlign = 'right';
          g.fillText(sub, col.x + col.w - 12, y + geo.header / 2);
        }
        g.restore();
      };
      title(geo.std, state.t('col.standard'), COLORS.standard);
      title(geo.main, state.t('col.current'), COLORS.accent, state.t('col.left') + ' · ' + state.t('col.right'));
      title(geo.alt, state.t('col.alternate'), COLORS.alternate);
    }

    /** 游戏里一个音符（横条或圆形）：x 是这条轨道的左边，lw 轨道宽，s 是相对 1080p 的缩放。 */
    gameNote(x, lw, s, y, color) {
      const g = this.g;
      if (this.geom.round) {
        const r = 25 * s, rim = Math.max(1, 3 * s), cx = x + lw / 2;
        g.fillStyle = GAME.ballShadow;
        g.beginPath();
        g.arc(cx, y, r + rim / 2, 0, Math.PI * 2);
        g.fill();
        g.fillStyle = color;
        g.beginPath();
        g.arc(cx, y, r, 0, Math.PI * 2);
        g.fill();
        g.strokeStyle = '#ffffff';
        g.lineWidth = rim;
        g.beginPath();
        g.arc(cx, y, r - rim / 2, 0, Math.PI * 2);
        g.stroke();
        g.lineWidth = 1;
        return { x: cx - r, y: y - r, w: r * 2, h: r * 2, round: true, s };
      }
      const inset = 6 * s, w = lw - inset * 2, h = Math.max(5, 16 * s), bx = x + inset, line = Math.max(1, 2 * s);
      roundRect(g, bx, y - h / 2, w, h, 4 * s);
      g.fillStyle = color;
      g.fill();
      roundRect(g, bx + line / 2, y - h / 2 + line / 2, w - line, h - line, 3 * s);
      g.strokeStyle = 'rgba(255,255,255,0.85)';
      g.lineWidth = line;
      g.stroke();
      g.lineWidth = 1;
      return { x: bx, y: y - h / 2, w, h, round: false, s };
    }

    /** 游戏里的长按尾巴（横条：半宽、六成透明；圆形：和球一样宽的胶囊）。 */
    gameHold(x, lw, s, y, y1, color) {
      const g = this.g;
      if (this.geom.round) {
        const r = 25 * s, rim = Math.max(1, 3 * s), w = 2 * (r - rim / 2), cx = x + lw / 2;
        const top = y1 - w / 2, h = y - top, radius = Math.min(w, h) / 2;
        roundRect(g, cx - w / 2, top, w, h, radius);
        g.fillStyle = mixColor(color, GAME.panel, 0.55);
        g.fill();
        g.strokeStyle = color;
        g.lineWidth = rim;
        g.stroke();
        g.lineWidth = 1;
        return;
      }
      const inset = 6 * s, w = lw - inset * 2, alpha = g.globalAlpha;
      g.globalAlpha = alpha * 0.6;
      roundRect(g, x + inset + w * 0.25, y1, w * 0.5, y - y1, 3 * s);
      g.fillStyle = color;
      g.fill();
      g.globalAlpha = alpha;
    }

    /** 轨道分隔线（2 像素）和节拍线（小节 2 像素、拍 1 像素），和游戏一样；end 是画到哪里。 */
    gameLanes(col, lw, s, state, t0, t1, end) {
      const g = this.g, geo = this.geom, keys = geo.keys, width = lw * keys;
      g.save();
      g.beginPath();
      g.rect(col.x, geo.noteTop - 6, col.w, end - geo.noteTop + 6);
      g.clip();
      this.drawLoop(col, width, state);
      g.fillStyle = GAME.sep;
      const sep = Math.max(1, 2 * s);
      // 和游戏一样轨道铺满整列，最外侧两条贴着边画在里面
      for (let i = 0; i <= keys; i++) g.fillRect(Math.min(Math.max(col.lanesX + i * lw - sep / 2, col.lanesX), col.lanesX + width - sep), geo.noteTop - 6, sep, end - geo.noteTop + 6);
      if (state.show.beats) {
        for (const b of this.doc.beats) {
          if (b.time < t0) continue;
          if (b.time > t1) break;
          const y = this.yAt(b.time, state), h = Math.max(1, (b.measure ? 2 : 1) * s);
          g.fillStyle = b.measure ? GAME.measure : GAME.beat;
          g.fillRect(col.lanesX, y - h / 2, width, h);
        }
      }
      g.restore();
    }

    drawLoop(col, width, state) {
      const loop = state.loop;
      if (loop.a === null && loop.b === null) return;
      const g = this.g, geo = this.geom;
      const a = loop.a !== null ? loop.a : -1e9, b = loop.b !== null ? loop.b : 1e9;
      const y1 = this.yAt(a, state), y0 = this.yAt(b, state);
      g.fillStyle = loop.on ? 'rgba(245,120,41,0.08)' : 'rgba(255,255,255,0.03)';
      g.fillRect(col.lanesX, Math.max(geo.noteTop - 6, y0), width, Math.min(geo.bottom, y1) - Math.max(geo.noteTop - 6, y0));
    }

    /** 编辑器自己的标记画在音符外面一圈：选中（白色、轻轻呼吸）、连按（红）、自动补齐（黄虚线）。 */
    mark(box, color, dashed, width) {
      const g = this.g, pad = 3 * box.s + 1;
      if (dashed) g.setLineDash([4, 3]);
      g.strokeStyle = color;
      g.lineWidth = width;
      if (box.round) {
        g.beginPath();
        g.arc(box.x + box.w / 2, box.y + box.h / 2, box.w / 2 + pad, 0, Math.PI * 2);
      } else roundRect(g, box.x - pad, box.y - pad, box.w + pad * 2, box.h + pad * 2, 5 * box.s + pad);
      g.stroke();
      g.setLineDash([]);
      g.lineWidth = 1;
    }

    /** 参考列：一样的游戏画法缩小；和当前手序一样的压暗，不一样的照常亮着。 */
    drawReference(col, which, state, t0, t1) {
      const g = this.g, geo = this.geom, doc = this.doc, lw = geo.refLane, s = geo.sr, refs = doc.refs[which];
      this.gameLanes(col, lw, s, state, t0, t1, geo.bottom);
      // 和中间一样：音符到判定线就收住（圆形样式落进判定区）
      const clipBottom = geo.round ? geo.bottom : geo.playY + 8 * s;
      g.save();
      g.beginPath();
      g.rect(col.x, geo.noteTop - 6, col.w, clipBottom - geo.noteTop + 6);
      g.clip();
      for (let i = this.firstVisible(t0 - 30); i < doc.notes.length && doc.notes[i].time <= t1; i++) {
        const n = doc.notes[i], r = refs.get(n.tile);
        if (!r || n.time + r.lanes.reduce((m, l) => Math.max(m, l[1]), 0) < t0) continue; // 连长按尾巴都过了判定线：看不见
        const differs = r.lastTile !== n.lastTile || r.lanes.map(l => l[0]).join() !== lanesOf(n, state).map(l => l[0]).join();
        const y = this.yAt(n.time, state), color = colorOf(r);
        g.globalAlpha = differs ? 1 : 0.22;
        for (const [lane, hold] of r.lanes) if (hold > 0) this.gameHold(col.lanesX + lane * lw, lw, s, y, this.yAt(n.time + hold, state), color);
        for (const [lane] of r.lanes) this.gameNote(col.lanesX + lane * lw, lw, s, y, color);
      }
      g.globalAlpha = 1;
      g.restore();
      // 参考列也有一条判定线，和中间对齐
      g.fillStyle = 'rgba(0,0,0,0.7)';
      g.fillRect(col.lanesX, geo.playY - 4 * s, lw * geo.keys, 8 * s);
      g.fillStyle = 'rgba(255,255,255,0.75)';
      g.fillRect(col.lanesX, geo.playY - Math.max(1, 2 * s), lw * geo.keys, Math.max(2, 4 * s));
    }

    drawMain(state, t0, t1) {
      const g = this.g, geo = this.geom, doc = this.doc, lw = geo.lane, col = geo.main, s = geo.s, width = lw * geo.keys;
      this.gameLanes(col, lw, s, state, t0, t1, geo.round ? geo.bottom : geo.playY);

      // 判定处的命中光柱（横条样式，从判定线往上淡出）；圆形样式是判定圈
      if (!geo.round) {
        for (let lane = 0; lane < geo.keys; lane++) {
          const f = state.flash[lane], a = f ? 1 - (state.now - f.t) / FLASH_MS : 0;
          if (a <= 0) continue;
          const h = 200 * s, grad = g.createLinearGradient(0, geo.playY, 0, geo.playY - h);
          grad.addColorStop(0, mixAlpha(f.color, 0.45 * a));
          grad.addColorStop(1, mixAlpha(f.color, 0));
          g.fillStyle = grad;
          g.fillRect(col.lanesX + lane * lw, geo.playY - h, lw, h);
        }
      } else this.drawJudgeCircles(state);

      // 音符：横条样式到按键框就消失，圆形样式落进判定圈
      const clipBottom = geo.round ? geo.bottom : geo.playY + 8 * s;
      g.save();
      g.beginPath();
      g.rect(col.x, geo.noteTop - 6, col.w, clipBottom - geo.noteTop + 6);
      g.clip();

      const heads = [];
      for (let i = this.firstVisible(t0 - 30); i < doc.notes.length && doc.notes[i].time <= t1; i++) {
        const n = doc.notes[i], lanes = lanesOf(n, state);
        const hold = lanes.reduce((m, l) => Math.max(m, l[1]), 0);
        if (n.time + hold < t0) continue;
        heads.push([n, lanes]);
      }
      const past = state.time - 0.02;
      for (const [n, lanes] of heads) {
        const y = this.yAt(n.time, state), color = colorOf({ lanes });
        g.globalAlpha = state.playing && n.time < past ? 0.4 : 1;
        for (const [lane, hold] of lanes) if (hold > 0) this.gameHold(col.lanesX + lane * lw, lw, s, y, this.yAt(n.time + hold, state), color);
        g.globalAlpha = 1;
        if (state.show.ghost && n.orig.map(l => l[0]).join() !== lanes.map(l => l[0]).join()) {
          g.setLineDash([3, 3]);
          g.strokeStyle = 'rgba(255,255,255,0.35)';
          for (const [lane] of n.orig) {
            if (lanes.some(l => l[0] === lane)) continue;
            if (geo.round) {
              g.beginPath();
              g.arc(col.lanesX + lane * lw + lw / 2, y, 25 * s, 0, Math.PI * 2);
            } else roundRect(g, col.lanesX + lane * lw + 6 * s, y - 8 * s, lw - 12 * s, 16 * s, 4 * s);
            g.stroke();
          }
          g.setLineDash([]);
        }
      }

      // 圆形样式从远到近画（近的压在上面），和游戏一样
      const order = geo.round ? heads.slice().reverse() : heads;
      for (const [n, lanes] of order) {
        const y = this.yAt(n.time, state), color = colorOf({ lanes });
        const selected = state.selection.has(n.id), hover = state.hover === n.id;
        // 换轨动画：从改之前的轨道滑过来（按轨道顺序一一对应）
        const anim = state.anims && state.anims.get(n.id);
        let progress = 1;
        if (anim) {
          progress = Math.min(1, (state.now - anim.t0) / 170);
          if (progress >= 1) state.anims.delete(n.id);
        }
        const ease = 1 - Math.pow(1 - progress, 3);
        const from = anim && progress < 1 ? anim.from.map(l => l[0]).sort((a, b) => a - b) : null;
        g.globalAlpha = state.playing && n.time < past ? 0.4 : 1;
        for (let k = 0; k < lanes.length; k++) {
          const to = col.lanesX + lanes[k][0] * lw;
          const start = from ? col.lanesX + from[k] * lw : to;
          const box = this.gameNote(start + (to - start) * ease, lw, s, y, color);
          if (hover && !selected) {
            g.fillStyle = 'rgba(255,255,255,0.18)';
            if (box.round) { g.beginPath(); g.arc(box.x + box.w / 2, y, box.w / 2, 0, Math.PI * 2); g.fill(); }
            else { roundRect(g, box.x, box.y, box.w, box.h, 4 * s); g.fill(); }
          }
          if (selected) {
            g.save();
            g.shadowColor = 'rgba(255,255,255,0.7)';
            g.shadowBlur = 12;
            this.mark(box, 'rgba(255,255,255,' + (0.72 + 0.28 * Math.sin(state.now / 260)) + ')', false, 2); // 选中的轻轻呼吸
            g.restore();
          } else if (state.jacks.has(n.id)) this.mark(box, COLORS.danger, false, 2);
          else if (n.auto) this.mark(box, COLORS.warn, true, 1.5);
        }
        g.globalAlpha = 1;
        if (n.orig.map(l => l[0]).join() !== lanes.map(l => l[0]).join()) {
          g.fillStyle = COLORS.ok;
          g.beginPath();
          g.arc(col.lanesX + width + 4, y, 2.5, 0, Math.PI * 2);
          g.fill();
        }
      }
      g.restore();

      if (!geo.round) this.drawJudgeAndKeys(state);
    }

    /** 横条样式的判定线（黑底白线）和按键框：和游戏里一样大小、颜色；播放时命中闪一下，编辑时按轨道键亮一下。 */
    drawJudgeAndKeys(state) {
      const g = this.g, geo = this.geom, s = geo.s, lw = geo.lane, col = geo.main, keys = geo.keys;
      const labels = state.labels || this.doc.keyLabels;
      g.fillStyle = 'rgba(0,0,0,0.7)';
      g.fillRect(col.lanesX, geo.playY - 4 * s, lw * keys, 8 * s);
      g.fillStyle = '#ffffff';
      g.fillRect(col.lanesX, geo.playY - 2 * s, lw * keys, 4 * s);
      for (let lane = 0; lane < keys; lane++) {
        const x = col.lanesX + lane * lw + 4 * s, w = lw - 8 * s, y = geo.keyRow, h = 52 * s;
        const pressed = state.press[lane] !== undefined && state.now - state.press[lane] < 140;
        roundRect(g, x, y, w, h, 6 * s);
        g.fillStyle = pressed ? GAME.keyFillLit : GAME.keyFill;
        g.fill();
        const f = state.flash[lane], a = f ? 1 - (state.now - f.t) / FLASH_MS : 0;
        if (a > 0) {
          g.fillStyle = mixAlpha(f.color, 0.7 * a);
          g.fill();
        }
        roundRect(g, x + s, y + s, w - 2 * s, h - 2 * s, 5 * s);
        g.strokeStyle = pressed ? '#ffffff' : GAME.keyBorder;
        g.lineWidth = 2 * s;
        g.stroke();
        g.lineWidth = 1;
        g.fillStyle = GAME.keyText;
        g.font = '700 ' + Math.round(22 * s) + 'px ' + FONT;
        g.textAlign = 'center';
        g.textBaseline = 'middle';
        g.fillText(labels[lane] || String(lane + 1), x + w / 2, y + h / 2 + 1, w - 4);
      }
    }

    /** 圆形样式的判定区：一条细线串起四个判定圈（深色底、外圈和内圈），命中时染色、外圈扩散，按键时变亮。 */
    drawJudgeCircles(state) {
      const g = this.g, geo = this.geom, s = geo.s, lw = geo.lane, col = geo.main, keys = geo.keys;
      const labels = state.labels || this.doc.keyLabels, r = 29 * s, rim = 3 * s;
      g.fillStyle = GAME.judgeLine;
      g.fillRect(col.lanesX, geo.playY - s, lw * keys, 2 * s);
      for (let lane = 0; lane < keys; lane++) {
        const f = state.flash[lane], a = f ? 1 - (state.now - f.t) / FLASH_MS : 0;
        if (a <= 0) continue;
        const cx = col.lanesX + lane * lw + lw / 2, glow = g.createRadialGradient(cx, geo.playY, 0, cx, geo.playY, r * 2.2);
        glow.addColorStop(0, mixAlpha(f.color, 0.7 * a));
        glow.addColorStop(1, mixAlpha(f.color, 0));
        g.fillStyle = glow;
        g.fillRect(cx - r * 2.2, geo.playY - r * 2.2, r * 4.4, r * 4.4);
      }
      for (let lane = 0; lane < keys; lane++) {
        const cx = col.lanesX + lane * lw + lw / 2, cy = geo.playY;
        const lit = state.press[lane] !== undefined && state.now - state.press[lane] < 140;
        const f = state.flash[lane], a = f ? Math.max(0, 1 - (state.now - f.t) / FLASH_MS) : 0;
        g.fillStyle = lit ? GAME.keyFillLit : GAME.judgeFill;
        g.beginPath();
        g.arc(cx, cy, r, 0, Math.PI * 2);
        g.fill();
        g.lineWidth = rim;
        g.strokeStyle = lit ? '#ffffff' : GAME.judgeRim;
        g.beginPath();
        g.arc(cx, cy, r - rim / 2, 0, Math.PI * 2);
        g.stroke();
        g.lineWidth = rim * 0.75;
        g.strokeStyle = GAME.judgeInner;
        g.beginPath();
        g.arc(cx, cy, r * 36 / 46, 0, Math.PI * 2);
        g.stroke();
        if (a > 0) {
          g.fillStyle = mixAlpha(f.color, 0.55 * a);
          g.beginPath();
          g.arc(cx, cy, r, 0, Math.PI * 2);
          g.fill();
          g.lineWidth = rim;
          g.strokeStyle = mixAlpha(f.color, a);
          g.beginPath();
          g.arc(cx, cy, r + r * 14 / 46 * (1 - a), 0, Math.PI * 2);
          g.stroke();
        }
        g.lineWidth = 1;
        g.fillStyle = GAME.keyText;
        g.font = '700 ' + Math.round(20 * s) + 'px ' + FONT;
        g.textAlign = 'center';
        g.textBaseline = 'middle';
        g.fillText(labels[lane] || String(lane + 1), cx, cy + 1, r * 1.6);
      }
    }

    drawRuler(state, t0, t1) {
      const g = this.g, geo = this.geom, doc = this.doc, col = geo.ruler;
      g.save();
      g.beginPath();
      g.rect(col.x, geo.noteTop - 6, col.w + geo.wave.w, geo.bottom - geo.noteTop + 6);
      g.clip();
      g.textAlign = 'right';
      g.textBaseline = 'middle';
      let measure = 0, lastY = Infinity;
      for (const b of doc.beats) {
        if (b.measure) measure++;
        if (b.time < t0 || !b.measure) continue;
        if (b.time > t1) break;
        const y = this.yAt(b.time, state);
        g.fillStyle = 'rgba(255,255,255,0.08)';
        g.fillRect(col.x + col.w - 6, y - 0.5, 6, 1);
        if (lastY - y < 30) continue;
        lastY = y;
        g.fillStyle = COLORS.text2;
        g.font = '600 11px ' + MONO;
        g.fillText(String(measure), col.x + col.w - 10, y - 6);
        g.fillStyle = COLORS.text3;
        g.font = '10px ' + MONO;
        g.fillText(fmtTime(b.time), col.x + col.w - 10, y + 7);
      }
      if (!doc.beats.length) {
        // 没有节拍线（打开的是文件）：每秒标一次
        for (let s = Math.ceil(t0); s <= t1; s++) {
          g.fillStyle = COLORS.text3;
          g.font = '10px ' + MONO;
          g.fillText(fmtTime(s), col.x + col.w - 10, this.yAt(s, state));
        }
      }
      const loop = state.loop;
      for (const [t, label] of [[loop.a, 'A'], [loop.b, 'B']]) {
        if (t === null) continue;
        const y = this.yAt(t, state);
        g.fillStyle = COLORS.accent;
        roundRect(g, col.x + 6, y - 8, 16, 16, 4);
        g.fill();
        g.fillStyle = '#1a0d03';
        g.font = '700 10px ' + MONO;
        g.textAlign = 'center';
        g.fillText(label, col.x + 14, y + 0.5);
        g.textAlign = 'right';
      }
      g.restore();
    }

    drawWave(state) {
      const audio = state.waveform;
      if (!audio || !audio.peaks.length || !audio.perSecond) return;
      const g = this.g, geo = this.geom, col = geo.wave, offset = state.audioOffset;
      const cx = col.x + col.w / 2, half = col.w / 2 - 4;
      g.save();
      g.beginPath();
      g.rect(col.x, geo.noteTop - 6, col.w, geo.bottom - geo.noteTop + 6);
      g.clip();
      const step = Math.max(1, Math.floor(audio.perSecond / state.pxPerSec));
      for (let y = geo.noteTop - 6; y < geo.bottom; y += 2) {
        const t = this.timeAt(y, state) + offset;
        const i = Math.floor(t * audio.perSecond);
        if (i < 0 || i >= audio.peaks.length) continue;
        let v = 0;
        for (let k = 0; k < step && i + k < audio.peaks.length; k++) v = Math.max(v, audio.peaks[i + k]);
        const w = (v / 255) * half;
        g.fillStyle = y > geo.playY ? 'rgba(245,120,41,0.5)' : 'rgba(170,180,200,0.32)';
        g.fillRect(cx - w, y, w * 2, 1.5);
      }
      g.restore();
    }

    drawGaps(state, t0, t1) {
      const g = this.g, geo = this.geom, doc = this.doc, col = geo.gaps, model = root.FingeringModel;
      g.save();
      g.beginPath();
      g.rect(col.x, geo.noteTop - 6, col.w, geo.bottom - geo.noteTop + 6);
      g.clip();
      g.textAlign = 'left';
      g.textBaseline = 'middle';
      let lastY = Infinity;
      for (let i = Math.max(1, this.firstVisible(t0)); i < doc.notes.length && doc.notes[i - 1].time <= t1; i++) {
        const a = doc.notes[i - 1], b = doc.notes[i];
        const ya = this.yAt(a.time, state), yb = this.yAt(b.time, state);
        if (ya - yb < 13) continue;
        const y = (ya + yb) / 2;
        if (lastY - y < 13) continue;
        lastY = y;
        const la = lanesOf(a, state), lb = lanesOf(b, state);
        const gap = b.start - a.start, jack = state.jacks.has(a.id) && state.jacks.has(b.id) && la.some(x => lb.some(z => z[0] === x[0]));
        g.fillStyle = jack ? COLORS.danger : COLORS.text3;
        g.font = (jack ? '600 ' : '') + '10.5px ' + MONO;
        const value = model.noteValue(doc, b.start, gap);
        g.fillText(Math.round(gap * 1000) + (value ? ' · ' + value : '') + (jack ? ' ' + state.t('gap.jack') : ''), col.x + 12, y);
        g.fillStyle = jack ? 'rgba(239,91,91,0.5)' : 'rgba(255,255,255,0.09)';
        g.fillRect(col.x + 5, yb + 3, 1, ya - yb - 6);
      }
      g.restore();
    }

    /** 播放头：时间尺和间隔列里是橙线，轨道里是游戏的判定线。 */
    drawPlayhead(state) {
      const g = this.g, geo = this.geom;
      g.save();
      g.shadowColor = 'rgba(245,120,41,0.8)';
      g.shadowBlur = 8;
      g.fillStyle = COLORS.accent;
      g.fillRect(geo.ruler.x, geo.playY - 1, geo.ruler.w + geo.wave.w, 2);
      if (geo.gaps.w) g.fillRect(geo.gaps.x, geo.playY - 1, geo.gaps.w, 2);
      g.restore();
      const label = fmtTime(state.time) + '.' + String(Math.floor(Math.abs(state.time % 1) * 1000)).padStart(3, '0');
      g.font = '600 10.5px ' + MONO;
      const w = g.measureText(label).width + 12;
      roundRect(g, geo.ruler.x + geo.ruler.w - w - 4, geo.playY - 9, w, 18, 9);
      g.fillStyle = COLORS.accent;
      g.fill();
      g.fillStyle = '#1a0d03';
      g.textAlign = 'center';
      g.textBaseline = 'middle';
      g.fillText(label, geo.ruler.x + geo.ruler.w - w / 2 - 4, geo.playY + 0.5);
    }

    drawBox(box) {
      const g = this.g;
      const x = Math.min(box.x0, box.x1), y = Math.min(box.y0, box.y1), w = Math.abs(box.x1 - box.x0), h = Math.abs(box.y1 - box.y0);
      g.fillStyle = 'rgba(245,120,41,0.08)';
      g.fillRect(x, y, w, h);
      g.strokeStyle = 'rgba(245,120,41,0.8)';
      g.setLineDash([4, 3]);
      g.strokeRect(x + 0.5, y + 0.5, w, h);
      g.setLineDash([]);
    }

    /** 框里的音符（只看当前手序那一列）。 */
    notesInBox(box, state) {
      const geo = this.geom, doc = this.doc;
      const x0 = Math.min(box.x0, box.x1), x1 = Math.max(box.x0, box.x1);
      const ta = this.timeAt(Math.max(box.y0, box.y1), state), tb = this.timeAt(Math.min(box.y0, box.y1), state);
      const out = [];
      for (let i = this.firstVisible(ta); i < doc.notes.length && doc.notes[i].time <= tb; i++) {
        const n = doc.notes[i];
        if (n.lanes.some(([lane]) => {
          const a = geo.main.lanesX + lane * geo.lane + 5, b = a + geo.lane - 10;
          return b >= x0 && a <= x1;
        })) out.push(n.id);
      }
      return out;
    }
  }

  root.ChartView = ChartView;
  root.ChartColors = COLORS;
  root.ChartColorOf = colorOf;
})(typeof globalThis !== 'undefined' ? globalThis : this);
