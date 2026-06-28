# 快速开始

## 第 1 步：描述目标

用自然语言说明你想完成什么，包括：

- 背景：为什么要做。
- 目标：做到什么程度算成功。
- 范围：包含什么，不包含什么。
- 约束：时间、技术栈、兼容性、部署、安全等限制。

## 第 2 步：让 AI 生成 Plan

AI 应先读取：

- `docs/standards/agent-workflow.md`
- `docs/requirements/registry.md`
- 相关 `docs/modules/{module}/` 文档
- 相关编码、测试、API、部署规范

Plan 默认保存到：

```text
docs/requirements/{req-id}-plan.md
```

## 第 3 步：人类确认

确认以下内容后再进入实现：

- 需求边界是否正确。
- 验收标准是否可测试。
- 数据、权限、部署、迁移风险是否明确。
- 是否需要人工审批或外部凭证。

## 第 4 步：AI 执行

AI 实施时应遵守：

- 最小变更，避免重写无关代码。
- 先读规范，再改代码。
- 不覆盖用户未确认的已有变更。
- 不把密钥、Token、真实账号写入文档或代码。

## 第 5 步：质量验证

完成后至少提供：

- 修改清单。
- 测试命令与结果。
- 未验证项与风险。
- 如有代码审查，输出到 `docs/reports/code-review/`。

## 常用文档

- 需求模板：`docs/requirements/req-template-plan.md`
- 模块模板：`docs/modules/_template/`
- 编码规范：`docs/standards/code-standard/`
- 测试规范：`docs/standards/test.md`
- 部署规范：`docs/deploy/README.md`
