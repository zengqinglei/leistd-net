# Vitest 配置模板（Node.js 测试配置）

> Vitest 测试运行器配置文件（TypeScript）

---

## 基础模板

```typescript
// vitest.config.ts
import { defineConfig } from 'vitest/config';
import angular from '@analogjs/vite-plugin-angular';

export default defineConfig({
  plugins: [angular()],
  test: {
    // 全局超时（毫秒）
    timeout: 30000,
    
    // 测试环境
    environment: 'jsdom',
    
    // 包含的文件
    include: ['**/*.{test,spec}.{js,mjs,cjs,ts,mts,cts,jsx,tsx}'],
    
    // 排除的文件
    exclude: ['**/node_modules/**', '**/dist/**', '**/e2e/**'],
    
    // 覆盖率配置
    coverage: {
      provider: 'v8',
      reporter: ['text', 'json', 'html'],
      exclude: [
        'node_modules/',
        'src/test-setup.ts',
        '**/*.spec.ts',
        '**/*.test.ts',
      ],
    },
    
    // 全局测试设置
    setupFiles: ['./src/test-setup.ts'],
    
    // 自动清理 mocks
    clearMocks: true,
    
    // 失败后继续运行
    bail: false,
  },
});
```

---

## 完整模板（含覆盖率阈值）

```typescript
// vitest.config.ts
import { defineConfig } from 'vitest/config';
import angular from '@analogjs/vite-plugin-angular';
import path from 'path';

export default defineConfig({
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
      '@features': path.resolve(__dirname, './src/app/features'),
      '@shared': path.resolve(__dirname, './src/app/shared'),
      '@core': path.resolve(__dirname, './src/app/core'),
    },
  },
  
  plugins: [angular()],
  
  test: {
    // 全局超时（毫秒）
    timeout: 30000,
    
    // 测试环境
    environment: 'jsdom',
    
    // 包含的文件
    include: [
      'src/**/*.{test,spec}.{js,mjs,cjs,ts,mts,cts,jsx,tsx}',
    ],
    
    // 排除的文件
    exclude: [
      '**/node_modules/**',
      '**/dist/**',
      '**/cypress/**',
      '**/.{idea,git,cache,output,temp}/**',
      '**/{karma,rollup,webpack,vite,vitest,jest,ava,babel,nyc,cypress,tsup,build}.config.*',
      '**/e2e/**',
    ],
    
    // 测试设置
    setupFiles: ['./src/test-setup.ts'],
    
    // 全局测试 API
    globals: true,
    
    // 自动清理 mocks
    clearMocks: true,
    
    // 模拟导入
    mockReset: true,
    
    // 覆盖率配置
    coverage: {
      provider: 'v8',
      all: true,
      reporter: ['text', 'json-summary', 'json', 'html'],
      
      // 覆盖率阈值
      thresholds: {
        global: {
          branches: 70,
          functions: 80,
          lines: 80,
          statements: 80,
        },
      },
      
      // 排除的文件
      exclude: [
        'node_modules/',
        'src/test-setup.ts',
        'src/main.ts',
        '**/*.spec.ts',
        '**/*.test.ts',
        '**/*.component.ts',
        '**/*.module.ts',
        '**/environments/**',
      ],
    },
    
    // 报告配置
    reporters: ['default', 'html'],
    
    // 输出目录
    outputFile: {
      html: 'coverage/index.html',
      json: 'coverage/coverage.json',
    },
    
    // 失败后继续运行
    bail: false,
    
    // 并行运行
    maxConcurrency: 4,
  },
});
```

---

## 使用方式

### 1. 创建配置文件

```bash
# 在项目根目录创建
cp templates/vitest-config-template.ts vitest.config.ts

# 编辑配置
vim vitest.config.ts
```

### 2. 安装依赖

```bash
# 安装 Vitest 和相关依赖
npm install -D vitest @vitest/coverage-v8 jsdom @vitest/ui
```

### 3. 运行测试

```bash
# 运行所有测试
npx vitest run

# 运行测试并生成覆盖率报告
npx vitest run --coverage

# 运行特定测试
npx vitest run UserService

# 监听模式（开发时）
npx vitest

# 查看详细报告
npx vitest --reporter=verbose
```

### 4. 查看覆盖率报告

```bash
# 查看 HTML 报告
open coverage/index.html

# 查看 JSON 报告
cat coverage/coverage.json
```

---

## 配置说明

### 测试配置

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `timeout` | 全局超时（毫秒） | `30000` |
| `environment` | 测试环境 | `'jsdom'` |
| `include` | 包含的测试文件 | `['**/*.spec.ts']` |
| `exclude` | 排除的文件 | `['node_modules/**']` |
| `setupFiles` | 全局设置文件 | `[]` |
| `globals` | 全局测试 API | `false` |
| `clearMocks` | 自动清理 mocks | `true` |

### 覆盖率配置

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `provider` | 覆盖率提供者 | `'v8'` |
| `reporter` | 报告格式 | `['text', 'html']` |
| `thresholds.global` | 全局覆盖率阈值 | 无 |
| `exclude` | 排除的文件 | `[]` |

### 覆盖率阈值

| 阈值 | 说明 | 推荐值 |
|------|------|--------|
| `branches` | 分支覆盖率 | `70%` |
| `functions` | 函数覆盖率 | `80%` |
| `lines` | 行覆盖率 | `80%` |
| `statements` | 语句覆盖率 | `80%` |

---

## 最佳实践

### 测试配置

- ✅ **设置超时**：防止测试无限期运行
- ✅ **路径别名**：使用 `@/` 简化导入
- ✅ **全局 API**：启用 `describe`, `it`, `expect`
- ✅ **设置文件**：配置全局测试环境

### 代码覆盖率

- ✅ **包含所有源码**：`all: true`
- ✅ **排除测试文件**：不统计测试代码的覆盖率
- ✅ **设置阈值**：定义覆盖率门槛
- ✅ **多种报告**：生成文本、JSON、HTML 报告

### 持续集成

- ✅ **CI 配置**：在 CI/CD 中使用相同的配置
- ✅ **报告上传**：将测试报告上传到 CI 系统
- ✅ **覆盖率检查**：在 PR 中检查覆盖率变化
- ✅ **失败通知**：测试失败时通知团队

---

*模板版本：v1.0.0*
*最后更新：2026-06-07*
*维护：test-runner Skill*
