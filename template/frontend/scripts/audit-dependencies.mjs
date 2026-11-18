import { spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { setTimeout } from 'node:timers/promises';

const networkError =
  /ENOTFOUND|EAI_AGAIN|ETIMEDOUT|ECONNRESET|ECONNREFUSED|FETCH_ERROR|ERR_SOCKET_TIMEOUT|network timeout|audit endpoint returned an error/;

function versionParts(version) {
  const parts = /^(\d+)\.(\d+)\.(\d+)(?:-([\w.-]+))?(?:\+[\w.-]+)?$/.exec(version);
  if (!parts) throw new Error(`Invalid locked or suggested version: ${version}`);
  return [parts.slice(1, 4).map(Number), parts[4]?.split('.')];
}

function compareVersions(left, right) {
  const [leftCore, leftPre] = versionParts(left);
  const [rightCore, rightPre] = versionParts(right);
  for (const [index, part] of leftCore.entries()) {
    if (part !== rightCore[index]) return part - rightCore[index];
  }
  if (!leftPre || !rightPre) return Number(!leftPre) - Number(!rightPre);
  for (const [index, part] of leftPre.entries()) {
    const other = rightPre[index];
    if (other === undefined) return 1;
    if (part === other) continue;
    const numeric = /^\d+$/.test(part);
    const otherNumeric = /^\d+$/.test(other);
    if (numeric && otherNumeric) return Number(part) - Number(other);
    if (numeric !== otherNumeric) return numeric ? -1 : 1;
    return part < other ? -1 : 1;
  }
  return leftPre.length - rightPre.length;
}

function hasFix(fix, packages) {
  if (typeof fix === 'boolean') return fix;
  // npm can recommend changing a top-level dependency instead of the affected package.
  const locked = packages[`node_modules/${fix.name}`]?.version;
  return compareVersions(fix.version, locked) >= 0;
}

function isHigh(severity) {
  return severity === 'high' || severity === 'critical';
}

function describeAdvisory(advisory) {
  const id = advisory.url?.match(/GHSA-[\w-]+/)?.[0] ?? advisory.source;
  return `${id} ${advisory.name} ${advisory.range} (${advisory.severity})`;
}

export function evaluateAudit(output, packages) {
  const report = JSON.parse(output);
  if (report?.error) throw new Error(report.error.detail || report.error.summary);
  const vulnerabilities = report?.vulnerabilities;
  if (!vulnerabilities || typeof vulnerabilities !== 'object' || Array.isArray(vulnerabilities)) {
    throw new Error('npm audit did not return a vulnerability report.');
  }

  const warnings = new Set();
  const blocking = [];
  for (const { name, range, severity, fixAvailable, via } of Object.values(vulnerabilities)) {
    if (!isHigh(severity)) continue;
    if (hasFix(fixAvailable, packages)) {
      const advisories = via.map((item) =>
        typeof item === 'string' ? item : describeAdvisory(item),
      );
      blocking.push(`${name} ${range} (${severity}) via ${advisories.join(', ')}`);
      continue;
    }
    // String entries point to other packages in the report. Warn once per underlying advisory.
    for (const item of via) {
      if (typeof item === 'object' && isHigh(item.severity)) warnings.add(describeAdvisory(item));
    }
  }
  return { warnings: [...warnings], blocking, exitCode: Number(blocking.length > 0) };
}

async function audit() {
  for (let attempt = 1; ; attempt++) {
    // Leave vulnerability-based exit codes to the shared policy below.
    const result = spawnSync(
      'npm',
      [
        'audit',
        '--json',
        '--omit=dev',
        '--audit-level=none',
        '--package-lock-only',
        '--fetch-timeout=60000',
        '--fetch-retries=0',
      ],
      { encoding: 'utf8', shell: process.platform === 'win32', maxBuffer: 16 * 1024 * 1024 },
    );
    if (result.error) throw result.error;
    const diagnostics = `${result.stderr}\n${result.stdout}`.trim();
    if (result.status === 0 && !networkError.test(result.stderr)) return result.stdout;
    if (attempt >= 3 || !networkError.test(diagnostics)) {
      throw new Error(`npm audit failed (exit ${result.status ?? result.signal}).\n${diagnostics}`);
    }
    console.error(diagnostics);
    await setTimeout(5000 * attempt);
  }
}

function warn(message) {
  if (process.env.GITHUB_ACTIONS === 'true') {
    const escaped = message.replaceAll('%', '%25').replaceAll('\r', '%0D').replaceAll('\n', '%0A');
    console.warn(`::warning::${escaped}`);
  } else {
    console.warn(`Warning: ${message}`);
  }
}

if (import.meta.main) {
  try {
    const { packages } = JSON.parse(readFileSync('package-lock.json', 'utf8'));
    const { warnings, blocking, exitCode } = evaluateAudit(await audit(), packages);
    for (const advisory of warnings) warn(`No fix available for ${advisory}`);
    for (const vulnerability of blocking) console.error(`Fix available for ${vulnerability}`);
    if (exitCode === 0) console.log('No fixable high or critical production vulnerabilities.');
    process.exitCode = exitCode;
  } catch (error) {
    console.error(`Dependency audit failed: ${error.message}`);
    process.exitCode = 1;
  }
}
