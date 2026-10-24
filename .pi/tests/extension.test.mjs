// Run with node; provide the installed Pi package root as the sole argument.
// Uses Pi's installed loader, not an interactive agent or provider call.
import assert from 'node:assert/strict';
import { resolve, dirname } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { cancellationRegression } from './cancellation-regression.mjs';

const piRoot = process.argv[2];
assert.ok(piRoot, 'Usage: node .pi/tests/extension.test.mjs <installed-pi-package-root>');
const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const { loadExtensions } = await import(pathToFileURL(resolve(piRoot, 'dist/core/extensions/loader.js')));
const { loadSkillsFromDir } = await import(pathToFileURL(resolve(piRoot, 'dist/core/skills.js')));
const loaded = await loadExtensions([resolve(repositoryRoot, '.pi/extensions/brutal-support.ts')], repositoryRoot);
assert.deepEqual(loaded.errors, []);
assert.deepEqual(loaded.warnings ?? [], []);
assert.equal(loaded.extensions.length, 1);
const extension = loaded.extensions[0];
assert.equal(extension.handlers.size, 0, 'No auto-run hooks');
assert.equal(extension.tools.size, 1);
const registered = extension.tools.get('brutal_support');
const tool = registered.definition;
assert.ok(tool, 'Tool definition registered with installed Pi API');
const skills = loadSkillsFromDir({ dir: resolve(repositoryRoot, '.pi/skills'), source: 'project' });
assert.deepEqual(skills.diagnostics, []);
assert.deepEqual(skills.skills.map(s => s.name).sort(), ['brutal-package-audit', 'validate-renderer']);
const context = { cwd: process.cwd() };
await assert.rejects(() => tool.execute('bad', { operation: 'arbitrary-command' }, undefined, undefined, context), /Unknown/);
await assert.rejects(() => tool.execute('missing', { operation: 'package-assets' }, undefined, undefined, context), /packageId required/);
const preCancelled = new AbortController();
preCancelled.abort();
await assert.rejects(() => tool.execute('cancelled', { operation: 'doctor' }, preCancelled.signal, undefined, context), /Cancelled/);
// A real doctor run exercises spawn, bounded output, and structured return.
const result = await tool.execute('doctor', { operation: 'doctor' }, undefined, undefined, context);
assert.equal(result.isError, false, JSON.stringify(result));
assert.equal(result.structuredContent.exitCode, 0);
assert.equal(result.structuredContent.repositoryRoot, repositoryRoot);
assert.equal(result.structuredContent.callerCwd, process.cwd());
const summary = JSON.parse(result.structuredContent.output);
assert.equal(summary.exitCode, 0);
assert.ok(summary.steps.some(s => s.command.endsWith('vulkaninfo.exe')));
await cancellationRegression(loadExtensions, repositoryRoot);
console.log('PASS: installed Pi loader/API, two skill resources, no hooks, allowlist, real doctor, pre-cancellation and descendant cleanup.');
