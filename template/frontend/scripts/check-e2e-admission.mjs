import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

// 完整档保留少量余量。派生项目在这里调整用例上限。
const limit = 20;
// Playwright 的 JSON reporter 会从有效标签中去掉开头的 @。
const prefix = 'proves:';
const categories = new Set([
  'auth-session',
  'service-call',
  'gateway-route',
  'deploy-orchestration',
  'realtime',
  'main-flow',
]);

function* discoveredSpecs(suites) {
  for (const suite of suites) {
    yield* suite.specs;
    yield* discoveredSpecs(suite.suites ?? []);
  }
}

function check(suites, maximum) {
  const errors = [];
  let count = 0;
  for (const spec of discoveredSpecs(suites)) {
    count += spec.tests.length;
    const tags = spec.tags.filter((tag) => tag.startsWith(prefix));
    const location = `${spec.file}:${spec.line} ${spec.title}`;
    if (tags.length !== 1) {
      errors.push(`${location}: expected one @${prefix} tag, got ${tags.length}.`);
    } else if (!categories.has(tags[0].slice(prefix.length))) {
      errors.push(`${location}: unknown category @${tags[0]}.`);
    }
  }
  if (count > maximum)
    errors.push(`Full E2E suite has ${count} tests, above the cap of ${maximum}.`);
  return { count, errors };
}

if (process.argv.includes('--self-test')) {
  const suites = (tags, tests = [{}]) => [
    {
      specs: [],
      suites: [{ specs: [{ file: 'sample.e2e.ts', line: 1, title: 'sample', tags, tests }] }],
    },
  ];
  assert.deepEqual(check(suites(['auth', 'proves:auth-session']), 1), { count: 1, errors: [] });
  assert.match(check(suites(['auth']), 1).errors[0], /got 0/);
  assert.match(check(suites(['proves:auth-session', 'proves:main-flow']), 1).errors[0], /got 2/);
  assert.match(check(suites(['proves:unknown']), 1).errors[0], /unknown category/);
  assert.match(check(suites(['proves:main-flow'], [{}, {}]), 1).errors[0], /above the cap/);
  console.log('E2E admission self-test passed.');
} else {
  const cli = fileURLToPath(import.meta.resolve('@playwright/test/cli'));
  const result = spawnSync(process.execPath, [cli, 'test', '--list', '--reporter=json'], {
    cwd: fileURLToPath(new URL('../', import.meta.url)),
    encoding: 'utf8',
  });
  if (result.error) throw result.error;
  if (result.status !== 0) {
    process.stderr.write(result.stderr || result.stdout);
    process.exit(result.status ?? 1);
  }
  const { count, errors } = check(JSON.parse(result.stdout).suites, limit);
  if (errors.length) {
    console.error(errors.join('\n'));
    process.exit(1);
  }
  console.log(`E2E admission passed: ${count}/${limit} tests.`);
}
