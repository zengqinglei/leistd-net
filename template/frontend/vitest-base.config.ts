import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { defineConfig, type Plugin } from 'vitest/config';

/**
 * 按 Angular 精确发现的 spec 源码分配 fine/touch 实例，每个 spec 在原实例内只分配一次，空组省略。
 * Linux 关闭触屏仿真不恢复 fine/hover；isolate 仅隔离 iframe，所以触屏须独立实例，细指针先跑。
 * 名称保留原实例前缀，:pointer-touch 后缀供测试助手拒绝错误实例调用；其余实例选项保持不变。
 */
export function pointerInstances<
  T extends { browser: string; name?: string; sequence?: { groupOrder?: number } },
>(instances: T[], files: string[], root: string) {
  const touch = files.filter((file) =>
    /\bsetCoarsePointer\s*\(/.test(readFileSync(resolve(root, file), 'utf8')),
  );
  const touchSet = new Set(touch);
  const groups = [
    { name: 'fine', files: files.filter((file) => !touchSet.has(file)), order: 0 },
    { name: 'touch', files: touch, order: 1 },
  ].filter((group) => group.files.length);
  return instances.flatMap((instance) =>
    groups.map((group) => ({
      ...instance,
      name: `${instance.name ?? instance.browser}:pointer-${group.name}`,
      include: group.files,
      sequence: { ...instance.sequence, groupOrder: group.order },
    })),
  );
}

const pointerIsolation: Plugin = {
  name: 'test-pointer-browser-isolation',
  configResolved: {
    order: 'pre',
    handler(config) {
      // Angular 在 config 阶段写入精确 include，须在 Vitest 展开浏览器实例前拆分。
      for (const project of config.test?.projects ?? []) {
        if (typeof project !== 'object' || project === null || !('test' in project)) continue;
        const test = project.test;
        const browser = test?.browser;
        if (!test || !browser?.enabled || !browser.instances?.length || !test.include?.length)
          continue;
        if (
          browser.instances.some(
            (instance) =>
              instance.name?.endsWith(':pointer-touch') || instance.name?.endsWith(':pointer-fine'),
          )
        )
          continue;
        browser.instances = pointerInstances(
          browser.instances,
          test.include,
          project.root ?? config.root,
        );
      }
    },
  },
};

// 由 angular.json 的 test.options.runnerConfig 加载；构建、浏览器与用例范围仍由 Angular 的 unit-test 构建器决定。
export default defineConfig({
  plugins: [pointerIsolation],
  test: {
    // 每条用例开始前还原 vi.spyOn 创建的替身与 vi.stubGlobal 替换的全局值：
    // 用例改了原型方法、console 或 URL 静态方法时，不能漏到后面的用例里。
    // 只管这两种；Object.defineProperty 之类的直接修改与假计时器仍由用例自己还原
    restoreMocks: true,
    unstubGlobals: true,
  },
});
