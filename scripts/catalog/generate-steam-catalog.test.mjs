import { test } from 'node:test';
import assert from 'node:assert/strict';
import { decodeHtml, parseReleaseDate, parseSearchPage, parseAppDetails } from './generate-steam-catalog.mjs';

const row = ({ id = '1091500', name = 'Cyberpunk 2077', date = 'Dec 9, 2020', platform = 'win', item = `App_${id}`, href = `https://store.steampowered.com/app/${id}/Game/` } = {}) =>
  `<a href="${href}" data-ds-appid="${id}" data-ds-itemkey="${item}" class="search_result_row ds_collapse_flag"><span class="title">${name}</span><span class="platform_img ${platform}"></span><div class="search_released responsive_secondrow">${date}</div></a>`;
const response = html => JSON.stringify({ success: 1, results_html: html, total_count: 1, start: 0 });

test('official text is decoded without invented translations and dates are exact', () => {
  assert.equal(decodeHtml('Game &amp; Co. &#039; &#x4e2d;'), "Game & Co. ' 中");
  assert.equal(parseReleaseDate('Sep 27, 2026'), '2026-09-27');
  assert.equal(parseReleaseDate('27 Sep, 2026'), '2026-09-27');
  assert.equal(parseReleaseDate('2026 年 9 月 27 日'), '2026-09-27');
  assert.equal(parseReleaseDate('Feb 29, 2025'), null);
  assert.equal(parseReleaseDate('September 2026'), null);
  assert.equal(parseReleaseDate('Coming soon'), null);
});

test('only single released Windows game entries from official app links survive', () => {
  const parsed = parseSearchPage(response(row() + row({ id: '2', date: 'Oct 1, 2026' }) + row({ id: '3', platform: 'mac' })
    + row({ id: '4', name: 'Example Demo' }) + row({ id: '5', item: 'Bundle_5' })
    + row({ id: '6,7' }) + row({ id: '8', href: 'https://example.invalid/app/8/' }) + row({ id: '9', date: 'Coming soon' })), '2026-09-27');
  assert.deepEqual(parsed.rows, [{ steamAppId: '1091500', name: 'Cyberpunk 2077', releaseDate: '2020-12-09', storeUrl: 'https://store.steampowered.com/app/1091500/' }]);
  assert.equal(Object.values(parsed.excluded).reduce((sum, count) => sum + count, 0), 7);
});

test('unexpected source responses and changed markup fail visibly', () => {
  assert.throws(() => parseSearchPage(JSON.stringify({ success: 0 }), '2026-09-27'));
  assert.throws(() => parseSearchPage(response('<p>new markup</p>'), '2026-09-27'));
});

test('explicit continuity AppIDs require official game type, Windows support and released status', () => {
  const fixture = { '271590': { success: true, data: { steam_appid: 271590, type: 'game', name: 'Grand Theft Auto V Legacy',
    platforms: { windows: true }, release_date: { coming_soon: false, date: 'Apr 13, 2015' } } } };
  assert.equal(parseAppDetails(JSON.stringify(fixture), '271590', '2026-09-27').releaseDate, '2015-04-13');
  for (const override of [{ type: 'dlc' }, { type: 'demo' }, { type: 'software' }, { steam_appid: 1 },
    { platforms: { windows: false } }, { release_date: { coming_soon: true, date: 'Apr 13, 2015' } },
    { release_date: { coming_soon: false, date: 'Oct 1, 2026' } }]) {
    assert.throws(() => parseAppDetails(JSON.stringify({ '271590': { success: true, data: { ...fixture['271590'].data, ...override } } }), '271590', '2026-09-27'));
  }
});
