// @ts-check
import { readdirSync, realpathSync } from 'node:fs';
import path from 'node:path';

import eslint from '@eslint/js';
import { defineConfig, globalIgnores } from 'eslint/config';
import tseslint from 'typescript-eslint';
import angular from 'angular-eslint';

import eslintConfigPrettier from 'eslint-config-prettier/flat';
import { createNodeResolver, importX } from 'eslint-plugin-import-x';
import unusedImports from 'eslint-plugin-unused-imports';

// 解析器返回真实路径，项目经符号链接打开时也要按真实路径比对
const projectRoot = realpathSync(import.meta.dirname);

// 显式解析器：方向检查与跨功能检查都按它解析出的真实文件判断，不看路径字符串
const resolver = createNodeResolver({
  extensions: ['.ts', '.mjs', '.js', '.json'],
  conditionNames: ['types', 'import', 'default'],
  tsconfig: { configFile: path.join(projectRoot, 'tsconfig.json') },
});

// 目录依赖方向，见 docs/standards/coding-frontend.md 第 2 节。功能目录按磁盘现状生成，新增功能自动纳入。
const app = 'src/app';
const features = readdirSync(path.join(projectRoot, app, 'features'), { withFileTypes: true })
  .filter((entry) => entry.isDirectory())
  .map((entry) => `${app}/features/${entry.name}`);
const assembly = ['app.ts', 'app.config.ts', 'app.interceptors.ts', 'app.routes.ts'].map(
  (file) => `${app}/${file}`,
);

/**
 * 生成 `import-x/no-restricted-paths` 的规则区。功能之间的引用由下方 `local/feature-boundaries` 检查。
 *
 * @param {{ mock: boolean }} restrict `mock`：是否禁止引用 `_mock`（测试文件放开）。
 */
function directionZones({ mock }) {
  const zone = (target, from, message) => ({ target, from, message });
  const mockFrom = mock ? ['_mock'] : [];
  return [
    zone(
      `${app}/core`,
      [`${app}/layout`, `${app}/features`, ...assembly, ...mockFrom],
      'core may depend only on core and shared.',
    ),
    zone(
      `${app}/layout`,
      [`${app}/features`, ...assembly, ...mockFrom],
      'layout may depend only on core, layout and shared.',
    ),
    zone(
      `${app}/shared`,
      [`${app}/core`, `${app}/layout`, `${app}/features`, ...assembly, ...mockFrom],
      'shared may depend only on shared.',
    ),
    ...features.map((feature) =>
      zone(
        feature,
        [`${app}/layout`, ...assembly, ...mockFrom],
        'A feature may depend only on core, shared and itself.',
      ),
    ),
    zone(
      '_mock',
      [`${app}/layout`, ...assembly],
      '_mock must not depend on layout or the application assembly.',
    ),
  ];
}

const featureRoots = features.map((feature) => path.join(projectRoot, feature) + path.sep);
const featureOf = (/** @type {string} */ file) =>
  featureRoots.find((root) => file.startsWith(root));
const routeLoaders = new Set(['loadComponent', 'loadChildren']);

/**
 * 动态导入是否直接位于路由文件里 `loadComponent`/`loadChildren` 的加载函数中，如
 * `loadComponent: () => import('...').then((m) => m.Page)`，或块体里 `return import(...)...`。
 * 加载函数内嵌套回调里的 `import()` 与其他位置的都不算懒加载路由。
 *
 * @param {any} node ImportExpression
 */
function isRouteLoader(node) {
  let current = node.parent;
  while (
    ['MemberExpression', 'CallExpression', 'AwaitExpression', 'ChainExpression'].includes(
      current?.type,
    )
  ) {
    current = current.parent;
  }
  if (current?.type === 'ReturnStatement' && current.parent?.type === 'BlockStatement') {
    current = current.parent.parent;
  }
  return (
    (current?.type === 'ArrowFunctionExpression' || current?.type === 'FunctionExpression') &&
    current.parent?.type === 'Property' &&
    routeLoaders.has(current.parent.key?.name)
  );
}

