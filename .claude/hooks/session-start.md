本仓库（leistd-net 模板）有项目级开发工作流。动手实现需求前，请先读技能 `using-leistd-workflow`（位于 .claude/skills/），它是工作流发现入口，会指引你按意图选择 requirement-plan / task-manager / coding / code-review / test-runner / deploy，并要求先读 template/docs/standards/agent-workflow.md。

如果任务涉及 `framework/components`、`framework/ddd-struct` 或任何 `Leistd.*` 组件 API / DI / Options / 运行时语义，请同时读技能 `using-leistd-net-framework`，按 `framework/docs/` 与源码核对组件事实源，不要凭记忆臆造 API。
