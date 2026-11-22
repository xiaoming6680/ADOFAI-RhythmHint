// 编辑器五种语言：每句都有五种译文、占位符一致；界面代码和页面里用到的键都存在、表里没有用不到的键；界面代码里没有写死的中日韩文字。
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { STRINGS, LANGUAGES, create, fromBrowser } = require('../i18n.js');

const dir = path.join(__dirname, '..');
const read = name => fs.readFileSync(path.join(dir, name), 'utf8');
const placeholders = s => (s.match(/\{\d+\}/g) || []).sort().join();
// 编辑器的全部脚本（i18n.js 是译文表本身，不算）
const SCRIPTS = fs.readdirSync(dir).filter(f => f.endsWith('.js') && f !== 'i18n.js').sort();
const PREFIXES = new Set(Object.keys(STRINGS).map(k => k.split('.')[0]));

/**
 * 用到的键：页面上 data-i18n、data-i18n-title、data-i18n-placeholder 的值，脚本里所有「前缀在表里、形如 前缀.键」的字符串
 * （不只认 t('…') 的写法：三元表达式里的、数组里的、驼峰前缀的 saveAs.done 都算），再加上拼出来的键。
 */
function usedKeys() {
  const used = new Set();
  for (const m of read('index.html').matchAll(/data-i18n(?:-title|-placeholder)?="([^"]+)"/g)) used.add(m[1]);
  for (const file of SCRIPTS) {
    for (const m of read(file).matchAll(/(['"`])([a-zA-Z]+\.[A-Za-z0-9]+)\1/g)) if (PREFIXES.has(m[2].split('.')[0])) used.add(m[2]);
  }
  // 拼出来的键：'kind.' + 键数（app.js 检查器），'err.' + FingeringError 的代码（app.js errorText，代码从 model.js 里找）
  [1, 2, 3, 4, 5, 6, 7, 8].forEach(n => used.add('kind.' + n));
  for (const m of read('model.js').matchAll(/(?:need\([^\n]*?,\s*|new FingeringError\()'([a-zA-Z]+)'/g)) used.add('err.' + m[1]);
  return used;
}

test('every sentence has all five languages with the same placeholders', () => {
  for (const [key, row] of Object.entries(STRINGS)) {
    assert.equal(row.length, LANGUAGES.length, key);
    row.forEach((text, i) => {
      assert.ok(typeof text === 'string' && text.trim().length > 0, key + ' #' + i);
      assert.equal(placeholders(text), placeholders(row[0]), key + ' #' + i);
    });
  }
});

test('keys used by the page and the code exist', () => {
  const used = usedKeys();
  assert.ok(used.has('saveAs.done') && used.has('tr.pause') && used.has('save.pickBodyInbox') && used.has('keys.loop'), 'the scan finds keys outside t(…)');
  assert.ok(used.has('err.notFingering') && used.has('err.notSession'), 'error codes are found');
  assert.deepEqual([...used].filter(k => !STRINGS[k]), []);
});

test('every key in the table is used somewhere', () => {
  const used = usedKeys();
  assert.deepEqual(Object.keys(STRINGS).filter(k => !used.has(k)), []);
});

test('no hard-coded Chinese, Japanese or Korean in the interface code and the page', () => {
  const CJK = /[぀-ヿ㐀-鿿가-힯]/;
  for (const file of SCRIPTS) {
    const code = read(file).split('\n').filter(line => !/^\s*(\/\/|\*|\/\*)/.test(line)).map(line => line.replace(/\/\/.*$/, '').replace(/\/\*.*?\*\//g, ''));
    const hits = code.filter(line => new RegExp('[\'"`][^\'"`]*' + CJK.source + '[^\'"`]*[\'"`]').test(line));
    assert.deepEqual(hits, [], file);
  }
  const page = read('index.html').replace(/<!--[\s\S]*?-->/g, '').split('\n').filter(line => CJK.test(line));
  assert.deepEqual(page, [], 'index.html');
});

test('language choice and formatting', () => {
  assert.equal(fromBrowser('zh-CN'), 'zh-Hans');
  assert.equal(fromBrowser('zh-TW'), 'zh-Hant');
  assert.equal(fromBrowser('ko-KR'), 'ko');
  assert.equal(fromBrowser('ja'), 'ja');
  assert.equal(fromBrowser('fr-FR'), 'en');
  assert.equal(create('en')('insp.count', 3), '3 notes selected');
  assert.equal(create('xx')('insp.count', 3), '3 notes selected');
});