/** 功能之间不互相依赖；唯一例外是路由文件经 loadComponent/loadChildren 懒加载其他功能的页面。 */
const featureBoundaries = {
  meta: {
    type: 'problem',
    messages: {
      crossFeature:
        'A feature may not depend on another feature; move what features share into core or shared. Route files may lazy-load other features only through loadComponent/loadChildren.',
    },
    schema: [],
  },
  create(/** @type {any} */ context) {
    let filename = context.physicalFilename;
    try {
      filename = realpathSync(filename);
    } catch {
      // 标准输入等没有磁盘文件的情形按原路径比对
    }
    const own = featureOf(filename);
    if (!own) return {};
    const isRoutes = filename.endsWith('.routes.ts');
    const check = (/** @type {any} */ node, /** @type {any} */ source) => {
      // 非字符串字面量的 import() 由 no-restricted-syntax 拦截；不带 from 的 export 没有 source
      if (source?.type !== 'Literal' || typeof source.value !== 'string') return;
      const resolved = resolver.resolve(source.value, filename);
      const target = resolved.found && resolved.path ? featureOf(resolved.path) : undefined;
      if (!target || target === own) return;
      if (isRoutes && node.type === 'ImportExpression' && isRouteLoader(node)) return;
      context.report({ node: source, messageId: 'crossFeature' });
    };
    return {
      ImportDeclaration: (/** @type {any} */ node) => check(node, node.source),
      ExportNamedDeclaration: (/** @type {any} */ node) => check(node, node.source),
      ExportAllDeclaration: (/** @type {any} */ node) => check(node, node.source),
      ImportExpression: (/** @type {any} */ node) => check(node, node.source),
    };
  },
};

const tests = ['**/*.spec.ts', '**/*.testing.ts'];

export default defineConfig(
  globalIgnores(['.angular/**', 'coverage/**', 'dist/**']),
  {
    files: ['**/*.ts'],
    plugins: {
      'import-x': importX,
      local: { rules: { 'feature-boundaries': featureBoundaries } },
      'unused-imports': unusedImports,
    },
    settings: {
      // libs/ui 经 tsconfig paths 以 @spartan-ng/helm/* 引用，排序上与 npm 包同组
      'import-x/external-module-folders': ['node_modules', 'libs/ui'],
      // 显式解析器：解析不到的路径会被方向检查静默放过，所以同时开启 no-unresolved
      'import-x/resolver-next': [resolver],
    },
    extends: [
      eslint.configs.recommended,
      ...tseslint.configs.recommended,
      ...tseslint.configs.stylistic,
      ...angular.configs.tsRecommended,
      eslintConfigPrettier,
    ],
    processor: angular.processInlineTemplates,
    rules: {
      '@angular-eslint/component-selector': [
        'error',
        { type: 'element', prefix: 'app', style: 'kebab-case' },
      ],
      '@angular-eslint/directive-selector': [
        'error',
        { type: 'attribute', prefix: 'app', style: 'camelCase' },
      ],
      '@angular-eslint/no-pipe-impure': 'error',
      '@angular-eslint/prefer-output-readonly': 'error',
      '@typescript-eslint/no-unused-vars': 'off',
      'import-x/no-duplicates': 'error',
      'import-x/no-restricted-paths': [
        'error',
        { basePath: projectRoot, zones: directionZones({ mock: true }) },
      ],
      'import-x/no-unassigned-import': 'error',
      'import-x/no-unresolved': 'error',
      'import-x/order': [
        'error',
        {
          alphabetize: { order: 'asc' },
          'newlines-between': 'always',
          groups: ['builtin', 'external', 'internal', ['parent', 'sibling', 'index'], 'type'],
        },
      ],
      'local/feature-boundaries': 'error',
      'no-restricted-imports': [
        'error',
        {
          paths: [
            {
              name: '@angular/forms',
              importNames: ['FormsModule', 'ReactiveFormsModule', 'NgModel', 'NgForm', 'NgControl'],
              message:
                'Use Angular Signal Forms (@angular/forms/signals); FormsModule/ReactiveFormsModule/ngModel are not allowed.',
            },
          ],
        },
      ],
      'unused-imports/no-unused-imports': 'error',
      'unused-imports/no-unused-vars': [
        'error',
        { vars: 'all', varsIgnorePattern: '^_', args: 'after-used', argsIgnorePattern: '^_' },
      ],
      'prefer-template': 'error',
      // 导入路径一律写成普通字符串字面量：模板字符串与表达式不经 import-x 解析，方向与存在性都检查不到
      'no-restricted-syntax': [
        'error',
        {
          selector: "ImportExpression[source.type!='Literal']",
          message:
            'Write import() paths as plain string literals so they can be resolved and checked.',
        },
      ],
    },
  },
  {
    files: tests,
    rules: {
      'import-x/no-restricted-paths': [
        'error',
        { basePath: projectRoot, zones: directionZones({ mock: false }) },
      ],
    },
  },
  {
    files: ['_mock/**/*.ts'],
    rules: {
      '@typescript-eslint/no-explicit-any': 'off',
    },
  },
  {
    files: ['**/*.html'],
    extends: [...angular.configs.templateRecommended, ...angular.configs.templateAccessibility],
  },
);
