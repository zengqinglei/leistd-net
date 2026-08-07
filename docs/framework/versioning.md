# Leistd 框架版本与发布规范

## 版本来源（VERSION 文件）

框架版本的**唯一来源**是仓库根的 **`VERSION`** 文件（基准 `x.y.z`），它存的是**最近一次已发布的正式版**。

- `framework/common.props` 在构建时自动读取 `VERSION` 作为 `VersionPrefix`，所有 `Leistd.*` 包同步该版本（无外部工具依赖）。
- 模板 `template/backend/Directory.Build.props` 的 `<LeistdFrameworkVersion>` 是字面值副本（生成项目需自包含），由发布流水线 `release.yml` 在发版时回写。

## 0.13.0 授权体系最终态（破坏性变更）

本次一并完成，不保留过渡重载与兼容分支。升级需要一次代码调整加一次 EF 迁移。

**`Leistd.Authorization.Core`**

- `IPermissionGrantStore` 重构为两个读方法：`GetGrantsAsync(providerName, providerKey)` 与 `GetGrantsForSubjectAsync(userId, roleIds)`。原有 `IsGrantedToUserAsync` / `IsGrantedToRoleAsync` / `IsGrantedToAnyRoleAsync` / `IsGrantedToUserOrRolesAsync` / `GetGrantedPermissionsFor*Async` 全部移除——它们互为退化形式，且逐权限查询无法表达三态。
- `IPermissionGrantManager` 改为按 `(providerName, providerKey)` 的通用签名：`GrantAsync` / `RevokeAsync` / `ReplaceGrantsAsync`。原有 `GrantToUserAsync` / `GrantToRoleAsync` / `RevokeFromUserAsync` / `RevokeFromRoleAsync` 与两个转发读方法移除，读职责归 Store。
- 新增 `PermissionGrantEffect`（`Granted` / `Prohibited`）、`PermissionGrant`、`PermissionGrantSet`、`SubjectPermissionGrants`、`PermissionGrantConcurrencyException`。
- `IPermissionDefinitionContext.AddPermission` 移除：每个权限都必须归属于某个组，游离权限无法被权限管理界面表达。权限名改为**全局唯一**，重复注册在启动阶段抛异常。
- `IPermissionDefinitionManager` 新增 `GetGroups()`、`IsEffectivelyEnabled(name)`、`GetAncestorNames(name)`、`GetDescendantNames(name)`。
- `IPermissionChecker` 注册生命周期从 Transient 改为 **Scoped**；调用签名不变，但同一作用域内只解析一次主体、只读取一次授予。
- **运行时语义变更**：权限未定义或未启用一律拒绝（此前不校验定义，`IsEnabled` 形同虚设）。若此前依赖"未定义权限也能通过 Checker"，需要补齐定义。

**`Leistd.Authorization.EntityFrameworkCore`**

- `PermissionGrantRecord` 新增非空 `Effect` 列（**以字符串持久化**，长度 32）；`ForUser` / `ForRole` 静态工厂移除（它们会绕过写时归一化）。
- 新增 `AuthorizationRevisionRecord` 表，`ConfigureAuthorization()` 会一并映射，缺失会导致批量替换无法做乐观并发校验。
- 需要一次 EF 迁移：为 `PermissionGrantRecord` 增加 `Effect`（`varchar(32)`，既有行填 `'Granted'`），并新建版本表。唯一索引保持 `(PermissionName, ProviderName, ProviderKey)` 不变。
- 迁移里对 `Effect` 做数据转换时**按字符串比较**（`Effect = 'Granted'`），不要对该列写整数字面量。
- 写入行为变更：授予子权限会补齐祖先，撤销父权限会级联清理子孙。既有的扁平授予在下一次经由 Manager 写入时被归一化。

**新增包**

- `Leistd.Authorization.Resource.Core` / `Leistd.Authorization.Resource.EntityFrameworkCore`：资源实例授权（可选）。
- `Leistd.Authorization.DataScope.Core`：数据范围（可选）。

三者互不依赖，按需引用；不引用即完全不产生模型与运行时成本。

## 提交规范决定版本递增（Conventional Commits）

发版时流水线（`release.yml`）分析"自上个 `v*` tag 以来"的提交信息，算出下一个版本（默认"优先最小版本"）：

