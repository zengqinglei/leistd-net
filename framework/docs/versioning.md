# Leistd 框架版本与发布规范

## 版本来源（单点）

整个框架使用**统一版本**，唯一来源是：

```
framework/common.props  →  <Version>X.Y.Z</Version>
```

所有 26 个 `Leistd.*` 包共享此版本。修改这一处即同步全部包。

模板侧引用版本在 `template/backend/Directory.Build.props` 的 `<LeistdFrameworkVersion>` 以**字面值**保存（生成项目需自包含，无法 import 仓库根文件），它**不是**第二个版本源，而是由唯一源单向同步的副本。同步**已自动化**，无需手工维护：

- **打包时自动**：`framework/build/pack.ps1` 在打包前内置调用 `sync-version.ps1`，把版本写入模板。
- **CI 校验**：每次 push / PR 运行 `sync-version.ps1 -Check`，不一致则失败。
- 需要时也可手动：`pwsh scripts/sync-version.ps1`（同步）/ `-Check`（仅校验，退出码 1 表示不一致）。

模板的 `Directory.Packages.props` 再通过 `$(LeistdFrameworkVersion)` 应用到各 `Leistd.*` 包引用。

## SemVer 约定

遵循 [语义化版本](https://semver.org)：

- **MAJOR**（X）：破坏性 API 变更（删除/改签名公共类型、改依赖方向）。
- **MINOR**（Y）：向后兼容的新增能力。
- **PATCH**（Z）：向后兼容的缺陷修复。
- 预发布：`X.Y.Z-preview.N`、`X.Y.Z-rc.N`。

## 发布检查清单

1. 确认 `dotnet build framework/Leistd.Framework.slnx -c Release` 0 错误。
2. 按变更性质更新**唯一源** `framework/common.props` 的 `<Version>`。
3. `pwsh framework/build/pack.ps1`，核对 `artifacts/` 下 nupkg 数量与版本（PDB 已内嵌）。
   > 版本同步已**内置**：`pack.ps1` 打包前会自动运行 `sync-version.ps1` 把版本写入模板，无需手动同步。
4. 配置 git 远程并推送代码（Source Link 需要远程提交可达，否则消费方无法步进源码）。
5. `$env:NUGET_API_KEY=...; pwsh framework/build/push.ps1`（或指定 `-Source` 私有源）。
6. 记录变更（CHANGELOG / release notes）。

> CI（`.github/workflows/ci.yml`）在每次 push / PR 时运行 `sync-version.ps1 -Check`，版本不一致会使构建失败，作为最终防线。

## 注意

- CPM 下第三方包版本集中在 `framework/Directory.Packages.props`；升级第三方依赖属于框架变更，按 SemVer 评估影响。
- 不要让不同包出现不一致版本——统一版本是本框架的核心约束。
