import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtemp, mkdir, writeFile, readFile, copyFile, chmod, symlink, unlink, readlink, stat, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawn, spawnSync } from 'node:child_process';
import { once } from 'node:events';

const script = join(dirname(fileURLToPath(import.meta.url)), 'activate.sh');
const oldHash = createHash('sha256').update('old binary').digest('hex');
const mutated = new Set(['backup', 'move', 'compose-create', 'compose-up']);

async function fixture(t, mode = 'running') {
  const root = await mkdtemp(join(tmpdir(), 'mu-activate-guard-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const bin = join(root, 'bin'), baseline = join(root, 'backups', 'rehearsal-candidate');
  for (const directory of [bin, baseline, join(root, 'data'), ...['old-release', 'candidate', 'concurrent'].map(x => join(root, 'releases', x))])
    await mkdir(directory, { recursive: true });
  await copyFile(script, join(root, 'activate.sh'));
  for (const [release, bytes] of [['old-release', 'old binary'], ['candidate', 'new binary'], ['concurrent', 'another binary']])
    await writeFile(join(root, 'releases', release, 'Mu.Server.dll'), bytes);
  await symlink('releases/old-release', join(root, 'current'));
  await writeFile(join(root, '.env'), 'NON_SECRET_FIXTURE=1\nProxy__KnownProxies__0=192.0.2.1\n');
  await chmod(join(root, '.env'), 0o644);
  await chmod(join(root, 'data'), 0o755);
  await chmod(join(root, 'backups'), 0o755);
  await writeFile(join(root, 'data', 'accounts.sqlite'), 'unchanged account fixture');
  await writeFile(join(root, 'backups', '.backup.lock'), 'lock contents preserved\n');
  await writeFile(join(baseline, 'baseline-release.txt'), 'releases/old-release\n');
  await writeFile(join(baseline, 'baseline-dll.sha256'), oldHash + '\n');
  const eventsPath = join(root, 'events.jsonl'), support = join(root, 'support.cjs');
  await writeFile(eventsPath, '');
  await writeFile(support, `
const fs=require('node:fs'), child=require('node:child_process');
function event(name, details){fs.appendFileSync(process.env.MOCK_EVENTS,JSON.stringify({name,details})+'\\n');}
function locked(){
 const result=child.spawnSync('python3',['-c',"import fcntl,sys;f=open(sys.argv[1],'r');\\ntry: fcntl.flock(f,fcntl.LOCK_EX|fcntl.LOCK_NB)\\nexcept BlockingIOError: sys.exit(0)\\nsys.exit(7)",process.env.MOCK_LOCK]);
 if(result.status!==0)throw Error('Action occurred outside the account backup lock');
}
module.exports={event,locked};
`);
  async function executable(path, text) { await writeFile(path, text); await chmod(path, 0o755); }
  await executable(join(bin, 'flock'), `#!/usr/bin/env python3
import fcntl,json,os,sys
with open(os.environ['MOCK_EVENTS'],'a') as out: out.write(json.dumps({'name':'lock-request'})+'\\n')
fcntl.flock(int(sys.argv[-1]),fcntl.LOCK_EX | (fcntl.LOCK_NB if '-n' in sys.argv else 0))
with open(os.environ['MOCK_EVENTS'],'a') as out: out.write(json.dumps({'name':'lock-acquired'})+'\\n')
`);
  await executable(join(bin, 'docker'), `#!${process.execPath}
const {event,locked}=require(process.env.MOCK_SUPPORT);locked();const a=process.argv.slice(2);
if(a.slice(0,2).join(' ')==='compose ps'){event('running-read');if(process.env.MOCK_MODE!=='stopped')console.log('account');}
else if(a[0]==='exec'){event('running-hash-read');console.log((process.env.MOCK_MODE==='running-drift'?'f'.repeat(64):'${oldHash}')+'  /app/Mu.Server.dll');}
else if(a.slice(0,2).join(' ')==='compose create')event('compose-create');
else if(a.slice(0,2).join(' ')==='network inspect'){event('network-read');console.log('172.20.0.1');}
else if(a.slice(0,2).join(' ')==='compose up')event('compose-up');
else{event('unexpected-docker',a);process.exit(2);}
`);
  await executable(join(bin, 'mv'), `#!${process.execPath}
const fs=require('node:fs');const {event,locked}=require(process.env.MOCK_SUPPORT);locked();
const a=process.argv.slice(2).filter(x=>!x.startsWith('-'));event('move',a);fs.renameSync(a[0],a[1]);
`);
  await executable(join(bin, 'curl'), `#!${process.execPath}
const {event,locked}=require(process.env.MOCK_SUPPORT);locked();event('health-read');console.log('{"status":"ok"}');
`);
  await executable(join(root, 'backup.sh'), `#!${process.execPath}
const fs=require('node:fs');const {event,locked}=require(process.env.MOCK_SUPPORT);locked();
if(process.argv[2]!=='--lock-held'||fs.fstatSync(9).ino!==fs.statSync(process.env.MOCK_LOCK).ino)throw Error('Missing inherited backup lock');
event('backup');fs.copyFileSync('data/accounts.sqlite','backups/final-snapshot.sqlite');
`);
  const env = { ...process.env, PATH: bin + ':' + process.env.PATH, MOCK_EVENTS: eventsPath,
    MOCK_LOCK: join(root, 'backups', '.backup.lock'), MOCK_SUPPORT: support, MOCK_MODE: mode };
  const args = [join(root, 'activate.sh'), 'candidate', '--expected-baseline', baseline];
  async function events() { return (await readFile(eventsPath, 'utf8')).trim().split('\n').filter(Boolean).map(JSON.parse); }
  async function unchanged(target = 'releases/old-release') {
    assert.equal(await readlink(join(root, 'current')), target);
    assert.equal(await readFile(join(root, '.env'), 'utf8'), 'NON_SECRET_FIXTURE=1\nProxy__KnownProxies__0=192.0.2.1\n');
    assert.equal((await stat(join(root, '.env'))).mode & 0o777, 0o644);
    assert.equal((await stat(join(root, 'data'))).mode & 0o777, 0o755);
    assert.equal((await stat(join(root, 'backups'))).mode & 0o777, 0o755);
    assert.equal(await readFile(join(root, 'data', 'accounts.sqlite'), 'utf8'), 'unchanged account fixture');
    assert.equal(await readFile(join(root, 'backups', '.backup.lock'), 'utf8'), 'lock contents preserved\n');
    assert.ok(!(await events()).some(x => mutated.has(x.name)), 'guard rejection must have no mutation');
  }
  return { root, baseline, eventsPath, env, args, events, unchanged };
}

test('locked baseline guard rejects release, disk and running drift without mutations', async t => {
  for (const mismatch of ['release', 'disk', 'running-drift', 'stopped', 'malformed']) {
    const f = await fixture(t, mismatch);
    if (mismatch === 'release') await writeFile(join(f.baseline, 'baseline-release.txt'), 'releases/unexpected\n');
    if (mismatch === 'disk') await writeFile(join(f.baseline, 'baseline-dll.sha256'), 'e'.repeat(64) + '\n');
    if (mismatch === 'malformed') await writeFile(join(f.baseline, 'baseline-release.txt'), '../../elsewhere\n');
    const result = spawnSync('/bin/bash', f.args, { env: f.env, encoding: 'utf8', timeout: 10000 });
    assert.notEqual(result.status, 0, mismatch);
    assert.match(result.stderr, /no activation performed/u, mismatch + ': ' + result.stderr);
    await f.unchanged();
    assert.equal((await f.events())[0].name, 'lock-request');
    assert.equal((await f.events())[1].name, 'lock-acquired');
  }
});

test('matching baseline backs up before activation and legacy single argument still works', async t => {
  for (const legacy of [false, true]) {
    const f = await fixture(t, legacy ? 'stopped' : 'running');
    const result = spawnSync('/bin/bash', legacy ? f.args.slice(0, 2) : f.args, { env: f.env, encoding: 'utf8', timeout: 10000 });
    assert.equal(result.status, 0, result.stdout + result.stderr);
    assert.equal(await readlink(join(f.root, 'current')), 'releases/candidate');
    assert.equal((await stat(join(f.root, '.env'))).mode & 0o777, 0o600);
    assert.match(await readFile(join(f.root, '.env'), 'utf8'), /Proxy__KnownProxies__0=172\.20\.0\.1/u);
    const names = (await f.events()).map(x => x.name);
    assert.ok(names.indexOf('lock-acquired') < names.indexOf('move'));
    if (legacy) assert.ok(!names.includes('backup') && !names.includes('running-hash-read'));
    else {
      assert.ok(names.indexOf('running-hash-read') < names.indexOf('backup'));
      assert.ok(names.indexOf('backup') < names.indexOf('move'));
      assert.equal(await readFile(join(f.root, 'backups', 'final-snapshot.sqlite'), 'utf8'), 'unchanged account fixture');
    }
    assert.ok(names.indexOf('move') < names.indexOf('compose-up'));
    assert.equal(names.at(-1), 'health-read');
  }
});

test('a concurrent change while waiting for fd 9 is checked after the lock is acquired', async t => {
  const f = await fixture(t);
  const holder = spawn('python3', ['-u', '-c', "import fcntl,sys;f=open(sys.argv[1],'r');fcntl.flock(f,fcntl.LOCK_EX);print('locked',flush=True);sys.stdin.readline()", f.env.MOCK_LOCK]);
  t.after(() => holder.kill());
  await once(holder.stdout, 'data');
  const activation = spawn('/bin/bash', f.args, { env: f.env });
  t.after(() => activation.kill());
  let stderr = ''; activation.stderr.on('data', bytes => { stderr += bytes; });
  const completed = once(activation, 'exit');
  for (let attempt = 0; attempt < 100 && !(await f.events()).some(x => x.name === 'lock-request'); attempt++)
    await new Promise(done => setTimeout(done, 20));
  assert.deepEqual((await f.events()).map(x => x.name), ['lock-request']);
  await f.unchanged();
  await unlink(join(f.root, 'current'));
  await symlink('releases/concurrent', join(f.root, 'current'));
  holder.stdin.end('\n');
  const [code] = await completed;
  assert.notEqual(code, 0);
  assert.match(stderr, /release changed since rehearsal/u);
  await f.unchanged('releases/concurrent');
  assert.deepEqual((await f.events()).map(x => x.name), ['lock-request', 'lock-acquired']);
});
