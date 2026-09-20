# Leistd 框架版本与发布规范

## 版本来源（VERSION 文件）

框架版本的**唯一来源**是仓库根的 `VERSION` 文件（`x.y.z`），构建和发布都以它为基准。

### `VERSION` 是"上一次已发布的正式版"，不是"下一版"

这一条必须明确，因为两种读法都讲得通，而选错会拿到不存在的包：

- `VERSION` 的内容 **等于最近一次 stable 发布的版本号**，由 `release.yml` 在发版成功后回写；
- **下一版由流水线按提交算出**（见下一节），`VERSION` 里不会提前出现；
- 因此在 `main` 上读到 `0.12.0`，含义是"nuget.org 上最新的正式版是 0.12.0"，
  而不是"正在做 0.12.0"。
- 在 `develop` 上读到的仍是 `0.12.0`，但该分支产出的包是 `0.13.0-beta.<N>`
  ——`VERSION` 不随 beta 递增。要知道当前 beta 版本号，看 nuget.org 的预发布列表或流水线日志，
  不要从 `VERSION` 推。

下游仓库据此定版：跟 stable 用 `VERSION` 的值；跟 beta 必须写实际的 `-beta.<N>` 全称，
不能写 `VERSION` 的值加猜测的后缀。

### ⚠️ nuget.org 上存在版本号虚高的历史预发布包

`1.0.0-beta.22`（2026-07-20）与 `1.0.0-preview.20260908`–`20260918` 共 10 个包，
按 SemVer 排序**高于**当前在用的 `0.13.0-beta.*`。后果是
`dotnet add package Leistd.Core --prerelease` 会解析到那批旧包，而不是最新 beta。

**在这批包被 unlist 之前，预发布依赖必须写死全称**，例如
`<PackageReference Include="Leistd.Core" Version="0.13.0-beta.170" />`，不要用浮动或 `--prerelease`。

成因（两条，都已确认）：

1. `1.0.0-beta.22` 早于「0.x 期间破坏性变更按 Minor 递增」这条规则（2026-09-06 引入）。
2. `1.0.0-preview.*` 是**规则引入之后**仍在产出的：GitHub 的 `schedule` 事件
   **始终执行默认分支的 workflow 文件**，而该规则目前只在 `develop` 的 `release.yml` 里，
   `main` 还没有。nightly 因此检出 develop 的源码，却跑着 main 的版本推算逻辑。
   push 到 `develop` 触发的 beta 通道跑的是 develop 自己的文件，所以 beta 一直是正确的 `0.13.0-beta.*`。

修复需要两步，都不能顺带做：`release.yml` 的当前版本合入 `main`（合入 main 本身即触发正式发版），
以及在 nuget.org 上 unlist 那 10 个包（NuGet 包不可删，只能 unlist，且是对公共源的操作）。

- `framework/common.props` 在构建时自动读取 `VERSION` 作为 `VersionPrefix`，所有 `Leistd.*` 包同步该版本（无外部工具依赖）。
- 模板 `template/backend/Directory.Build.props` 的 `<LeistdFrameworkVersion>` 是字面值副本（生成项目需自包含），由发布流水线 `release.yml` 在发版时回写。

## 提交规范决定版本递增（Conventional Commits）

发版时流水线（`release.yml`）分析"自上个 `v*` tag 以来"的提交信息，算出下一个版本（默认"优先最小版本"）：

| 提交 | 递增 | 例 |
| --- | --- | --- |
| 普通提交 / `fix:` | Patch（默认） | `fix: 修复锁超时` → 0.8.0 → 0.8.1 |
| `feat:` / `feat(scope):` | Minor | `feat: 新增 Redis 锁` → 0.8.x → 0.9.0 |
| `feat!:` / 任意类型带 `!` / 含 `BREAKING CHANGE` | Major | `0.12.0` → **`0.13.0`**（见下方 0.x 规则）；`1.4.2` → `2.0.0` |

