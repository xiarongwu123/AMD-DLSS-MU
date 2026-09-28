import { test } from 'node:test';
import assert from 'node:assert/strict';
import { buildCatalog, parseCompatibility, parseEnvironment } from './collect-optiscaler.mjs';
import { reviewedCommit } from './optiscaler-notes.mjs';

const source = { commit: reviewedCommit, retrievedAt: '2026-09-27T16:22:15.598Z' };
const header = '| Game | Compatibility | Upscaler <br>Inputs | OptiPatcher <br>Support | Notes | Images |\n| ---- | :---: | --- | --- | --- | --- |\n';
const environment = '[cols="1,1"]\n|===\n|**Last Tested Version**\n|0.9.3\n\n|**OS**\n|W11 24H2\n\n|**GPU**\na|\n* RX 9070 XT\n\n|**Known Issues**\na|\n* <s>Old crash fixed</s>\n* Active flickering condition\n\n|**Reported By**\n|private-handle\n|===\n';

test('parses real table variants and separates third-party input paths', () => {
  const rows = parseCompatibility('<!-- | TEMPLATE | ✅ | DLSS | | | -->\n' + header
    + '| [Game](Game) | ✅ | DLSS, FSR3.1 | | settings | |\n'
    + 'Without leading pipe | ❌ | | | No hook |\n'
    + '| Platform game | ➖ | DLSS | | Linux only | |\n'
    + '## Upscaler mods support\n| Game | Compatibility | Upscaler <br>Inputs | Notes | Images |\n'
    + '| Mod game | ✅ | DLSS | Requires mod | |\n'
    + '## Luma Unreal Engine\n| Game | Compatibility | Upscaler <br>Inputs | Notes | Images |\n'
    + '| Luma game | ✅ | DLSS | | |\n');
  assert.equal(rows.length, 5);
  assert.deepEqual(rows[0].upscalerInputs, ['DLSS', 'FSR3.1']);
  assert.equal(rows[1].status, 'not_working');
  assert.equal(rows[2].status, 'platform_limited');
  assert.equal(rows[3].section, 'upscaler_mod');
  assert.equal(rows[4].section, 'luma_ue');
});

test('unknown status or table shape fails instead of fabricating compatibility', () => {
  assert.throws(() => parseCompatibility(header + '| Game | MAYBE | DLSS | | | |'));
  assert.throws(() => parseCompatibility(header + '| Game | ✅ | DLSS |'));
  assert.throws(() => parseCompatibility(header + '| [Game](https://attacker.invalid/) | ✅ | DLSS | | | |'));
});

test('environment fields are factual, nullable, and exclude reporter identity', () => {
  const parsed = parseEnvironment(environment);
  assert.deepEqual(parsed.environment, { optiscalerVersion: '0.9.3', gpu: 'RX 9070 XT', os: 'W11 24H2' });
  assert.equal(JSON.stringify(parsed).includes('private-handle'), false);
  assert.equal(parsed.conditionText.includes('Old crash fixed'), false);
  assert.match(parsed.conditionText, /Active flickering/);
  assert.equal(parseEnvironment('|**GPU**\n|-\n|===').environment, null);
});

test('exact identity matching retains editions and leaves unmatched source records unassigned', async () => {
  const markdown = header + '| [Game](Game) | ✅ | DLSS | | | |\n| Game Enhanced | ✅ | DLSS | | | |\n| Other | ❌ | | | No hook | |';
  const result = await buildCatalog(markdown, { games: [{ steamAppId: '1', name: 'Game\u2122' }, { steamAppId: '2', name: 'Game Legacy' }] }, new Map([['Game.asciidoc', Buffer.from(environment)]]), source);
  assert.equal(result.statistics.matchedSteamGames, 1);
  const game = result.entries.find(entry => entry.name === 'Game');
  assert.equal(game.steamAppId, '1'); assert.equal(game.muVerified, false);
  assert.equal(game.testEnvironment.gpu, 'RX 9070 XT');
  assert.ok(game.sourceUrl.endsWith('/Game/' + reviewedCommit));
  assert.equal(result.entries.find(entry => entry.name === 'Other').status, 'not_working');
  assert.equal(result.entries.find(entry => entry.name === 'Game Enhanced').steamAppId, null);
});

test('identical duplicates collapse but conflicting status duplicates stop the import', async () => {
  const row = '| Game | ✅ | DLSS | | | |\n';
  const result = await buildCatalog(header + row + row, { games: [] }, new Map(), source);
  assert.equal(result.entries.length, 1); assert.equal(result.statistics.duplicateRows, 1);
  await assert.rejects(buildCatalog(header + row + row.replace('✅', '❌'), { games: [] }, new Map(), source));
});

test('common mod documentation does not assign one GPU test to every linked game', async () => {
  const markdown = header + '| [One](Shared) | ✅ | DLSS | | | |\n| [Two](Shared) | ✅ | DLSS | | | |\n';
  const result = await buildCatalog(markdown, { games: [] }, new Map([['Shared.asciidoc', Buffer.from(environment)]]), source);
  assert.ok(result.entries.every(entry => entry.testEnvironment === null));
  assert.ok(result.entries.every(entry => entry.notes.some(note => note.includes('共用'))));
});

test('curated AMD and anti-cheat conditions stay separate from MU verification', async () => {
  const markdown = header + '| Cyberpunk 2077 | ✅ | DLSS, FSR3.1/4 | | | |\n| Grand Theft Auto V Enhanced | ✅ | DLSS | | | |\n';
  const result = await buildCatalog(markdown, { games: [] }, new Map(), source);
  const cyberpunk = result.entries.find(entry => entry.name === 'Cyberpunk 2077');
  assert.ok(cyberpunk.notes.some(note => note.includes('AMD/Intel') && note.includes('路径追踪')));
  assert.ok(result.entries.find(entry => entry.name.includes('Theft')).notes.some(note => note.includes('BattlEye')));
  assert.ok(result.entries.every(entry => entry.muVerified === false));
});
