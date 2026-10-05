import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { publicPages, siteOrigin, dlssFaq } from '../seo.mjs';

const base = new URL(process.argv[2] || siteOrigin).origin;
let checks = 0;
const check = (condition, message) => { assert.ok(condition, message); checks++; };
const get = (path, options = {}) => fetch(base + path, { redirect: 'manual', signal: AbortSignal.timeout(20000), ...options });
for (const [path, page] of publicPages) {
  const response = await get(path);
  check(response.status === 200, `GET ${path}`);
  const html = await response.text();
  check(html.includes(`<title>${page.title}</title>`), `title ${path}`);
  check(html.includes(`rel="canonical" href="${siteOrigin}${path}"`), `canonical ${path}`);
  check(!response.headers.get('x-robots-tag')?.includes('noindex'), `indexable ${path}`);
  check(/name="robots" content="index,follow/.test(html), `robots ${path}`);
  const json = html.match(/<script type="application\/ld\+json">([^]*?)<\/script>/)?.[1];
  check(Boolean(json) && JSON.parse(json)['@graph'].length >= 2, `JSON-LD ${path}`);
  check(response.headers.get('content-security-policy')?.includes(`'sha256-${createHash('sha256').update(json).digest('base64')}'`), `CSP ${path}`);
  check(html.includes('href="/dlss5"'), `topic link ${path}`);
  const alias = await get(`/${page.file}?utm_source=seo-check`);
  check(alias.status === 301 && alias.headers.get('location') === path + '?utm_source=seo-check', `alias ${path}`);
}
const map = await get('/sitemap.xml');
check(map.status === 200 && /application\/xml/.test(map.headers.get('content-type')), 'sitemap response');
const xml = await map.text();
const urls = [...xml.matchAll(/<loc>(.*?)<\/loc>/g)].map(match => match[1]);
check(JSON.stringify(urls) === JSON.stringify([...publicPages.keys()].map(path => siteOrigin + path)), 'sitemap canonical URLs');
const robots = await (await get('/robots.txt')).text();
check(robots.includes(`Sitemap: ${siteOrigin}/sitemap.xml`), 'robots sitemap declaration');
check(robots.includes('User-agent: *\nAllow: /'), 'public crawling allowed');
check(!robots.includes('Disallow: /\n'), 'no global crawl denial');
const llms = await get('/llms.txt');
check(llms.status === 200 && (await llms.text()).includes('/dlss5'), 'AI reference index');
const reference = await (await get('/dlss5')).text();
for (const agent of ['Googlebot', 'Baiduspider', 'bingbot', 'OAI-SearchBot']) {
  const response = await get('/dlss5', { headers: { 'User-Agent': agent } });
  check(response.status === 200 && await response.text() === reference, `same public content for ${agent}`);
}
for (const [question, answer] of dlssFaq) check(reference.includes(`<h3>${question}</h3><p>${answer}</p>`), `visible answer ${question}`);
const admin = await get('/admin');
check(admin.headers.get('x-robots-tag')?.includes('noindex'), 'admin noindex');
check((await get('/seo-missing-page-check')).status === 404, 'real 404');
const head = await get('/dlss5', { method: 'HEAD' });
check(head.status === 200 && await head.text() === '', 'HEAD without body');
const release = await (await get('/release.json')).json();
check(release.channel === 'stable', 'stable release preserved');
console.log(JSON.stringify({ base, checks, pages: publicPages.size, release: release.tag, sha256: release.sha256,
  scope: 'HTTP and HTML checks; actual search indexing and real crawler-IP access are not established' }, null, 2));
