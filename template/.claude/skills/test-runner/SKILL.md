---
name: test-runner
description: |
  运行测试套件并生成报告 - 通用测试流程。

  **当以下情况时使用此 Skill**:
  (1) PR 提交后自动测试
  (2) 开发完成后验证
  (3) 部署前测试
  (4) 用户要求"运行测试"

  **调用 Agent**:架构师助手(architect)
metadata:
  openclaw:
    requires: []
    skillKey: "test-runner"
user-invocable: true
disable-model-invocation: false
---

# 运行测试 (test-runner)

> 运行测试套件并生成报告

## 🚨 执行前必读

- ✅ **直接操作文件**:使用 `read/exec` 工具,不调用脚本
- ✅ **读取项目配置**:优先读取项目内的测试配置文件
- ⚠️ **无配置用默认**:无配置文件时使用通用测试框架和默认配置
- ⚠️ **降级提示**:当项目无测试配置时,**必须提示用户**建议创建配置文件
- ✅ **覆盖率检查**:检查测试覆盖率是否达标
- ✅ **测试报告**:生成详细的测试报告

---

## 📋 工作流程

```
1. 检测项目类型
   → .NET 项目:xUnit/NUnit + dotnet test
   → Node 项目:Vitest/Jest
   → Python 项目:pytest

2. 读取项目配置
   → .runsettings(.NET)
   → vitest.config.ts(Node)
   → pytest.ini(Python)
   → 如无配置,使用默认配置

3. 运行测试
   → 单元测试
   → 集成测试
   → E2E 测试(可选)

4. 收集结果
   → 通过/失败数量
   → 覆盖率数据
   → 失败测试详情

5. 生成报告
   → 测试总结
   → 失败测试列表
   → 覆盖率分析
   → 决策建议

6. 决策
   → 通过:所有测试通过 + 覆盖率达标
   → 打回:有失败测试或覆盖率不达标
```

---

## 🎯 核心能力

### 1. 检测项目类型

**检测方法**:
```markdown
1. .NET 项目
   - 检测文件:*.csproj, *.sln
   - 测试框架:xUnit, NUnit, MSTest
   - 运行命令:dotnet test

2. Node.js 项目
   - 检测文件:package.json
   - 测试框架:Vitest, Jest
   - 运行命令:npm test 或 npx vitest

3. Python 项目
   - 检测文件:requirements.txt, setup.py
   - 测试框架:pytest, unittest
   - 运行命令:pytest
```

---

### 2. 读取项目配置

**配置文件**(按优先级):

**.NET**:
```markdown
1. .runsettings(推荐)
2. xunit.runner.json
3. testsettings
```

**Node.js**:
```markdown
1. vitest.config.ts(推荐)
2. vitest.config.js
3. jest.config.js
4. package.json 中的 test 配置
```

**Python**:
```markdown
1. pytest.ini
2. pyproject.toml 中的 [tool.pytest]
3. setup.cfg
```

**降级处理**:
```markdown
如无配置文件:
1. 使用测试框架的默认配置
2. **必须提示用户**:
   "⚠️ 检测到项目未配置测试运行设置,已使用默认配置运行测试。
   建议创建 {配置文件} 以声明项目特定的测试配置。
   参考模板:templates/{模板名称}(本 Skill 提供)"
3. 继续执行,使用默认配置运行测试
```

---

### 3. 运行测试

**.NET 项目**:
```bash
# 运行所有测试
dotnet test

# 运行测试并生成覆盖率报告
dotnet test --collect:"XPlat Code Coverage"

# 运行测试并指定设置文件
dotnet test --settings:.runsettings

# 运行特定测试
dotnet test --filter "FullyQualifiedName~UserService"

# 详细输出
dotnet test --logger "console;verbosity=detailed"
```

**Node.js 项目**:
```bash
# 运行所有测试(Vitest)
npx vitest run

# 运行测试并生成覆盖率报告
npx vitest run --coverage

# 运行特定测试
npx vitest run UserService

# 监听模式(开发时)
npx vitest

# 详细输出
npx vitest run --reporter=verbose
```

**Python 项目**:
```bash
# 运行所有测试
pytest

# 运行测试并生成覆盖率报告
pytest --cov=.

# 运行特定测试
pytest tests/test_user.py

# 详细输出
pytest -v
```

---

### 4. 收集结果

**测试结果**:
```markdown
- 总测试数:125
- 通过:120
- 失败:3
- 跳过:2
- 耗时:12.5s

失败测试:
1. UserService.should_create_user
   - 错误:Expected 200 but got 500
   - 文件:tests/user-service.spec.ts:45

2. OrderService.should_calculate_total
   - 错误:Timeout exceeded
   - 文件:tests/order-service.spec.ts:78
```

**覆盖率数据**:
```markdown
- 行覆盖率:85.3%
- 分支覆盖率:72.1%
- 函数覆盖率:88.9%

未覆盖的关键文件:
1. src/utils.ts - 45% 覆盖率
2. src/helpers.ts - 32% 覆盖率
```

---

### 5. 生成报告

