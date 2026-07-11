# 三层交付与 AI 协作

本文说明 Framework、Skills、Template 的使用场景和端到端链路。长期约束见 [设计原则](./design-principles.md)，具体行为以源码、配置和所属层文档为准。

## 1. 三层交付面

| 层 | 交付内容 | 主要使用者 | 完成标准 |
| --- | --- | --- | --- |
| Framework | `framework/components/`、`framework/ddd-struct/` 和 NuGet 包 | 框架维护者、业务开发者 | 包可独立消费，行为、测试和随包文档一致 |
| Skills | 仓库维护、框架使用和项目协作 Skill | 执行对应任务的 AI | 准确触发，只加载必要事实并完成用户最终意图 |
| Template | `template/` 的后端、前端、规范和条件参数 | 新项目团队、模板维护者 | 条件场景可生成、构建、运行并消费 NuGet 包 |

三层不是运行时依赖链。Framework 提供能力，Skills 提供执行知识，Template 组合并验证能力；任何一层都不复制其他层的权威事实。

## 2. Skill 边界

| Skill | 场景 | 事实范围 |
| --- | --- | --- |
| `.claude/skills/developing-leistd-framework` | 开发 Framework | 框架源码、测试、随包文档和包验证 |
| `.claude/skills/developing-leistd-template` | 维护 Template | 模板源码、条件配置和实际生成结果 |
| `.claude/skills/maintaining-leistd-repository` | 跨层、CI、版本或发布维护 | 整个仓库及各专项 Skill 的验证结果 |
| `skills/leistd-net-framework` | 下游项目使用 `Leistd.*` | 项目实际安装包的文档、XML 和程序集 |
| `template/.agents/skills/leistd-project-workflow` | 生成项目或其他业务项目的完整协作 | 项目源码、配置、测试、CI 和已沉淀文档 |

维护 Skill 时同时使用官方 `skill-creator`。每个 Skill 必须在自己的分发环境中自洽，不能依赖不会一同交付的文件。

## 3. 事实入口

| 场景 | 稳定入口 | 按需事实 |
| --- | --- | --- |
| 框架设计与维护 | `docs/architecture/design-principles.md`、`docs/framework/development-guide.md` | 目标源码、测试、`.csproj`、`framework/docs/` |
| 框架组件使用 | `leistd-net-framework` | 已安装 NuGet 包的 `docs/*.md`、XML、项目配置 |
| 模板维护 | `docs/template/development-guide.md` | `template.json`、模板源码、生成场景 |
| 业务项目协作 | `leistd-project-workflow`、项目 `docs/README.md` | 项目源码、配置、测试、CI 和最新同类文档 |

历史 assessment、plan 和 Git 记录用于追溯，不作为当前规则入口。

## 4. 端到端链路

### 4.1 下游项目使用框架

1. 从项目文件、CPM 和还原资产确认宿主、目标框架及实际 `Leistd.*` 版本。
2. 由 `leistd-net-framework` 定位该版本包内文档和 XML，不从在线最新版或模型记忆猜 API。
3. 按项目现有边界完成注册、Options、端点、Store、DbContext 或迁移集成。
4. 构建并运行相关测试；需要运行时语义时启动真实宿主，验证主要路径和拒绝路径。
5. 仅将项目特有且长期复用的决策写入项目文档，框架事实继续引用包内文档。

### 4.2 开发 Framework

1. 读取设计原则、框架开发规范、目标项目和最新同类实现，确认组件边界与公共 API。
2. 修改源码和风险匹配的测试，评估依赖方向与兼容性。
3. 同步受影响的 XML 注释和 `framework/docs/` 使用者文档。
4. 构建、测试并打包到 `.tmp/local-feed`。
5. 检查包内容，并从隔离本地源完成 NuGet 消费验证。
6. Template 消费该能力时继续验证对应生成场景；否则使用组件集成测试或临时宿主闭环。

### 4.3 维护 Template

1. 读取模板开发规范、`template.json` 和邻近条件块，确定受影响参数组合。
2. 同步修改模板源码、测试、Mock、项目 Skill 和确有长期价值的生成项目文档。
3. 涉及 Framework 时先生成本地包，再通过 `PackageReference` 消费。
4. 在 `.tmp/generated-template/` 生成受影响场景，检查条件标记、占位符和旧路径残留。
5. 对生成结果执行还原、构建、测试和必要的前端构建。
6. 权限、审计、通知、实时或数据库变化时完成对应业务闭环。

### 4.4 业务项目协作

1. `leistd-project-workflow` 根据用户最终意图确定交付结果，而不是按固定阶段选择多个 Skill。
2. 从工作区、源码、配置、测试、CI、`docs/README.md` 和最新同类文档建立事实。
3. 按需加载 development、quality、delivery、documentation 或 bootstrap reference。
4. 完成方案、实现、审查、测试、协调或部署目标，并运行风险匹配的验证。
5. 仅在产生长期可复用信息时更新或创建最小权威文档；缺少文档不阻断低风险任务。
6. 汇报实际结果、未验证项和残余风险，不默认生成阶段报告。

### 4.5 跨层维护

跨 Framework、Template、Skills、CI、版本或发布的任务由 `maintaining-leistd-repository` 统筹：先确定受影响交付面，再由各专项 Skill 完成对应实现和验证，最后核对版本、文档、流水线与消费边界。某层不受影响时明确核对后跳过，不制造空改动。

## 5. 生成项目意图

| 用户最终意图 | 完成条件 |
| --- | --- |
| 只要分析或方案 | 决策、风险、验收和步骤明确，并说明尚未实现 |
| 要求实现或修复 | 可观察行为、必要测试、运行验证和文档同步完成 |
| 只要代码审查 | findings、证据、未检查范围和残余风险明确 |
| 只要测试或定位故障 | 命令和结果可复现，原因与未覆盖范围明确 |
| 多任务或跨会话推进 | 每个完成条件都有真实产物和验证证据 |
| 部署、迁移或回滚 | 获准操作、健康检查、核心路径和回滚判断完成 |

辅助步骤不转移最终交付责任。不可逆、生产、真实数据、密钥、费用或流量操作仍需明确确认。

## 6. Skill 验收

静态校验只证明目录和元数据合法。修改 Skill 的 description、边界或流程后，还应在不泄露预期答案的上下文中验证：

| 请求 | 应触发 | 关键边界 |
| --- | --- | --- |
| “给 auditing 增加 Options 并补测试” | `developing-leistd-framework` | 不加载模板或仓库统筹 Skill |
| “调整 Authorization API，并同步模板和 CI” | `maintaining-leistd-repository` + 专项 Skill | 不把单层通过当作全部完成 |
| “在现有 ASP.NET Core 项目中接入 Notifications” | `leistd-net-framework` | 读取已安装版本，不猜 API |
| “在模板项目中实现订单管理” | `leistd-project-workflow` | 实现和验证完成前不宣称交付 |
| “只审查这个 PR” | `leistd-project-workflow` | findings 优先，不擅自扩大为实现 |
| “部署到生产并准备回滚” | `leistd-project-workflow` | 未确认前不执行生产操作 |

验收结果保留在当前评审、PR 或 CI；只有团队或合规明确要求时才另建长期记录。
