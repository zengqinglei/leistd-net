---
name: developing-leistd-framework
description: 在 leistd-net 仓库中为 framework/components、framework/ddd-struct、公共 API、依赖注入、Options、NuGet 打包及随包文档设计方案、制定实施计划、新增、修改、审查或排查时使用，例如“新增组件”“规划 DDD 基座调整”“审视组件边界”。不用于下游项目仅消费 Leistd 包或只维护项目模板。
---

# 开发 Leistd 框架

## 事实与边界

先读取：

1. `docs/architecture/design-principles.md` 和 `docs/framework/development-guide.md`。
2. 目标家族全部 `.csproj`、公共类型、DI 入口、Options、实现和测试。
3. `framework/docs/` 中目标文档及一至两篇最新同类组件文档。
4. 公共 API、包依赖或运行时语义变化时读取 `docs/framework/versioning.md`。

源码和项目引用定义实际 API 与行为；文档冲突时修正权威文档，不为兼容旧说明保留错误实现。

保持以下边界：

- `components` 不依赖 `ddd-struct`；Core/Domain 不依赖 Web、EF Core 或其他具体基础设施。
- 组件通过宿主显式组合，不替其他组件注册服务、映射端点或隐式挂载拦截器。
- 公共 API、命名、目录和依赖沿用同类组件规范，避免无实际收益的新抽象。
- 组件文档示例只使用该组件真实依赖；DDD 组合示例留在 DDD 文档。
- 注释与组件文档按 `docs/framework/development-guide.md` §4 编写；公共契约在接口或基类定义，实现使用继承文档。

## 方案与实施计划

涉及通用组件、DDD 基座、公共 API 或包边界的方案设计时，先读取 `docs/README.md` 并搜索同主题最新文档：

- 仍在比较候选或诊断现状时，跨会话材料写入 `docs/assessments/YYYY-MM-DD-<topic>.md`；
- 方案已经选定且需要任务分解、实施顺序和验收时，写入 `docs/plans/YYYY-MM-DD-<topic>.md`；
- 长期维护规则才写入 `docs/framework/`；
- 已实现且使用者必须知道的公共契约才写入 `framework/docs/`。

临时分析默认留在当前答复。不得把未实施方案、迁移步骤、任务状态、分支记录或仓库验证过程写入随 NuGet 分发的 `framework/docs/`。同时影响 Template、Skill、CI 或发布流程时，改用 `maintaining-leistd-repository` 维护一份跨交付面计划。

## 工作流

1. 检查分支、工作区和用户已有改动，确认受影响家族、依赖方向、公共表面和消费点。
2. 用邻近实现、现有测试和调用方建立当前行为基线。
3. 实施最小变更，并为行为风险补充对应测试和 XML 注释。
4. 公共 API、包依赖、注册、默认值或运行时语义变化时同步所有当前消费者和目标家族文档，不保留未发布兼容层。
5. 按变化选择验证：Markdown 检查引用与骨架；XML 变更构建受影响项目，关键示例单独编译；行为变化运行相关测试。
6. 随包内容或公共契约变化时打包到 `.tmp/local-feed`，检查 XML、文档及依赖；包依赖或集成契约变化时验证隔离消费。
7. 影响 Template 消费方式时使用 `developing-leistd-template` 验证相关生成场景；其他运行时语义由组件测试或最小宿主验证。

组件契约归 `framework/docs/components/{family}.md`，DDD 组合归 `framework/docs/ddd-struct/`，维护规则归 `docs/framework/`。缺少必要文档时创建最小权威说明并更新索引，不复制精确签名或维护过程。

## 验证入口

```powershell
dotnet build framework/Leistd.Framework.slnx -c Release
dotnet test framework/Leistd.Framework.slnx -c Release
pwsh scripts/check-all.ps1            # 全部静态闸门（唯一清单来源，-List 只看清单）
pwsh framework/build/pack-local-feed.ps1
pwsh framework/build/test-package-consumption.ps1
```

本地可用 `-PackageIds Leistd.Xxx` 只检查受影响包，CI 检查全部包。根据变更选择最小充分集合；未执行项和原因必须如实说明。
