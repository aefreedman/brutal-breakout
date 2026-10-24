// Headless child/grandchild fixture; an independent safety deadline limits leaks.
import { spawn } from 'node:child_process';
import { writeFile, readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { setTimeout as delay } from 'node:timers/promises';

const [root, role] = process.argv.slice(2);
if (!root || !['child', 'grandchild'].includes(role)) throw new Error('Invalid fixture arguments');
const identity = { pid: process.pid, parentPid: process.ppid };
await writeFile(resolve(root, `${role}.json`), JSON.stringify(identity));
const safetyDeadline = setTimeout(() => process.exit(2), 60000);
if (role === 'child') {
  const grandchild = spawn(process.execPath, [fileURLToPath(import.meta.url), root, 'grandchild'], {
    windowsHide: true, stdio: 'ignore',
  });
  grandchild.on('error', (error) => { console.error(error); process.exitCode = 1; clearTimeout(safetyDeadline); });
  const deadline = Date.now() + 10000;
  let descendant;
  while (Date.now() < deadline) {
    try { descendant = JSON.parse(await readFile(resolve(root, 'grandchild.json'), 'utf8')); break; }
    catch (error) { if (error.code !== 'ENOENT' && !(error instanceof SyntaxError)) throw error; }
    await delay(25);
  }
  if (!descendant || descendant.pid !== grandchild.pid || descendant.parentPid !== process.pid) {
    throw new Error('Grandchild readiness/identity mismatch');
  }
  await writeFile(resolve(root, 'ready.json'), JSON.stringify({ child: identity, grandchild: descendant }));
}
