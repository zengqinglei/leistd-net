import assert from 'node:assert/strict';
import { test } from 'node:test';

import { evaluateAudit } from './audit-dependencies.mjs';

const advisory = {
  name: 'matcher',
  url: 'https://github.com/advisories/GHSA-xxxx-yyyy-zzzz',
  severity: 'high',
  range: '<=4.0.7',
};
const vulnerable = {
  name: advisory.name,
  severity: advisory.severity,
  via: [advisory],
  range: advisory.range,
  fixAvailable: false,
};
const warning = 'GHSA-xxxx-yyyy-zzzz matcher <=4.0.7 (high)';
const packages = {
  'node_modules/matcher': { version: '4.0.7' },
  'node_modules/icon-kit': { version: '36.1.0' },
};

function evaluate(vulnerabilities) {
  return evaluateAudit(JSON.stringify({ vulnerabilities }), packages);
}

test('fails for a high vulnerability with an automatic fix', () => {
  const result = evaluate({ matcher: { ...vulnerable, fixAvailable: true } });
  assert.equal(result.exitCode, 1);
  assert.match(result.blocking[0], /GHSA-xxxx-yyyy-zzzz/);
  assert.deepEqual(result.warnings, []);
});

test('warns for a high advisory with no fix', () => {
  assert.deepEqual(evaluate({ matcher: vulnerable }), {
    exitCode: 0,
    warnings: [warning],
    blocking: [],
  });
});

test('warns once when a suggested fix downgrades the package that pulls in the advisory', () => {
  const fixAvailable = { name: 'icon-kit', version: '33.0.0', isSemVerMajor: true };
  assert.deepEqual(
    evaluate({
      matcher: { ...vulnerable, fixAvailable },
      'icon-kit': {
        ...vulnerable,
        name: 'icon-kit',
        range: '>=36.0.0',
        via: ['matcher'],
        fixAvailable,
      },
    }),
    { exitCode: 0, warnings: [warning], blocking: [] },
  );
});

test('fails for a critical vulnerability with an equal or newer suggested version', () => {
  for (const version of ['4.0.7', '4.0.10', '4.1.0-rc.1']) {
    assert.equal(
      evaluate({
        matcher: {
          ...vulnerable,
          severity: 'critical',
          fixAvailable: { name: 'matcher', version },
        },
      }).exitCode,
      1,
    );
  }
});

test('passes with only moderate vulnerabilities', () => {
  assert.deepEqual(
    evaluate({ matcher: { ...vulnerable, severity: 'moderate', fixAvailable: true } }),
    { exitCode: 0, warnings: [], blocking: [] },
  );
});

test('rejects invalid JSON and npm error output', () => {
  assert.throws(() => evaluateAudit('invalid JSON', packages), SyntaxError);
  assert.throws(() => evaluateAudit('{}', packages), /did not return a vulnerability report/);
  assert.throws(
    () => evaluateAudit('{"error":{"summary":"audit endpoint returned an error"}}', packages),
    /audit endpoint returned an error/,
  );
});