**报告格式**：
```markdown
## 测试报告

### 总结

- ✅ 通过测试：120
- ❌ 失败测试：3
- ⏭️ 跳过测试：2
- 📊 总测试数：125
- ⏱️ 耗时：12.5s

### 验收项覆盖（五段闭环 Plan 场景）

若存在 Plan.md，测试报告必须包含验收项覆盖表：

| 验收项 | 测试类型 | 测试结果 | 证据 |
|--------|---------|---------|------|
| 验收项 1 | 单元/集成/E2E | ✅ 通过 / ❌ 失败 / ⚠️ 未覆盖 | 测试文件：行号 / 测试报告路径 |
| 验收项 2 | 单元/集成/E2E | ✅ 通过 / ❌ 失败 / ⚠️ 未覆盖 | 测试文件：行号 / 测试报告路径 |

如 Must have 验收项无测试覆盖，必须标记：

```markdown
⚠️ 验收项 "xxx" 未覆盖测试，建议补充。
```

### 覆盖率

| 类型 | 覆盖率 | 门槛 | 状态 |
|------|--------|------|------|
| 行覆盖率 | 85.3% | 80% | ✅ |
| 分支覆盖率 | 72.1% | 70% | ✅ |
| 函数覆盖率 | 88.9% | 80% | ✅ |

### 失败测试

1. **测试**: UserService.should_create_user
   **文件**: tests/user-service.spec.ts:45
   **错误**: Expected 200 but got 500
   **建议**: 检查用户创建逻辑

2. **测试**: OrderService.should_calculate_total
   **文件**: tests/order-service.spec.ts:78
   **错误**: Timeout exceeded
   **建议**: 检查订单计算逻辑

### 决策

❌ **打回**: 发现 3 个失败测试，必须修复后重新运行。
```

---

### 6. 覆盖率门槛

**通用标准**(如无项目配置):

| 类型 | 门槛 | 说明 |
|------|------|------|
| **行覆盖率** | ≥80% | 至少 80% 的代码行被测试覆盖 |
| **分支覆盖率** | ≥70% | 至少 70% 的分支被测试覆盖 |
| **函数覆盖率** | ≥80% | 至少 80% 的函数被测试调用 |

**决策规则**:
```markdown
通过:
- ✅ 所有测试通过
- ✅ 覆盖率达标(所有指标≥门槛)

打回:
- ❌ 有失败测试
- ❌ 覆盖率不达标(任一指标<门槛)
```

---

## 📚 参考模板(本 Skill 提供)

**相对路径引用**(从本 Skill 目录):

| 模板 | 路径 | 用途 |
|------|------|------|
| **runsettings 模板** | `templates/runsettings-template` | .NET 测试配置 |
| **Vitest 配置模板** | `templates/vitest-config-template.ts` | Vitest 配置 |
| **覆盖率门槛模板** | `templates/coverage-thresholds-template.md` | 覆盖率标准 |
| **降级策略** | `templates/fallback-strategy.md` | 无配置时的降级处理 |

---

## ⛔ 禁止事项

- ❌ 不要跳过项目配置文件的读取
- ❌ 不要忽略失败测试
- ❌ 不要提交覆盖率不达标的代码
- ❌ 不要混淆测试通过率
- ❌ 不要假设配置文件存在(优先读取项目配置)

---

## ✅ 最佳实践

### 测试前

1. **读取配置**:优先读取项目的测试配置文件
2. **检测项目类型**:自动识别使用的测试框架
3. **准备环境**:确保测试依赖已安装

### 测试中

1. **详细输出**:使用详细模式输出测试结果
2. **收集信息**:记录失败测试的文件、行号、错误信息
3. **生成覆盖率**:同时运行覆盖率检查

### 测试后

1. **生成报告**:详细的测试报告
2. **失败分析**:分析失败原因并提供修复建议
3. **覆盖率分析**:识别未覆盖的关键代码
4. **决策建议**:通过/打回的明确建议

---

## 📊 质量指标

| 指标 | 目标值 | 说明 |
|------|--------|------|
| 测试通过率 | 100% | 所有测试必须通过 |
| 行覆盖率 | ≥80% | 至少 80% 代码行被覆盖 |
| 分支覆盖率 | ≥70% | 至少 70% 分支被覆盖 |
| 函数覆盖率 | ≥80% | 至少 80% 函数被调用 |
| 关键代码覆盖率 | 100% | 核心业务逻辑必须全覆盖 |

---

## 🔗 完成衔接

**测试完成后,自动通知派发方(小架)。**

小架作为执行者,测试任务,完成后通过 completion event 自动回报小架。小架根据结果决定下一步:

| 测试结果 | 小架决策 | 下一动作 |
|---------|---------|----------|
| ✅ 全部通过 | 继续编排 | 执行部署 |
| ❌ 测试失败 | 阻塞 | 小架自行修复后重新测试,修复后重新派发测试 |
| ⚠️ 部分通过 | 需决策 | 向用户报告,由用户决定是否继续部署 |

**测试报告应包含**:通过/失败数量、失败用例详情、覆盖率数据。

---

*最后更新:2026-06-14 文档驱动版 v2.1.0*
*维护:通用质量助手*
