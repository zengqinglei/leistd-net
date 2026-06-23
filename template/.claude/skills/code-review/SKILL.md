---
name: code-review
description: |
  代码规范检查与审查 - 通用检查流程。

  **当以下情况时使用此 Skill**:
  (1) 代码提交前检查
  (2) PR 创建时检查
  (3) 用户要求"检查代码规范" / "代码审查"
  (4) 开发完成后验证

  **调用 Agent**：架构师助手（architect）
metadata:
  openclaw:
    requires: []
    skillKey: "code-review"
user-invocable: true
disable-model-invocation: false
---

# 代码规范审查 (code-review)

> 自动化工具检查 + 项目规范清单审查

## 🚨 执行前必读

- ✅ **直接操作文件**：使用 `read/exec` 工具，不调用脚本
- ✅ **读取项目规范**：优先读取项目内的规范文档及其附录检查清单
- ⚠️ **无规范用默认**：无规范文档时使用本 Skill 模板的默认规则
- ⚠️ **降级提示**：当项目无规范文档时，**必须提示用户**建议创建规范文档
- ✅ **违规分级**：P0（阻塞）/ P1（严重）/ P2（建议）
- ✅ **检查报告**：生成详细的检查报告

---

## 📋 工作流程

```
1. 读取项目规范文档（见下方索引）
   → 提取文档末尾的"附录：规范检查清单"
   → 如无规范文档，使用本 Skill 模板默认规则

2. 自动化工具检查
   → .NET: dotnet format --verify-no-changes
   → Angular: npx eslint / npx prettier --check

3. 规范检查清单审查
   → 逐条对照代码变更与检查清单
   → 每项标注 ✅ 通过 或 ❌ 违规

4. 分类分级
   → P0：违反强制规则（必须修复，阻塞提交）
   → P1：违反推荐规则（尽快修复）
   → P2：风格建议

5. 生成报告
   → 写入 docs/reviews/{feature}-code-review.md
```

---

## 📂 项目规范索引

**本 Skill 通过扫描项目文档来应用具体项目的规范规则。**

扫描项目 `docs/standards/` 目录，读取所有相关规范文档。常见文档结构：

| 类型 | 常见路径 | 包含内容 |
|------|----------|----------|
| **后端规范** | `docs/standards/code-standard/backend-develop.md` | 分层架构、编码规范、命名规范、DTO 规范、Controller 规范 |
| **前端规范** | `docs/standards/code-standard/frontend-develop.md` | Angular 规范、PrimeNG 使用、Tailwind CSS、组件规范 |
| **文档命名** | `docs/standards/document-naming.md` | 文档命名规范 |
| **测试规范** | `docs/standards/test.md` | 测试类型、覆盖率和命令 |

**检查清单来源**：每个规范文档末尾的 `## 附录：规范检查清单` 章节。

**降级处理**：当项目无规范文档时，使用本 Skill 模板：
- 后端模板：`templates/code-standard-backend.md`
- 前端模板：`templates/code-standard-frontend.md`

---

## 🎯 核心能力

### 0. 需求对齐检查（五段闭环 Plan 场景）

若存在 Plan.md，代码审查必须增加需求对齐检查：

| 检查项 | 方法 |
|--------|------|
| Must have 覆盖 | 对照 `## 5. 验收闭环` 逐条检查代码是否实现 |
| 策略偏离 | 对照 `## 2. 核心策略` 检查架构和目录是否按策略执行 |
| 文件遗漏 | 对照 `## 3.2 文件变更清单` 检查是否有遗漏文件 |
| 风险对策 | 对照 `## 4. 风险及应对策略` 检查风险对策是否落地 |

**P0 条件**：Must have 明确要求但代码未实现 → P0 违规，阻塞提交。

### 1. 读取项目规范

**操作**：
```markdown
使用 `read` 工具读取：
1. `{project}/docs/standards/code-standard/backend-develop.md`（后端）
2. `{project}/docs/standards/code-standard/frontend-develop.md`（前端）

提取文档末尾的"附录：规范检查清单"章节。
该清单由项目维护，包含该项目特定的编码规范检查项。

如无规范文档：
- 使用本 Skill 模板：`templates/code-standard-backend.md` 和 `templates/code-standard-frontend.md`
- 提示用户创建项目规范文档
```

**提取信息**：
```markdown
从规范文档中提取：
- 分层架构规则
- 编码规范（record/主构造函数/namespace 等）
- 命名规范（DTO/Service/Component 等）
- 检查清单（逐条可勾选的规范项）
```

---

### 2. 自动化工具检查

**.NET 项目**：
```bash
dotnet format --verify-no-changes --verbosity detailed
```

**Angular/Node.js 项目**：
```bash
npx eslint . --ext .ts
npx prettier --check "src/**/*.{ts,html,css}"
```

