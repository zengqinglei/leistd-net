# Leistd 框架版本与发布规范

## 版本来源（单点 = git）

框架版本**不再手填**，由 [GitVersion](https://gitversion.net)（配置见仓库根 `GitVersion.yml`）从 **git tag + 历史 + 提交信息**推算，CI 中由 `GitVersion.MsBuild` 注入到所有 `Leistd.*` 包。

- `framework/common.props` 的 `<VersionPrefix>` 仅是**本地无 GitVersion 时的回退基准**，不是发布版本来源。
- 模板 `template/backend/Directory.Build.props` 的 `<LeistdFrameworkVersion>` 是字面值副本，发布时由流水线调用 `scripts/sync-version.ps1 -Version <GitVersion算出的版本>` 回写（生成项目需自包含，无法跑 GitVersion）。

## 提交规范决定版本递增（Conventional Commits）

版本如何递增由**提交信息**决定，默认"优先最小版本"：

| 提交 | 递增 | 例 |
| --- | --- | --- |
| 普通提交 | Patch（默认） | `chore: 调整日志` → 0.8.0 → 0.8.1 |
| `fix:` | Patch | `fix: 修复锁超时` |
| `feat:` | Minor | `feat: 新增 Redis 锁` → 0.8.x → 0.9.0 |
| `feat!:` / 含 `BREAKING CHANGE:` | Major | → 1.0.0 |
| `+semver: none` | 不递增 | GitVersion 原生标记 |

（也兼容 GitVersion 原生 `+semver: minor|major|patch|none` 标记。）

## 分支 → 包类型

| 分支 / 触发 | 版本形态 | 发布目标 | 工作流 |
| --- | --- | --- | --- |
| `main` 打 tag `v*.*.*` | `x.y.z`（正式） | nuget.org | `release-stable.yml` |
| `develop` 推送 | `x.y.z-beta.N` | nuget.org（预发布） | `release-beta.yml` |
| 每工作日定时（develop） | `x.y.z-preview.<yyyyMMdd>` | GitHub Packages（内部） | `release-nightly.yml` |

> 预发布后缀一律用**点分数字**（`-beta.1`、`-preview.20260623`），保证 NuGet 数值排序正确。

## 发布正式版（操作步骤）

正式版**只在打 tag 时发**（避免每次提交污染 nuget.org，NuGet 包不可删只能 unlist）：

```bash
# 1. 确保 main 已包含要发布的提交，本地拉到最新
git checkout main && git pull

# 2. 打版本 tag（版本号应与 GitVersion 将算出的一致，遵循上面提交规范的累积结果）
git tag v0.8.0
git push origin v0.8.0
```

推送 tag 后 `release-stable.yml` 自动：GitVersion 算版本 → 同步模板 → 打包 → 经 Trusted Publishing 推 nuget.org → 创建 GitHub Release（自动生成 release notes）。

## 鉴权

- **nuget.org**：Trusted Publishing（OIDC，免长期 API Key）。需在 nuget.org 配置信任策略（仓库 + 工作流文件名），并设 `NUGET_USER` secret。
- **GitHub Packages**（nightly）：用内置 `GITHUB_TOKEN`，无需额外配置。

### 引用 nightly 包（内部测试）

GitHub Packages 需认证拉取，即使公开仓库：

```bash
dotnet nuget add source "https://nuget.pkg.github.com/zengqinglei/index.json" \
  --name leistd-nightly --username <你的GitHub用户名> \
  --password <PAT，需 read:packages> --store-password-in-clear-text

dotnet add package Leistd.Core --prerelease
```

## 本地手动打包（不发布）

```bash
pwsh framework/build/pack.ps1   # 用 VersionPrefix 回退版本，产出到 framework/artifacts
```

## 注意

- CPM 下第三方包版本集中在 `framework/Directory.Packages.props`；升级第三方依赖属于框架变更，按提交规范评估影响。
- monorepo 统一版本：26 个包共享同一 GitVersion 版本，要么全发要么全不发；`--skip-duplicate` 保证重跑幂等。
- `GitVersion.MsBuild` 仅在 CI（`GITHUB_ACTIONS=true`）启用，本地构建用 `VersionPrefix` 回退，避免浅克隆/无 git 时算错。
