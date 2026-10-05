# Search and AI discovery

Production origin: https://amd-dlss-mu.claude-api.cn

## Implementation

- `seo.mjs` owns seven public canonical URLs, page titles/descriptions, Open Graph metadata, JSON-LD, FAQ answers and the XML sitemap. Metadata is rendered into the initial HTTP HTML response for every visitor. There is no user-agent-specific content.
- `/dlss5` covers DLSS5, DLSS 5, 大力水手5 and 大力水手 5. Homepage, download, guide and compatibility pages each address a different intent; avoid creating duplicate keyword landing pages.
- `/sitemap.xml` includes only canonical public HTML pages. No invented modification dates, search filters, binaries, admin URLs or API URLs are included.
- Known `.html` aliases and trailing slashes use permanent 301 redirects, retaining query parameters. Canonical URLs omit search and campaign parameters.
- Public HTML has `index,follow` and snippet permissions. Admin/API/binary responses carry `X-Robots-Tag: noindex, nofollow`; private APIs still require authentication. Robots exclusion is not access control. Admin HTML remains crawlable to expose noindex.
- The wildcard robots policy permits ordinary web crawlers including Googlebot, Baiduspider, Bingbot and OAI-SearchBot on public content. CDN rules may still affect real crawlers and must be checked separately.
- `/llms.txt` provides a compact, linked product facts index. This is supplementary documentation, not a ranking mechanism or a promise that an AI service reads it.
- JSON-LD uses WebSite, SoftwareApplication, WebPage, BreadcrumbList and FAQPage. FAQ markup and visible answers share one source. No invented ratings, reviews or performance guarantees are emitted. Google may not display FAQ/software rich results for this site; semantic markup alone does not establish eligibility.
- A SHA-256 CSP hash permits only the emitted JSON-LD block. Inline executable scripts remain blocked. Text is visible when JavaScript is disabled; viewport reveal animations are progressive enhancements.
- Static CSS/JS references receive a version suffix at render time to avoid stale cached reveal styles and title-overwriting JavaScript.

## Ownership verification and submission

Google ownership verification was completed on 2026-09-28 through the owner's existing browser session and an HTML meta tag. The production token is stored in the server environment, not committed to this repository. Search Console accepted `/sitemap.xml`, reported `成功`, and discovered all seven pages. Sitemap discovery does not establish that the pages are indexed or ranked. Baidu remains unverified; the owner explicitly deferred Baidu login and requested Google first.

1. Google Search Console: add the URL-prefix property `https://amd-dlss-mu.claude-api.cn/`. Choose HTML meta-tag verification and set only its `content` value as `GOOGLE_SITE_VERIFICATION` in the production environment. A domain property instead requires the DNS TXT record issued by Google.
2. 百度搜索资源平台: add this site and choose HTML-tag verification where available. Set only its `content` value as `BAIDU_SITE_VERIFICATION` in the production environment. Use exactly the domain and verification method the platform offers.
3. Recreate the website container after environment changes; check the live homepage source, then complete verification in each owner's platform session. Do not commit verification credentials or fabricate tokens.
4. Submit `https://amd-dlss-mu.claude-api.cn/sitemap.xml` in Search Console. In Baidu use the currently available link/sitemap submission entry for the verified site; availability and quotas depend on the account. No deprecated Google sitemap ping or Indexing API for ordinary pages is used.
5. Inspect `/`, `/dlss5`, `/download` and `/guide` using the platforms' URL/crawl diagnostics. Confirm there is no Cloudflare challenge or CDN bot block. User-Agent simulation tests only public HTTP responses; they do not prove access from crawler IP ranges.
6. Follow impressions, indexing reasons and queries such as `dlss5`, `大力水手5`, `AMD DLSS5`, `大力水手5 下载`, and `DLSS5 安装教程` after crawl processing. No search volume, ranking or acquisition forecast has been fabricated.

## Verification and deployment

```sh
node --test website/tests/*.test.mjs
node website/deploy/verify-seo.mjs http://127.0.0.1:8096
node website/deploy/verify-seo.mjs https://amd-dlss-mu.claude-api.cn
```

The existing `prepare-website-patch.mjs` and `activate-website-compatibility.sh` pipeline now includes the SEO module, content and assets. Obtain a fresh live source baseline; review its manifest before activation. It verifies source/dependency hashes, builds before replacing files, keeps an image/source backup and rolls back on failed health/content checks. Runtime `data`, release metadata and existing downloads are outside the patch payload. Retain the printed bundle and backup path for rollback.

## Primary references

- [Google AI features and your website](https://developers.google.com/search/docs/appearance/ai-features): ordinary indexability, visible content and matched structured data remain the foundation; no special AI text file is required.
- [Google sitemap submission](https://developers.google.com/search/docs/crawling-indexing/sitemaps/build-sitemap).
- [OpenAI crawler documentation](https://developers.openai.com/api/docs/bots): OAI-SearchBot governs search discovery separately from GPTBot training crawling.
- [百度搜索资源平台](https://ziyuan.baidu.com/).

## Deployment evidence: 2026-09-28

- Activated on the production website through the existing hash-checked patch workflow. The 21-file manifest contained 11 changed files; all four runtime dependencies matched the live baseline. The server, container and public HTML were checked after activation.
- `node --test website/tests/*.test.mjs`: 44 passed, 0 failed.
- `verify-seo.mjs`: 83 checks passed locally and 83 on the public HTTPS origin, across seven public pages. Four crawler User-Agent simulations received the same content as ordinary requests.
- Browser checks: public `/dlss5` shows the correct title, canonical URL and six visible FAQ answers; local 390px and 1280px layouts had no document overflow in the checked views. Homepage body remained visible with JavaScript execution disabled. Browser overrides were restored.
- Public `/download/file` HEAD: 200, 201095999 bytes, ETag `f30315f9bdef2701c4a6d91c96e0dc43a6d8ad26152a01441e24ac0e9e26fd95`. Release metadata remained `v2.0.1`, `stable`. This deployment did not replace the EXE.
- Server backup: `/home/xrw/amd-dlss-mu-site/backups/website-compatibility-20260928T020306Z-1281322`.
- Rollback command on the server: `bash /tmp/mu-seo-20260928-patch/activate-website-compatibility.sh /home/xrw/amd-dlss-mu-site --rollback /home/xrw/amd-dlss-mu-site/backups/website-compatibility-20260928T020306Z-1281322`.
- At initial deployment, search platform verification/submission and actual crawler-IP reachability had not been established. The follow-up Google verification and sitemap result are recorded below; actual indexing, ranking and AI citations remain separate outcomes.

## Google Search Console follow-up: 2026-09-28

- URL-prefix property: `https://amd-dlss-mu.claude-api.cn/`.
- Google displayed `已完成所有权验证` using the HTML tag method. The verification tag was first checked on the public homepage.
- Sitemap: `https://amd-dlss-mu.claude-api.cn/sitemap.xml`; platform status `成功`, discovered pages `7`, discovered videos `0`, last read `2026-09-28`.
- URL inspection initially reported `/dlss5` as unknown and `/` as discovered but not indexed. Indexing requests for both URLs completed the platform's live-URL eligibility check and returned `已请求编入索引`, with confirmation that each URL was added to the priority crawl queue. No request was repeated. Actual indexing and ranking were not established.
- Environment-only deployment used the existing website image. Prior environment backup on the server: `/home/xrw/amd-dlss-mu-site/backups/seo-google-20260928T021234Z.env`. Preserve the active verification tag across later deployments.
- Baidu: login deferred by the owner; no site verification or submission performed.
