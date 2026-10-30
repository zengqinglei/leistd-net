import assert from 'node:assert/strict';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';

import config, { pointerInstances } from '../vitest-base.config.ts';

function fixture(t) {
  const root = mkdtempSync(join(tmpdir(), 'pointer-isolation-'));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  writeFileSync(join(root, 'normal.spec.ts'), 'it("normal", () => {});');
  writeFileSync(join(root, 'touch.spec.ts'), 'await setCoarsePointer(true);');
  writeFileSync(join(root, 'matrix.spec.ts'), 'await setCoarsePointer (coarse);');
  writeFileSync(join(root, 'unselected.spec.ts'), 'await setCoarsePointer(true);');
  return root;
}

test('assigns each discovered spec exactly once per original instance with fine pointers first', (t) => {
  const root = fixture(t);
  const selected = ['normal.spec.ts', 'touch.spec.ts', 'matrix.spec.ts'];
  const originals = [{ browser: 'chromium' }, { browser: 'chromium', name: 'desktop' }];
  const instances = pointerInstances(originals, selected, root);
  assert.deepEqual(
    instances.map((instance) => instance.name),
    [
      'chromium:pointer-fine',
      'chromium:pointer-touch',
      'desktop:pointer-fine',
      'desktop:pointer-touch',
    ],
  );
  for (let i = 0; i < instances.length; i += 2) {
    assert.deepEqual(instances[i].include, ['normal.spec.ts']);
    assert.deepEqual(instances[i + 1].include, ['touch.spec.ts', 'matrix.spec.ts']);
    assert.deepEqual(
      instances
        .slice(i, i + 2)
        .flatMap((instance) => instance.include)
        .sort(),
      selected.toSorted(),
    );
    assert.deepEqual(
      instances.slice(i, i + 2).map((instance) => instance.sequence.groupOrder),
      [0, 1],
    );
  }
  assert.deepEqual(originals, [{ browser: 'chromium' }, { browser: 'chromium', name: 'desktop' }]);
});

test('omits empty groups and preserves targeted includes including absolute paths', (t) => {
  const root = fixture(t);
  for (const file of ['normal.spec.ts', 'touch.spec.ts', join(root, 'matrix.spec.ts')]) {
    const instances = pointerInstances([{ browser: 'chromium' }], [file], root);
    assert.equal(instances.length, 1);
    assert.deepEqual(instances[0].include, [file]);
  }
  assert.deepEqual(pointerInstances([{ browser: 'chromium' }], [], root), []);
});

test('preserves browser options and sequence options while assigning group order', (t) => {
  const root = fixture(t);
  const original = {
    browser: 'chromium',
    name: 'desktop',
    headless: true,
    viewport: { width: 1440, height: 900 },
    sequence: { hooks: 'list', groupOrder: 8 },
  };
  const instances = pointerInstances([original], ['normal.spec.ts', 'touch.spec.ts'], root);
  for (const [i, instance] of instances.entries()) {
    assert.equal(instance.browser, 'chromium');
    assert.equal(instance.headless, true);
    assert.deepEqual(instance.viewport, original.viewport);
    assert.deepEqual(instance.sequence, { hooks: 'list', groupOrder: i });
  }
  assert.deepEqual(original.sequence, { hooks: 'list', groupOrder: 8 });
});

test('splits Angular browser projects in the pre resolved hook without changing discovery', (t) => {
  const root = fixture(t);
  const include = ['normal.spec.ts', 'touch.spec.ts'];
  const browser = { enabled: true, instances: [{ browser: 'chromium' }] };
  const project = { root, test: { include, browser } };
  const plugin = config.plugins[0];
  assert.equal(plugin.configResolved.order, 'pre');
  plugin.configResolved.handler({ root: '/unused', test: { projects: [project] } });
  assert.equal(project.test.include, include);
  assert.deepEqual(
    browser.instances.map((instance) => instance.include),
    [['normal.spec.ts'], ['touch.spec.ts']],
  );
  const split = browser.instances;
  plugin.configResolved.handler({ root, test: { projects: [project] } });
  assert.equal(browser.instances, split);
});

test('leaves non-browser projects alone and uses the resolved root when the project has none', (t) => {
  const root = fixture(t);
  const nodeProject = { test: { include: ['normal.spec.ts'], browser: { enabled: false } } };
  const browser = { enabled: true, instances: [{ browser: 'chromium' }] };
  const project = { test: { include: ['touch.spec.ts'], browser } };
  config.plugins[0].configResolved.handler({
    root,
    test: { projects: ['other.config.ts', nodeProject, project] },
  });
  assert.deepEqual(nodeProject.test.browser, { enabled: false });
  assert.equal(browser.instances[0].name, 'chromium:pointer-touch');
});
