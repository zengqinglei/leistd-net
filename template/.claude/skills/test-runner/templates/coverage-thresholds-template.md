# 测试覆盖率门槛模板

> 测试覆盖率标准定义

---

## 通用标准

### 推荐门槛

| 指标 | 门槛 | 说明 |
|------|------|------|
| **行覆盖率** | ≥80% | 至少 80% 的代码行被测试覆盖 |
| **分支覆盖率** | ≥70% | 至少 70% 的分支被测试覆盖 |
| **函数覆盖率** | ≥80% | 至少 80% 的函数被测试调用 |
| **语句覆盖率** | ≥80% | 至少 80% 的语句被测试执行 |

### 关键代码要求

以下关键代码必须达到 **100% 覆盖率**：

- ✅ 核心业务逻辑
- ✅ 支付相关代码
- ✅ 安全相关代码
- ✅ 数据转换逻辑
- ✅ 错误处理逻辑

---

## .NET 项目标准

### 门槛配置（.runsettings）

```xml
<CoverageThresholds>
  <LineCoverage>80</LineCoverage>
  <BranchCoverage>70</BranchCoverage>
  <MethodCoverage>80</MethodCoverage>
</CoverageThresholds>
```

### 检查命令

```bash
# 运行测试并检查覆盖率
dotnet test --settings:.runsettings --collect:"XPlat Code Coverage"

# 使用 ReportGenerator 生成报告
reportgenerator -reports:TestResults/**/coverage.cobertura.xml \
                -targetdir:coverage \
                -reporttypes:Html

# 检查覆盖率是否达标
# （通过 CI/CD 插件或自定义脚本）
```

---

## Node.js 项目标准

### 门槛配置（vitest.config.ts）

```typescript
coverage: {
  provider: 'v8',
  reporter: ['text', 'json-summary', 'html'],
  thresholds: {
    global: {
      branches: 70,
      functions: 80,
      lines: 80,
      statements: 80,
    },
  },
}
```

### 检查命令

```bash
# 运行测试并生成覆盖率报告
npx vitest run --coverage

# 查看覆盖率总结
cat coverage/coverage-summary.json
```

---

## Python 项目标准

### 门槛配置（pytest.ini）

```ini
[pytest]
addopts = --cov=. --cov-report=html --cov-report=term-missing --cov-fail-under=80
```

### 检查命令

```bash
# 运行测试并检查覆盖率
pytest --cov=. --cov-fail-under=80

# 生成 HTML 报告
pytest --cov=. --cov-report=html

# 查看覆盖率
coverage report
```

---

## 决策规则

### 通过标准

测试通过当且仅当：

1. ✅ **所有测试通过**：无失败测试
2. ✅ **覆盖率达标**：所有指标≥门槛
3. ✅ **关键代码全覆盖**：核心业务逻辑 100% 覆盖

### 打回标准

测试打回如果：

1. ❌ **有失败测试**：任一测试失败
2. ❌ **覆盖率不达标**：任一指标<门槛
3. ❌ **关键代码未覆盖**：核心业务逻辑有未覆盖代码

---

## 例外情况

### 可豁免的代码

以下代码可以不统计覆盖率：

- ✅ 纯 UI 组件（Component 模板）
- ✅ 配置类（Configuration）
- ✅ 数据传输对象（DTO）
- ✅ 自动生成的代码
- ✅ 第三方库的包装器

### 豁免申请流程

1. 在测试文件中添加注释说明
2. 在 PR 描述中说明豁免原因
3. 团队评审通过
4. 记录在案

---

## 持续改进

### 覆盖率提升计划

1. **短期**（1-2 周）：
   - 修复失败测试
   - 覆盖核心业务逻辑
   - 达到最低门槛（80%）

2. **中期**（1-2 月）：
   - 覆盖边界条件
   - 覆盖错误处理
   - 提升到 90%

3. **长期**（3-6 月）：
   - 覆盖所有业务逻辑
   - 维持 95%+ 覆盖率
   - 自动化覆盖率检查

### 监控与告警

- ✅ **CI 检查**：每次提交检查覆盖率
- ✅ **PR 检查**：PR 中显示覆盖率变化
- ✅ **周报**：每周发送覆盖率报告
- ✅ **告警**：覆盖率下降时通知团队

---

*模板版本：v1.0.0*
*最后更新：2026-06-07*
*维护：test-runner Skill*

