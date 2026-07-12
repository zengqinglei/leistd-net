---
name: developing-leistd-framework
description: 在 leistd-net 仓库中新增、修改、审查或排查 framework/components、framework/ddd-struct、公共 API、依赖注入、Options、NuGet 打包及随包文档时使用，例如“新增组件”“调整框架注册”“审视组件边界”。不用于下游项目仅消费 Leistd 包或只维护项目模板。
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

## 工作流

1. 检查分支、工作区和用户已有改动，确认受影响家族、依赖方向、公共表面和消费点。
2. 用邻近实现、现有测试和调用方建立当前行为基线。
3. 实施最小变更，并为行为风险补充对应测试和 XML 注释。
4. 公共 API、包依赖、注册、默认值或运行时语义变化时评估兼容性，并同步目标家族文档。
5. 构建、测试、执行文档检查，并打包到 `.tmp/local-feed`。
6. 检查受影响 `.nupkg` 的程序集、XML、随包文档和依赖，并从隔离本地源完成还原与构建。
7. Template 已消费该能力时使用 `developing-leistd-template` 验证受影响场景；未消费时运行组件家族集成测试，公共集成方式变化时再建立 `.tmp/` 下的临时宿主验证。

使用者可见的组件契约写入 `framework/docs/components/{family}.md`，DDD 基座契约写入 `framework/docs/ddd-struct/`；仅供仓库维护者使用的规则写入 `docs/framework/`。编写前先参考最新同类内容并按组件特性组织，不使用固定章节模板。公共能力缺少对应文档时主动创建并更新索引，只覆盖使用者必须知道的安装、注册、调用、默认行为和限制。

## 验证入口

```powershell
dotnet build framework/Leistd.Framework.slnx -c Release
dotnet test framework/Leistd.Framework.slnx -c Release
dotnet pack framework/Leistd.Framework.slnx -c Release -o .tmp/local-feed
pwsh framework/build/test-package-consumption.ps1
pwsh framework/build/check-docs-sync.ps1
pwsh framework/build/check-docs-api-drift.ps1
```

本地可用 `-PackageIds Leistd.Xxx` 只检查受影响包，CI 检查全部包。根据变更选择最小充分集合；未执行项和原因必须如实说明。
