---
name: requirement-plan
description: |
  将用户的想法、背景或自然语言需求整理为可执行的五段闭环 Plan.md。

  使用时机：
  (1) 用户提出新想法、需求、目标或业务背景
  (2) 需要澄清范围、目标、风险和验收标准
  (3) 需要生成或修订 docs/requirements/{req-id}-plan.md
metadata:
  openclaw:
    requires: []
    skillKey: "requirement-plan"
user-invocable: true
disable-model-invocation: false
---

# 需求分析 (requirement-plan)

> 输入自然语言需求，输出符合项目规范的五段闭环 Plan 草案。

## 必读顺序

1. 先读 `docs/standards/agent-workflow.md`，获取路径、Plan 结构、阶段交接包和人工确认规则。
2. 如索引存在，按需读取文档命名、技术栈、模块或业务规范；不要一次性加载无关文档。
3. 无项目规范时，使用本 Skill 模板，并在输出中说明假设和建议补齐项目规范。

## 输入

- 用户原始想法、需求、目标或背景。
- 现有项目规范、模块文档、相关代码结构（按需读取）。
- 用户已给出的限制：时间、范围、技术偏好、风险边界。

## 工作流程

1. **识别需求**：提取背景、用户角色、核心场景、目标、范围边界和非目标。
2. **澄清缺口**：仅对会影响方案正确性的关键问题提问；可合理假设的问题写入 Plan 的“待确认”。
3. **设计方案**：给出总体策略、目录/模块影响、核心流程、关键风险和验证方式。
4. **生成 Plan**：保存到 `docs/requirements/{req-id}-plan.md`，文件名使用 lowercase kebab-case。
5. **自检质量**：检查五段结构、Must have 映射、风险处理、完成定义和冗余内容。
6. **输出交接包**：标记 `gateStatus: needs-confirmation`，默认下一步为用户确认后进入 `task-manager`。

## Plan 最低要求

以 `docs/standards/agent-workflow.md` 为准，必须包含：

- `## 1. 需求&背景&目标`：背景、需求、目标、范围边界。
- `## 2. 核心策略`：总体策略、影响目录/模块、核心流程、注意事项。
- `## 3. 实施步骤`：步骤、产出物、验证方式、文件变更清单、执行顺序。
- `## 4. 风险及应对策略`：风险、概率、影响、应对策略、触发后处理。
- `## 5. 验收闭环`：Must have 3-7 个，Nice to have 0-3 个，验收映射和完成定义。

## 输出

- Plan 文件路径。
- 需求 ID 和需求名称。
- 关键假设与待确认问题。
- 质量检查摘要。
- 阶段交接包：

```yaml
handoff:
  reqId: REQ-YYYYMMDD-NNN
  phase: 0
  phaseName: requirement-plan
  gateStatus: needs-confirmation
  nextRecommendedSkill: task-manager
  userConfirmationRequired: true
  artifacts:
    - docs/requirements/req-yyyymmdd-nnn-plan.md
```

## 按需资源

| 资源 | 路径 | 读取时机 |
| --- | --- | --- |
| Plan 模板 | `templates/plan.md.template` | 创建 Plan 时 |
| Agent 工作流模板 | `templates/agent-workflow-template.md` | 项目缺少规范且用户要求创建规范时 |
| Agent 工作流字段说明 | `references/agent-workflow-schema-template.md` | 需要解释或修复规范索引时 |
| 示例 | `references/agent-workflow-examples.md` | 用户要求示例或复杂项目结构参考时 |
| 降级策略 | `references/fallback-strategy.md` | 项目无规范且需要说明降级时 |

## 禁止事项

- 不生成没有验收闭环的 Plan。
- 不让 Must have 缺少实施步骤或验证方式。
- 不在 Plan 中贴大段代码。
- 不把模板默认技术栈当作未知项目的事实。
- 不重复描述同一信息。
