import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { createHash } from 'node:crypto';
import { mkdtemp, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { publicPages, dlssFaq, renderSeoPage, siteOrigin } from '../seo.mjs';

test('verification tokens cannot inject markup and structured data matches visible FAQ answers', async () => {
  const source = await readFile(new URL('../public/dlss5.html', import.meta.url), 'utf8');
  const { html, scriptHash } = renderSeoPage(source, '/dlss5', { google: '\"><script>alert(1)</script>', baidu: 'baidu-test' });
  assert.ok(!html.includes('<script>alert(1)</script>'));
  assert.match(html, /name="google-site-verification" content="&quot;&gt;&lt;script&gt;/);
  assert.match(html, /name="baidu-site-verification" content="baidu-test"/);
  const json = html.match(/<script type="application\/ld\+json">([^]*?)<\/script>/)[1];
  assert.equal(scriptHash, createHash('sha256').update(json).digest('base64'));
  const faq = JSON.parse(json)['@graph'].find(item => item['@type'] === 'FAQPage');
  assert.equal(faq.mainEntity.length, dlssFaq.length);
  for (const [question, answer] of dlssFaq) {
    assert.ok(html.includes(`<button class="faq-q">${question}<span>`));
    assert.ok(html.includes(`<div class="faq-a">${answer}</div>`));
    assert.ok(faq.mainEntity.some(item => item.name === question && item.acceptedAnswer.text === answer));
  }
});

test('HTTP crawl contract: sitemap, metadata, bots, redirects, HEAD and protected routes', async t => {
  const data = await mkdtemp(join(tmpdir(), 'mu-seo-test-'));
  const child = spawn(process.execPath, [new URL('../server.mjs', import.meta.url).pathname], {
    env: { ...process.env, PORT: '0', HOST: '127.0.0.1', PUBLIC_ROOT: new URL('../public', import.meta.url).pathname,
      DATA_ROOT: data, GITHUB_SYNC_DISABLED: '1', ADMIN_PASSWORD_HASH: '', GOOGLE_SITE_VERIFICATION: '', BAIDU_SITE_VERIFICATION: '' },
    stdio: ['ignore', 'pipe', 'pipe']
  });
  t.after(async () => { const exited = once(child, 'exit'); child.kill(); await exited; await rm(data, { recursive: true, force: true }); });
  // Discover the actual ephemeral port without reserving/releasing a potentially racy port.
  let log = '';
  const base = await new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error('Server startup timed out: ' + log)), 10000);
    child.on('error', reject);
    child.stderr.on('data', chunk => { log += chunk; });
    child.stdout.on('data', chunk => {
      log += chunk;
      const match = log.match(/website listening on 127\.0\.0\.1:(\d+)/);
      if (match) { clearTimeout(timer); resolve(`http://127.0.0.1:${match[1]}`); }
    });
  });
  const get = (path, options) => fetch(base + path, { redirect: 'manual', ...options });
  const sitemap = await get('/sitemap.xml');
  assert.equal(sitemap.status, 200);
  assert.match(sitemap.headers.get('content-type'), /application\/xml/);
  const xml = await sitemap.text();
  const locations = [...xml.matchAll(/<loc>(.*?)<\/loc>/g)].map(match => match[1]);
  assert.deepEqual(locations, [...publicPages.keys()].map(path => siteOrigin + path));
  assert.ok(!xml.includes('lastmod'));
  for (const [path, page] of publicPages) {
    const response = await get(path + '?utm_source=test', { headers: { 'User-Agent': 'Baiduspider' } });
    assert.equal(response.status, 200, path);
    assert.equal(response.headers.get('x-robots-tag'), null);
    const html = await response.text();
    assert.ok(html.includes('apple-runtime.js'), path);
    assert.equal([...html.matchAll(/<title>/g)].length, 1);
    assert.equal([...html.matchAll(/name="description"/g)].length, 1);
    assert.equal([...html.matchAll(/rel="canonical"/g)].length, 1);
    assert.ok(html.includes(`<title>${page.title}</title>`));
    assert.ok(html.includes(`rel="canonical" href="${siteOrigin}${path}"`));
    assert.match(html, /name="robots" content="index,follow/);
    assert.match(html, /href="\/dlss5"/);
    const json = html.match(/<script type="application\/ld\+json">([^]*?)<\/script>/)[1];
    assert.ok(JSON.parse(json)['@graph'].length >= 2);
    assert.ok(response.headers.get('content-security-policy').includes(`'sha256-${createHash('sha256').update(json).digest('base64')}'`));
    assert.ok(!response.headers.get('content-security-policy').includes('unsafe-inline'));
    assert.equal(Number(response.headers.get('content-length')), Buffer.byteLength(html));
    const head = await get(path, { method: 'HEAD' });
    assert.equal(head.status, 200);
    assert.equal(Number(head.headers.get('content-length')), Buffer.byteLength(html));
    assert.equal(await head.text(), '');
    for (const alias of [`/${page.file}`, ...(path === '/' ? [] : [path + '/'])]) {
      const redirect = await get(alias + '?q=test');
      assert.equal(redirect.status, 301, alias);
      assert.equal(redirect.headers.get('location'), path + '?q=test');
    }
  }
  const normal = await (await get('/dlss5')).text();
  for (const agent of ['Googlebot', 'Baiduspider', 'bingbot', 'OAI-SearchBot', 'Mozilla/5.0']) {
    assert.equal(await (await get('/dlss5', { headers: { 'User-Agent': agent } })).text(), normal);
  }
  const robots = await (await get('/robots.txt')).text();
  assert.match(robots, /User-agent: \*\nAllow: \//);
  assert.ok(robots.includes(`Sitemap: ${siteOrigin}/sitemap.xml`));
  assert.ok(!robots.includes('Disallow: /\n'));
  const llms = await get('/llms.txt');
  assert.equal(llms.status, 200);
  assert.match(llms.headers.get('content-type'), /text\/plain/);
  assert.match(await llms.text(), /大力水手5/);
  for (const path of ['/admin', '/admin.html', '/api/health']) {
    const response = await get(path);
    assert.equal(response.status, 200);
    assert.equal(response.headers.get('x-robots-tag'), 'noindex, nofollow');
  }
  assert.equal((await get('/definitely-not-a-page')).status, 404);
  assert.equal((await get('/api/admin/status')).status, 503);
  assert.equal((await get('/sitemap.xml', { method: 'POST' })).status, 405);
  assert.equal(await (await get('/sitemap.xml', { method: 'HEAD' })).text(), '');
});