| 提交 | 递增 | 例 |
| --- | --- | --- |
| 普通提交 / `fix:` | Patch（默认） | `fix: 修复锁超时` → 0.8.0 → 0.8.1 |
| `feat:` / `feat(scope):` | Minor | `feat: 新增 Redis 锁` → 0.8.x → 0.9.0 |
| `feat!:` / 任意类型带 `!` / 含 `BREAKING CHANGE` | Major | → 1.0.0 |

> **提交信息务必遵循 [Conventional Commits](https://www.conventionalcommits.org/)** —— 它直接决定版本如何递增。

## 分支 → 包类型

| 分支 / 触发 | 版本形态 | 发布目标 | 工作流 |
| --- | --- | --- | --- |
| push `main` | `x.y.z`（正式，自动递增） | nuget.org | `release.yml`（stable 通道） |
| push `develop` | `x.y.z-beta.<N>` | nuget.org（预发布） | `release.yml`（beta 通道） |
| 每工作日定时（develop） | `x.y.z-preview.<yyyyMMdd>` | GitHub Packages（内部） | `release.yml`（nightly 通道） |

> 三个通道由**单个** `release.yml` 内部按 `github.ref` / `github.event_name` 自动判定。

> 预发布后缀用**点分数字**（`-beta.12`、`-preview.20260623`），保证 NuGet 数值排序正确。

## 正式版发布（全自动）

**push 到 `main` 即自动发布**，无需手动打 tag：

1. 按提交推算新正式版本；
2. 回写 `VERSION` + 同步模板，提交 `chore: 发布 vX.Y.Z [skip ci]`；
3. 打 tag `vX.Y.Z`；
4. 打包 → 经 Trusted Publishing 推 nuget.org；
5. 创建 GitHub Release（自动生成 release notes）。

机制要点：
- 触发发版的变更：`VERSION`、**`framework/` 源码**（框架内非 docs 的 `.md` 除外）、或 **`framework/docs/` 组件文档**（文档随包分发，故文档更新也发一版送达）。
- **不**触发 stable 正式版：`docs/framework/`、`template/` 与仓库根的 `*.md`（内部开发规范、模板文档、仓库元文档），避免非交付内容改动误发。develop 分支仍按 beta 通道策略执行。
- 回写提交带 `[skip ci]` 且过滤 `github-actions[bot]`，避免死循环。
- ⚠️ NuGet 包不可删（只能 unlist）。框架源码每次有效变更都会产出一个正式版，请把控合入 main 的节奏。

## 鉴权

- **nuget.org**（stable / beta）：Trusted Publishing（OIDC，免长期 API Key）。需在 nuget.org 配置信任策略（仓库 + 工作流文件名 `release.yml`），并设 `NUGET_USER` secret。
- **GitHub Packages**（nightly）：用内置 `GITHUB_TOKEN`，无需额外配置。

### 引用 nightly 包（内部测试）

GitHub Packages 需认证拉取，即使公开仓库：

```bash
dotnet nuget add source "https://nuget.pkg.github.com/zengqinglei/index.json" --name leistd-nightly --username <你的GitHub用户名> --password <PAT，需 read:packages> --store-password-in-clear-text

dotnet add package Leistd.Core --prerelease
```

## 本地手动操作（不发布）

```bash
# 打包（用 VERSION 文件的版本，固定产出到本地 feed）
dotnet pack framework/Leistd.Framework.slnx -c Release -o .tmp/local-feed

# 想发布更高的基准版本：直接编辑 VERSION 文件即可（CI 在此基础上按提交递增）
```

> `.tmp/local-feed` 仅用于本地开发并由 `.gitignore` 忽略；CI 发布继续使用 `framework/artifacts`。版本推算、模板同步、打包发布逻辑全部内联于 `.github/workflows/release.yml`，发布由 push 自动触发，本地通常无需手动介入。

## 注意

- CPM 下第三方包版本集中在 `framework/Directory.Packages.props`；升级第三方依赖按提交规范评估影响。
- monorepo 统一版本：所有可发布框架包共享同一版本，要么全发要么全不发；`--skip-duplicate` 保证重跑幂等。
- 整个机制零外部版本工具（纯 git + PowerShell + MSBuild 读文件），与团队其它项目（如 ai-relay）的 VERSION 文件范式一致。
