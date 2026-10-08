import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import vm from 'node:vm';

const source = await readFile(new URL('../public/assets/compatibility.js', import.meta.url), 'utf8');
// This retained controller uses the legacy DOM; the Apple page has its own runtime.
const html = await readFile(new URL('fixtures/compatibility-legacy.html', import.meta.url), 'utf8');
const hardware = JSON.parse(await readFile(new URL('../public/assets/hardware-reference.json', import.meta.url), 'utf8'));
const requirements = { provider: 'Steam Store', status: 'available', reason: null,
  sourceUrl: 'https://store.steampowered.com/app/1091500/', sourceRetrievedAt: '2026-01-01T00:00:00.000Z', sourceSha256: 'a'.repeat(64),
  minimum: { text: 'Fixture requirements only', os: 'Fixture OS', processor: null, memory: null, graphics: null, directX: null, storage: null, additionalNotes: null, memoryMb: null, storageMb: null }, recommended: null };
const game = { id: 'steam-1091500', name: 'Cyberpunk 2077', steamAppId: '1091500', catalog: null };
const detail = { game, requirements, modCompatibility: [], gpus: [], tests: [], counts: { success: 0, partial: 0, failure: 0 }, status: 'untested' };
const listing = { items: [{ game, counts: detail.counts, status: 'untested', evidence: { hasRequirements: true, modStatus: null } }], total: 1, page: 1, pageSize: 20, catalogTotal: 2001 };
const flush = async () => { for (let i = 0; i < 6; i++) await new Promise(resolve => setImmediate(resolve)); };
class Element {
  constructor(tag = 'div') { this.tag = tag; this.children = []; this.dataset = {}; this.listeners = {}; this.attributes = {}; this.value = ''; this.hidden = false; this.type = 'text'; this.maxLength = 160; this.ownText = ''; }
  set textContent(text) { this.ownText = String(text); this.children = []; }
  get textContent() { return this.ownText + this.children.map(child => child.textContent).join(' '); }
  append(...nodes) { this.children.push(...nodes); }
  replaceChildren(...nodes) { this.children = nodes; this.ownText = ''; }
  setAttribute(key, value) { this.attributes[key] = value; }
  addEventListener(event, callback) { (this.listeners[event] ||= []).push(callback); }
  querySelectorAll() { return this.children.flatMap(child => [...(child.tag === 'button' && child.dataset.game ? [child] : []), ...child.querySelectorAll()]); }
  fire(name, event = {}) { for (const fn of this.listeners[name] || []) fn({ preventDefault() {}, ...event }); }
  checkValidity() { return this.type !== 'number' || this.value === '' || (Number.isFinite(Number(this.value)) && Number(this.value) >= Number(this.min || 0) && Number(this.value) <= Number(this.max || Infinity)); }
  reportValidity() { return this.checkValidity(); }
  focus() {} select() {}
}
async function setup({ url = 'https://example.test/compatibility?game=steam-1091500', failHardware = false, detailResponse = detail, clipboard } = {}) {
  const elements = new Map([...html.matchAll(/<([a-z][a-z0-9]*)\b[^>]*\bid="([^"]+)"[^>]*>/g)].map(match => {
    const element = new Element(match[1]);
    for (const key of ['type', 'min', 'max', 'maxlength']) { const value = match[0].match(new RegExp(key + '="([^"]+)"'))?.[1]; if (value) element[key === 'maxlength' ? 'maxLength' : key] = value; }
    return [match[2], element];
  }));
  const location = { href: url, get search() { return new URL(this.href).search; } };
  const requests = [], evaluations = [], listeners = {};
  const window = { isSecureContext: true, addEventListener(event, fn) { listeners[event] = fn; }, MuHardwareCheck: {
    matchHardware: () => ({ vramMb: 8192 }),
    evaluate(req, profile) { evaluations.push({ req, profile }); return { status: 'incomplete', title: 'Fixture hardware result', summary: 'Fixture only', checks: [], sources: hardware.sources }; }
  } };
  const context = { window, location, history: Object.fromEntries(['pushState', 'replaceState'].map(key => [key, (_state, _title, value) => { location.href = value.href; }])),
    document: { getElementById: id => elements.get(id), createElement: tag => new Element(tag), createDocumentFragment: () => new Element('fragment') },
    navigator: { clipboard }, URL, URLSearchParams, AbortController, setTimeout, clearTimeout, console,
    fetch: async (path, options) => {
      requests.push({ path, options });
      if (path.startsWith('/assets/')) return { ok: !failHardware, status: failHardware ? 503 : 200, json: async () => hardware };
      if (path.includes('/games/')) return { ok: detailResponse !== 403, status: detailResponse === 403 ? 403 : 200, json: async () => detailResponse };
      return { ok: true, json: async () => listing };
    }
  };
  vm.runInNewContext(source, context); await flush();
  return { elements, location, requests, evaluations, listeners };
}
test('profile URL restores all fields without sending hardware to either API query', async () => {
  const app = await setup({ url: 'https://example.test/compatibility?q=Cyberpunk&game=steam-1091500&gpu=Radeon%20RX%207900%20XT&cpu=Ryzen%207%207800X3D&ram=16&vram=100&os=Windows%2011&dx=12&storage=150' });
  assert.equal(app.elements.get('compat-ram').value, '16');
  assert.equal(app.elements.get('compat-os').value, 'Windows 11');
  assert.ok(app.evaluations.length > 0);
  assert.equal(app.evaluations.at(-1).profile.vramGb, undefined, 'known VRAM cannot be overridden');
  for (const request of app.requests.filter(item => item.path.startsWith('/api/'))) {
    assert.doesNotMatch(request.path, /gpu=|cpu=|ram=|vram=|os=|storage=/);
    assert.equal(request.options.referrerPolicy, 'no-referrer');
  }
  const rendered = app.elements.get('compat-detail-content').textContent;
  assert.match(rendered, /Steam 官方配置要求/);
  assert.doesNotMatch(rendered, /0 次实测|历史用户环境记录|待验证/);
});
test('profile changes re-evaluate locally, preserve unapplied query, and omit invalid numeric fields', async () => {
  const app = await setup({ url: 'https://example.test/compatibility?q=Cyberpunk&game=steam-1091500' });
  const before = app.requests.length;
  app.elements.get('compat-q').value = 'new unsubmitted query';
  app.elements.get('compat-ram').value = '999999';
  app.elements.get('compat-ram').fire('change');
  app.elements.get('compat-cpu').value = 'Ryzen 7 7800X3D';
  app.elements.get('compat-cpu').fire('change');
  assert.equal(new URL(app.location.href).searchParams.get('q'), 'Cyberpunk');
  assert.equal(new URL(app.location.href).searchParams.get('ram'), null);
  assert.equal(new URL(app.location.href).searchParams.get('cpu'), 'Ryzen 7 7800X3D');
  assert.equal(app.requests.length, before);
});
test('hardware download failure leaves official requirements and share available', async () => {
  const app = await setup({ failHardware: true });
  assert.match(app.elements.get('compat-detail-content').textContent, /Steam 官方配置要求/);
  assert.equal(app.elements.get('compat-share').hidden, false);
  assert.equal(app.elements.get('compat-gpu-retry').hidden, false);
});
test('forbidden service response is a visible error rather than no requirements', async () => {
  const app = await setup({ detailResponse: 403 });
  assert.match(app.elements.get('compat-detail-state').textContent, /暂未开放/);
  assert.equal(app.elements.get('compat-detail-content').textContent, '');
  assert.equal(app.elements.get('compat-share').hidden, true);
});
test('source URI and detail identity validation reject untrusted substitutions', async () => {
  const wrong = await setup({ detailResponse: { ...detail, game: { ...game, id: 'wrong' } } });
  assert.equal(wrong.elements.get('compat-detail-retry').hidden, false);
  const bad = await setup({ detailResponse: { ...detail, requirements: { ...requirements, sourceUrl: 'javascript:alert(1)' } } });
  assert.equal(bad.elements.get('compat-detail-retry').hidden, false);
  assert.equal(bad.elements.get('compat-detail-content').textContent, '');
});
test('clipboard completion after hardware navigation cannot report a stale link', async () => {
  let release;
  const app = await setup({ clipboard: { writeText: () => new Promise(resolve => { release = resolve; }) } });
  app.elements.get('compat-share').fire('click');
  app.elements.get('compat-ram').value = '32'; app.elements.get('compat-ram').fire('change');
  release(); await flush();
  assert.equal(app.elements.get('compat-share-state').textContent, '');
});

test('changing the game query aborts its previous detail and ignores a late response', async () => {
  let release;
  const delayed = new Promise(resolve => { release = resolve; });
  const app = await setup({ detailResponse: delayed });
  const oldRequest = app.requests.find(request => request.path.includes('/games/'));
  assert.ok(oldRequest);
  app.elements.get('compat-q').value = 'another game';
  app.elements.get('compat-search').fire('submit');
  await flush();
  assert.equal(oldRequest.options.signal.aborted, true);
  release(detail); await flush();
  assert.equal(app.elements.get('compat-detail-content').textContent, '');
  assert.equal(app.elements.get('compat-share').hidden, true);
  assert.equal(new URL(app.location.href).searchParams.get('game'), null);
});
