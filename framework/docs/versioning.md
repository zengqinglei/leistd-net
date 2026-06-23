# Leistd 框架版本与发布规范

## 版本来源（VERSION 文件）

框架版本的**唯一来源**是仓库根的 **`VERSION`** 文件（基准 `x.y.z`），它存的是**最近一次已发布的正式版**。

- `framework/common.props` 在构建时自动读取 `VERSION` 作为 `VersionPrefix`，所有 `Leistd.*` 包同步该版本（无外部工具依赖）。
- 模板 `template/backend/Directory.Build.props` 的 `<LeistdFrameworkVersion>` 是字面值副本（生成项目需自包含），由发布流水线调用 `scripts/sync-version.ps1` 回写。

## 提交规范决定版本递增（Conventional Commits）

发版时由 `scripts/compute-version.ps1` 分析"自上个 `v*` tag 以来"的提交信息，算出下一个版本（默认"优先最小版本"）：

| 提交 | 递增 | 例 |
| --- | --- | --- |
| 普通提交 / `fix:` | Patch（默认） | `fix: 修复锁超时` → 0.8.0 → 0.8.1 |
| `feat:` / `feat(scope):` | Minor | `feat: 新增 Redis 锁` → 0.8.x → 0.9.0 |
| `feat!:` / 任意类型带 `!` / 含 `BREAKING CHANGE` | Major | → 1.0.0 |

> **提交信息务必遵循 [Conventional Commits](https://www.conventionalcommits.org/)** —— 它直接决定版本如何递增。

## 分支 → 包类型

| 分支 / 触发 | 版本形态 | 发布目标 | 工作流 |
| --- | --- | --- | --- |
| push `main` | `x.y.z`（正式，自动递增） | nuget.org | `release-stable.yml` |
| push `develop` | `x.y.z-beta.<N>` | nuget.org（预发布） | `release-beta.yml` |
| 每工作日定时（develop） | `x.y.z-preview.<yyyyMMdd>` | GitHub Packages（内部） | `release-nightly.yml` |

> 预发布后缀用**点分数字**（`-beta.12`、`-preview.20260623`），保证 NuGet 数值排序正确。

## 正式版发布（全自动）

**push 到 `main` 即自动发布**，无需手动打 tag：

1. `compute-version.ps1` 按提交推算新正式版本；
2. 回写 `VERSION` + 同步模板，提交 `chore: 发布 vX.Y.Z [skip ci]`；
3. 打 tag `vX.Y.Z`；
4. 打包 → 经 Trusted Publishing 推 nuget.org；
5. 创建 GitHub Release（自动生成 release notes）。

机制要点：
- 仅 `framework/`、`scripts/`、`VERSION`、release workflow 变更才触发发版；纯文档变更跳过。
- 回写提交带 `[skip ci]` 且过滤 `github-actions[bot]`，避免死循环。
- ⚠️ NuGet 包不可删（只能 unlist）。main 上每次有效变更都会产出一个正式版，请把控合入 main 的节奏。

## 鉴权

- **nuget.org**（stable / beta）：Trusted Publishing（OIDC，免长期 API Key）。需在 nuget.org 配置信任策略（仓库 + 工作流文件名 `release-stable.yml` / `release-beta.yml`），并设 `NUGET_USER` secret。
- **GitHub Packages**（nightly）：用内置 `GITHUB_TOKEN`，无需额外配置。

### 引用 nightly 包（内部测试）

GitHub Packages 需认证拉取，即使公开仓库：

```bash
dotnet nuget add source "https://nuget.pkg.github.com/zengqinglei/index.json" \
  --name leistd-nightly --username <你的GitHub用户名> \
  --password <PAT，需 read:packages> --store-password-in-clear-text

dotnet add package Leistd.Core --prerelease
```

## 本地手动操作（不发布）

```bash
# 打包（用 VERSION 文件的版本，产出到 framework/artifacts）
pwsh framework/build/pack.ps1

# 预览将推算出的版本（不修改任何文件）
pwsh scripts/compute-version.ps1                  # 正式版
pwsh scripts/compute-version.ps1 -PreRelease beta # beta

# 手动升基准版本：直接编辑 VERSION 文件即可
```

## 注意

- CPM 下第三方包版本集中在 `framework/Directory.Packages.props`；升级第三方依赖按提交规范评估影响。
- monorepo 统一版本：26 个包共享同一版本，要么全发要么全不发；`--skip-duplicate` 保证重跑幂等。
- 整个机制零外部版本工具（纯 git + PowerShell + MSBuild 读文件），与团队其它项目（如 ai-relay）的 VERSION 文件范式一致。
