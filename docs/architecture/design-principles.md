# leistd-net 设计原则

> 本文只定义长期约束。三层交付面、AI 协作场景和各步骤引用的文档见
> [三层交付与 AI 协作](./collaboration-scenarios.md)。

## 1. 三层边界

### 1.1 Framework：提供可组合能力

- `framework/components/` 提供与业务无关的通用能力；`framework/ddd-struct/` 提供可选的 DDD 分层基座。
- `components` 不依赖 `ddd-struct`；Core/Domain 不依赖 Web、EF Core、SignalR 等具体实现。
- 每个组件家族职责单一，通过宿主显式调用 `Add*`、`Map*` 和 Options 进行组合，不替其他组件隐式注册基础设施。
- 公共 API、注册方式和运行时语义以源码为准，面向使用者的说明位于 `framework/docs/` 并随 NuGet 包分发。
- 组件文档示例只使用该组件真实依赖和原生 .NET API；DDD 组合示例由 `framework/docs/ddd-struct/` 承载。
- 本地包固定输出到 `.tmp/local-feed`；仓库 `NuGet.Config` 不登记本地源。

### 1.2 Skills：提供按场景加载的执行知识

- Skill 负责告诉 AI 何时读取哪些事实、如何完成闭环，不复制源码、组件文档或项目规范。
- 仓库 Skill、框架使用 Skill、生成项目 Skill 分别随各自使用场景交付，不跨分发边界引用不可用文件。
- Skill 不建立运行时依赖，也不强制所有任务经过同一阶段状态机；模型按用户意图和风险选择所需能力。

### 1.3 Template：验证并示范组合

- `template/` 通过 `PackageReference` 消费框架，是可运行的集成示例，不绕过 NuGet 边界引用框架源码。
- `Api` 是组合根；Application 不反向依赖 Infrastructure；条件参数必须真正裁剪代码、项目和文档。
- 模板变更必须通过实际生成项目验证，不能只验证模板源目录。

## 2. 文档原则

### 2.1 单一信息源

- 同一事实只维护一处，其他位置使用链接和简短上下文，不复制正文。
- 源码定义 API 与行为；项目配置定义实际工具和命令；长期文档定义仍需跨会话共享的决策与规则。
- 历史 assessment、plan 和 Git 记录是证据，不作为当前规则；长期结论应回写稳定文档。

### 2.2 自主沉淀

- 开始工作时先读取最新的同类文档、邻近实现和实际配置，不从固定模板复制结构。
- 只有信息会被后续任务、其他成员或运行维护再次使用时才创建文档；聊天答复、Git diff、测试输出和 CI 已能承载的信息不重复写报告。
- 存在同类文档时更新权威文件；不存在但沉淀价值明确时，按所属交付面的信息归属主动创建权威文档，只写已验证且需长期复用的信息。只有业务决策、归类或落点不明确时才询问用户。
- 不预建空目录、占位文档、需求模板、报告模板或配置模板。

### 2.3 可读性

- 标题表达内容而非流程编号；先结论和约束，后必要细节。
- 删除背景叙事、重复清单、不会执行的约定和无实际值的占位字段。
- 文档应能让人和 AI 从稳定入口直接定位，不增加 manifest、索引 schema 等旁路元数据。

## 3. Skill 原则

- `SKILL.md` frontmatter 只使用 `name` 和 `description`；名称为 kebab-case，目录名与名称一致。
- description 同时说明能力、准确触发场景和相邻边界，避免宽泛触发或依赖正文才能理解用途。
- 正文只保留作用域、事实读取顺序、最小工作流、风险边界和验证要求；详细事实留在其权威文档。
- 共享同一项目事实和交付责任的场景使用一个路由 Skill，并通过一层 references 渐进加载；只有受众、分发边界或触发语义真正独立时才拆分，避免重复元数据和规则漂移。
- 跨工具 Skill 使用中立目录并保持自包含，不依赖 `CLAUDE.md`、`AGENTS.md` 等工具专属入口。
- references、scripts、assets 仅在能减少重复工作或提高确定性时保留；不为完整目录结构而创建资源。
- 每个 Skill 都遵循“先找最新同类文档，只沉淀已验证且需长期复用的信息”的规则，不强制生成 Plan、registry、context、handoff 或阶段报告。
- 每种用户最终意图只有一个交付所有者；组合其他 Skill 不转移完成责任，也不强制所有任务经过同一阶段状态机。
- 所有 Skill 必须通过 `skill-creator/scripts/quick_validate.py`；变更触发描述或流程后，还应使用架构地图中的真实请求验证能否正确触发和执行。

## 4. 验证原则

- 验证范围与风险匹配：局部改动运行最小相关检查；公共 API、条件模板或跨层变更扩大到消费端和场景矩阵。
- 不把未执行、失败或降级的检查描述为通过；明确记录命令、结果和残余风险即可，不默认另建报告。
- 框架公共变更按“源码与测试 -> 文档 -> NuGet 包内容与隔离消费 -> 实际调用方”验证完整链路；只有 Template 消费该能力时才进入对应模板场景。
- 高风险安全、权限、数据迁移、生产配置、部署和破坏性操作必须先取得用户明确确认。

## 5. 规范入口

| 范围 | 权威入口 |
| --- | --- |
| 三层场景与文档读写链路 | `docs/architecture/collaboration-scenarios.md` |
| 仓库内部 AI 协作 | `.agents/skills/developing-leistd-framework/SKILL.md`、`.agents/skills/developing-leistd-template/SKILL.md`、`.agents/skills/maintaining-leistd-repository/SKILL.md` |
| 框架开发 | `docs/framework/development-guide.md` |
| 框架版本与发布 | `docs/framework/versioning.md` |
| 模板维护 | `docs/template/development-guide.md` |
| 生成项目 AI 协作 | `template/.agents/skills/leistd-project-workflow/SKILL.md`、`template/docs/README.md` |
| 组件与 DDD 使用 | `framework/docs/README.md` |