> **提交信息务必遵循 [Conventional Commits](https://www.conventionalcommits.org/)** —— 它直接决定版本如何递增。

### 0.x 期间的破坏性变更按 Minor 递增

依据 [SemVer 第 4 条](https://semver.org/lang/zh-CN/#spec-item-4)：`0.y.z` 是初始开发期，
公共 API 不承诺稳定。因此当前主版本为 `0` 时，`!` / `BREAKING CHANGE` 递增 **Minor** 而不是 Major。

这条规则解决的是：破坏性变更在 0.x 阶段是常态，若照搬"带 `!` 就进 Major"，
**第一条不兼容改动就会把版本推到 `1.0.0`** —— 而 1.0 意味着 API 稳定承诺，
那是一次产品决定，不该由某条提交顺带触发。

破坏性变更**不会被隐藏**：release notes 仍按 `!` / `BREAKING CHANGE` 归入「破坏性变更」小节，
升级 0.x 小版本时必须照常阅读。

进入 `1.0.0` 需要显式抬 `VERSION`，见下方「本地手动操作」。

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

## 破坏性变更怎么让下游知道

**发版日志与升级清单是唯一的对外交付物**，和生态里的通行做法一致（ABP 同样是
release notes + migration guides，没有提交式的 API 基线）：

- release notes 由 `release.yml` 按 Conventional Commits 自动归类，`!` 与
  `BREAKING CHANGE` 脚注进「破坏性变更」小节；
- 需要写明"原来怎么写、现在怎么写"的，另出一份升级清单，见文末列表。

因此**提交信息必须如实**：脚注漏写就等于下游拿不到那一条。0.13.0 把分页契约从
`Leistd.Ddd.Application.Contracts` 挪到新包 `Leistd.Data` 并改了类型名，这一条当初就是
这么漏掉的（见 [0.13.0 升级清单](upgrade-0.13.0.md) 第 3 节）。

曾经为此引入过一套自制的公共表面快照（`framework/api-baseline/`，66 个文件 2302 行），
后来撤掉了：它把"公共成员清单"当成了兼容性真值，而 XML 文档 ID 连 static↔实例、常量值、
基类变化都看不见；同时它与发版日志职责重叠，两套并存只是重复维护。译文键与占位符的比对
并入了 `scripts/check-i18n-keys.ps1`（它本就在做同类事，现已改为自动枚举全部随包资源）。

### 什么时候写升级清单

不是每版都写。**只有调用方必须动手改代码时才写**——否则 release notes 已经够了。
一份升级清单要能回答四件事：受影响的是什么、原来怎么写、现在怎么写、怎么迁。

纳入发版评审的变化面（这些即使不改公共 API 也会让下游动手）：数据库迁移、配置键、
路由、授权策略、随包译文键、模板消费方式。Framework、随包文档与 Template 同步交付，
不允许某一面先行。

### Package Validation：当前不是既定任务

.NET SDK 内置的 **Package Validation** 能对上一个已发布的包做二进制兼容性比对
（基类与接口、泛型约束、参数默认值、static↔实例、访问器可见性、常量值都在内），
有意的破坏性变更落进 `CompatibilitySuppressions.xml` 供评审。

**现在不接，也不作为任何版本的发布前置条件。** 理由是当前的治理方式够用：
0.x 期间破坏性变更是常态，消费方是已知的几个内部仓库，Conventional Commits +
release notes + 按需升级清单能把变化送到；再叠一套机械闸门属于为不存在的风险付维护成本。

满足下面任一条时再评估引入：

- 框架进入 `1.0.0`——那意味着对外承诺 API 稳定，"我以为没破坏"不再是可接受的答案；
- 消费方变得不可控（公开发布、外部团队接入），脚注漏写的代价不再由自己承担；
- **升级说明重复漏记**——这是最实在的触发条件，一次是意外，反复发生说明靠人不行了。

真要接入时：基线取**一个已发布的版本**（不能取当前开发版），在 `framework/common.props`
打开 `EnablePackageValidation` 并设 `PackageValidationBaselineVersion`，首次 `dotnet pack`
带 `/p:GenerateCompatibilitySuppressionFile=true` 生成抑制文件，逐条评审后提交。
注意改过名或新增的包没有基线，需逐包例外。

已发布版本的升级清单：

- [从 0.12.0 升级到 0.13.0](upgrade-0.13.0.md)

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
# 打包（用 VERSION 文件的版本，固定产出到本地 feed，产出前先清空）
pwsh framework/build/pack-local-feed.ps1

# 想发布更高的基准版本：直接编辑 VERSION 文件即可（CI 在此基础上按提交递增）
```

> `.tmp/local-feed` 仅用于本地开发并由 `.gitignore` 忽略；CI 发布继续使用 `framework/artifacts`。版本推算、模板同步、打包发布逻辑全部内联于 `.github/workflows/release.yml`，发布由 push 自动触发，本地通常无需手动介入。

## 注意

- CPM 下第三方包版本集中在 `framework/Directory.Packages.props`；升级第三方依赖按提交规范评估影响。
- monorepo 统一版本：所有可发布框架包共享同一版本，要么全发要么全不发；`--skip-duplicate` 保证重跑幂等。
- 整个机制零外部版本工具（纯 git + PowerShell + MSBuild 读文件），与团队其它项目（如 ai-relay）的 VERSION 文件范式一致。
