// Exercise the actual, unmodified extension against a disposable script fixture.
// Copying its relative layout avoids any production command/API injection seam.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { copyFile, mkdir, mkdtemp, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { resolve } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';

function alive(pid) {
  try { process.kill(pid, 0); return true; }
  catch (error) { if (error.code === 'ESRCH') return false; throw error; }
}
async function bounded(promise, milliseconds, message) {
  let timer;
  try {
    return await Promise.race([promise, new Promise((_, reject) => {
      timer = setTimeout(() => reject(new Error(message)), milliseconds);
    })]);
  } finally { clearTimeout(timer); }
}
async function waitUntil(check, milliseconds, message) {
  const deadline = Date.now() + milliseconds;
  while (Date.now() < deadline) {
    if (await check()) return;
    await delay(25);
  }
  throw new Error(message);
}
async function readJson(path) {
  try { return JSON.parse(await readFile(path, 'utf8')); }
  catch (error) { if (error.code === 'ENOENT' || error instanceof SyntaxError) return undefined; throw error; }
}

export async function cancellationRegression(loadExtensions, repositoryRoot) {
  const fixture = await mkdtemp(resolve(tmpdir(), 'brutal-cancellation-'));
  const controller = new AbortController();
  let pending;
  const pids = new Set();
  try {
    await mkdir(resolve(fixture, '.pi/extensions'), { recursive: true });
    await mkdir(resolve(fixture, '.pi/scripts'), { recursive: true });
    const extensionPath = resolve(fixture, '.pi/extensions/brutal-support.ts');
    await copyFile(resolve(repositoryRoot, '.pi/extensions/brutal-support.ts'), extensionPath);
    assert.deepEqual(await readFile(extensionPath), await readFile(resolve(repositoryRoot, '.pi/extensions/brutal-support.ts')));
    await copyFile(resolve(repositoryRoot, '.pi/tests/fixtures/cancellation-support.ps1'), resolve(fixture, '.pi/scripts/support.ps1'));
    await copyFile(resolve(repositoryRoot, '.pi/tests/fixtures/cancellation-tree.mjs'), resolve(fixture, '.pi/scripts/cancellation-tree.mjs'));
    const loaded = await loadExtensions([extensionPath], fixture);
    assert.deepEqual(loaded.errors, []);
    const tool = loaded.extensions[0].tools.get('brutal_support').definition;
    pending = tool.execute('tree-cancellation', { operation: 'doctor' }, controller.signal, undefined, { cwd: process.cwd() });
    // Attach a rejection observer while waiting for the independent readiness barrier.
    pending.catch(() => {});
    let ready;
    await waitUntil(async () => {
      ready = await readJson(resolve(fixture, 'ready.json'));
      return !!ready;
    }, 10000, 'Fixture child/grandchild did not become ready within 10 seconds');
    const rootPid = Number(await readFile(resolve(fixture, 'root.pid'), 'utf8'));
    for (const pid of [rootPid, ready.child.pid, ready.grandchild.pid]) {
      assert.ok(Number.isSafeInteger(pid) && pid > 0 && pid !== process.pid);
      pids.add(pid);
      assert.ok(alive(pid), `Fixture PID ${pid} must be alive before cancellation`);
    }
    assert.equal(pids.size, 3);
    assert.equal(ready.child.parentPid, rootPid);
    assert.equal(ready.grandchild.parentPid, ready.child.pid);
    console.log(`Cancellation ready: root=${rootPid}, child=${ready.child.pid}, grandchild=${ready.grandchild.pid}`);
    controller.abort();
    const cancelled = await bounded(pending, 8000, 'Extension cancellation did not settle within 8 seconds');
    assert.equal(cancelled.isError, true);
    assert.equal(cancelled.structuredContent.cancelled, true);
    assert.equal(cancelled.structuredContent.exitCode, 130);
    // Check before cleanup: immediate-child-only termination MUST fail here.
    await waitUntil(() => [...pids].every(pid => !alive(pid)), 5000,
      `Cancellation leaked fixture processes: ${[...pids].join(', ')}`);
    console.log('PASS: readiness-confirmed child AND grandchild (plus root) disappeared after extension cancellation.');
  } finally {
    controller.abort();
    if (pending) await bounded(pending.catch(() => {}), 8000, 'Cancellation cleanup did not settle').catch(() => {});
    // Recover identities even when readiness failed halfway through startup.
    const rootPid = Number(await readFile(resolve(fixture, 'root.pid'), 'utf8').catch(() => '0'));
    if (Number.isSafeInteger(rootPid) && rootPid > 0) pids.add(rootPid);
    for (const role of ['child', 'grandchild']) {
      const identity = await readJson(resolve(fixture, `${role}.json`));
      if (Number.isSafeInteger(identity?.pid) && identity.pid > 0) pids.add(identity.pid);
    }
    for (const pid of pids) {
      if (alive(pid)) spawnSync('taskkill.exe', ['/PID', String(pid), '/T', '/F'], { windowsHide: true, stdio: 'ignore', timeout: 5000 });
    }
    await waitUntil(() => [...pids].every(pid => !alive(pid)), 5000,
      `Fixture cleanup failed; PID evidence retained in ${fixture}`);
    await rm(fixture, { recursive: true, force: true });
  }
}
