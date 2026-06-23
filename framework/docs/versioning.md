# Leistd 框架版本与发布规范

## 版本来源（单点）

整个框架使用**统一版本**，唯一来源是：

```
framework/common.props  →  <Version>X.Y.Z</Version>
```

所有 26 个 `Leistd.*` 包共享此版本。修改这一处即同步全部包。

模板侧引用版本在 `template/backend/Directory.Build.props` 的 `<LeistdFrameworkVersion>` 声明，发布新框架版本后需同步更新该值（并在 `template/backend/Directory.Packages.props` 通过 `$(LeistdFrameworkVersion)` 自动生效）。

## SemVer 约定

遵循 [语义化版本](https://semver.org)：

- **MAJOR**（X）：破坏性 API 变更（删除/改签名公共类型、改依赖方向）。
- **MINOR**（Y）：向后兼容的新增能力。
- **PATCH**（Z）：向后兼容的缺陷修复。
- 预发布：`X.Y.Z-preview.N`、`X.Y.Z-rc.N`。

## 发布检查清单

1. 确认 `dotnet build framework/Leistd.Framework.slnx -c Release` 0 错误。
2. 按变更性质更新 `framework/common.props` 的 `<Version>`。
3. 同步更新 `template/backend/Directory.Build.props` 的 `<LeistdFrameworkVersion>`。
4. `pwsh framework/build/pack.ps1`，核对 `artifacts/` 下 nupkg 数量与版本（PDB 已内嵌）。
5. 配置 git 远程并推送代码（Source Link 需要远程提交可达，否则消费方无法步进源码）。
6. `$env:NUGET_API_KEY=...; pwsh framework/build/push.ps1`（或指定 `-Source` 私有源）。
7. 记录变更（CHANGELOG / release notes）。

## 注意

- CPM 下第三方包版本集中在 `framework/Directory.Packages.props`；升级第三方依赖属于框架变更，按 SemVer 评估影响。
- 不要让不同包出现不一致版本——统一版本是本框架的核心约束。