**文档规范检查**（如项目有文档命名规范）：
- `docs/` 下目录无数字前缀
- Markdown 文件名为 lowercase-kebab-case，`README.md` 除外
- 文档路径不使用 snake_case
- Markdown 链接有效（无断裂链接）

### 2.5 自动修复

允许的自动修复：
```bash
cd apps/server && dotnet format
cd apps/web && npm run lint:fix
```

**要求**：
- 自动修复后必须重新运行检查
- 自动修复不得改变业务逻辑
- 命名、架构、安全问题不得静默修复，必须说明

---

### 3. 规范检查清单审查

**流程**：
1. 获取本次代码变更范围（git diff 或文件列表）
2. 读取规范文档附录的检查清单
3. 逐条对照变更代码，判断是否通过
4. 输出每项的结果（✅ / ❌）

**输出格式**：
```markdown
## 规范检查清单审查结果

### 后端规范（backend-develop.md）

#### ✅ 分层架构检查
- [x] HTTP 相关处理保留在 API 层
- [x] Application 层协调业务逻辑
- [x] Domain 层包含核心业务逻辑
- [x] Infrastructure 层负责技术实现
- [x] Domain 层无 EF Core 引用

#### ✅ 编码规范检查
- [x] 使用主构造函数
- [x] 使用文件范围 namespace
- [ ] DTO 使用 record 类型 ← ❌ 违规：使用了 class
- [x] 异步方法名以 Async 结尾
- [x] 实体使用充血模型

### 前端规范（frontend-develop.md）

#### ✅ 组件规范检查
- [x] 优先使用 PrimeNG v21 组件
- [ ] 自定义样式使用 Tailwind CSS v4 ← ❌ 违规：手写了 176 行 CSS
- [x] 组件遵循单一职责原则
- [x] 展示型组件使用 OnPush 策略
```

---

### 4. 分类分级

| 等级 | 说明 | 处理方式 | 示例 |
|------|------|---------|------|
| **P0** | 违反强制规则 | ❌ 必须修复，阻塞提交 | DTO 用 class 而非 record |
| **P1** | 违反推荐规则 | ⚠️ 尽快修复 | 缺少 `[Display]` 标注 |
| **P2** | 风格建议 | 💡 有空时修复 | 颜色硬编码而非主题变量 |

---

### 5. 生成报告

**报告存储路径**：`{project}/docs/reviews/{feature}-code-review.md`

**报告格式**：
```markdown
# {功能名称} 代码审查报告

> **审查时间**: YYYY-MM-DD HH:MM GMT+8
> **审查范围**: {commit range 或文件列表}
> **审查依据**: {project}/docs/standards/code-standard/*.md

## 一、违规统计

| 等级 | 数量 | 说明 |
|------|------|------|
| P0 | N | 违反强制规则 |
| P1 | N | 违反推荐规则 |
| P2 | N | 风格建议 |

## 二、后端规范审查

（逐条清单结果）

## 三、前端规范审查

（逐条清单结果）

## 四、P0 问题详情

（每个 P0 问题的文件、行号、违规内容、修复建议）

## 五、结论

（通过 / 打回）
```

---

## ⛔ 禁止事项

- ❌ 不要跳过项目规范文档的读取
- ❌ 不要忽略 P0 问题
- ❌ 不要自动提交有 P0 问题的代码
- ❌ 不要混淆违规等级
- ❌ 不要假设配置文件存在（优先读取项目配置）

---

## ✅ 最佳实践

1. **先读规范再审查**：不要凭记忆审查，每次都要重新读取规范文档
2. **逐条对照**：检查清单的每一项都必须有明确的 ✅ 或 ❌
3. **引用原文**：报告中引用规范文档的原文作为依据
4. **修复建议具体**：给出具体的代码修改建议，而非笼统的"请修复"
5. **报告留存**：报告写入 docs/reviews/，供后续参考

---

## 🔗 完成衔接

**规范审查完成后，根据结果决定下一步：**

| 审查结果 | 下一步 | 说明 |
|---------|--------|------|
| ✅ 无 P0 违规 | 允许提交代码，通知派发方 | 派发方决定是否派发测试 |
| ❌ 有 P0 违规 | 阻塞，必须修复后重新审查 | 修复后再次执行 code-review |
| ⚠️ 仅有 P1/P2 | 记录问题，允许提交 | P1/P2 不阻塞，但应尽快修复 |

**审查作为 coding Skill 的内置步骤时**：小架在开发流程中自动执行审查，不需要外部触发。

**审查作为独立任务时**：由小架通过 sessions_spawn 派发，完成后通过 completion event 回报。

---

*最后更新：2026-06-14 通用规范审查版 v3.1.0*
*维护：通用质量助手*
