# Leistd 框架版本与发布规范

## 版本来源（VERSION 文件）

框架版本的**唯一来源**是仓库根的 `VERSION` 文件（`x.y.z`），构建和发布都以它为基准。

### `VERSION` 是"上一次已发布的正式版"，不是"下一版"

- `VERSION` 等于最近一次 stable 已上传的版本，由 `release.yml` 上传后回写；包源索引与 Release 验收可能稍后完成。
- 下一版由流水线按提交推算，不提前写入 `VERSION`；`develop` 也保留这个稳定版本基准，不随 beta 递增。
- 下游跟 stable 使用 `VERSION`；跟 beta 从包源或流水线确认实际 `-beta.<N>` 全称，不猜测后缀。

### ⚠️ nuget.org 上存在版本号虚高的历史预发布包

`1.0.0-beta.22`（2026-07-20）与 `1.0.0-preview.20260908`–`20260918` 共 10 个包，
按 SemVer 排序**高于**当前在用的 `0.13.0-beta.*`。后果是
`dotnet add package Leistd.Core --prerelease` 会解析到那批旧包，而不是最新 beta。

**在这批包被 unlist 之前，预发布依赖必须写死全称**，例如
`<PackageReference Include="Leistd.Core" Version="0.13.0-beta.170" />`，不要用浮动或 `--prerelease`。

定时任务执行默认分支的 workflow；当前默认分支与 develop 的版本规则不同，nightly 因而仍可能产出虚高版本。修正需将 `release.yml` 合入 `main` 并在 nuget.org unlist 旧包；合入 main 会触发正式发布，unlist 是公共源操作，均须单独授权。

- `framework/common.props` 在构建时自动读取 `VERSION` 作为 `VersionPrefix`，所有 `Leistd.*` 包同步该版本（无外部工具依赖）。
- 模板 `template/backend/Directory.Build.props` 的 `<LeistdFrameworkVersion>` 是字面值副本（生成项目需自包含），由发布流水线 `release.yml` 在发版时回写。

## 提交规范决定版本递增（Conventional Commits）

发版时流水线（`release.yml`）分析自上个稳定版 `vX.Y.Z` tag 以来的提交信息，算出下一个版本（默认优先最小版本）：

| 提交 | 递增 | 例 |
| --- | --- | --- |
| 普通提交 / `fix:` | Patch（默认） | `fix: 修复锁超时` → 0.8.0 → 0.8.1 |
| `feat:` / `feat(scope):` | Minor | `feat: 新增 Redis 锁` → 0.8.x → 0.9.0 |
| `feat!:` / 任意类型带 `!` / 含 `BREAKING CHANGE` | Major | `0.12.0` → **`0.13.0`**（见下方 0.x 规则）；`1.4.2` → `2.0.0` |

> **提交信息务必遵循 [Conventional Commits](https://www.conventionalcommits.org/)** —— 它直接决定版本如何递增。

### 标了 `!` 就必须有 `BREAKING CHANGE:` 脚注

带 `!` 的提交必须有非空 `BREAKING CHANGE:` 或 `BREAKING-CHANGE:` 脚注，与升级指南一致，供下游按脚注检索。

```
refactor!: 异常响应统一走 Problem Details 管道

BREAKING CHANGE: 失败响应的 message/details 改为标准字段 detail/errors，
组件异常映射由各组件的 AddXxx 自行登记。详见 docs/framework/upgrades/0.13.0.md#异常处理。
```

脚注写变化概要，并须指向升级指南 `docs/framework/upgrades/<版本>.md` 的具体小节。旧提交中的节号指向迁移前说明，已推历史不改写；新脚注指向 `upgrades/`。

`release.yml` 在推算版本时拦截带 `!` 却无非空脚注的提交并列出原因。脚注检查、升版推算与发布说明提取使用同一解析口径：`!` 看标题前缀，脚注须在行首，接受 `BREAKING CHANGE:` 与 `BREAKING-CHANGE:`。

脚注检查只覆盖固定锚点 `802b4dca` 之后的提交，避免检查引入前的已推历史；此前缺脚注的破坏性内容见 [0.13.0 升级指南](upgrades/0.13.0.md)。锚点不扩大豁免范围，仅在历史重构后不再是 HEAD 祖先时更新；找不到祖先会报错，不静默跳过。升版推算仍使用完整提交范围。

### 0.x 期间的破坏性变更按 Minor 递增

依据 [SemVer 第 4 条](https://semver.org/lang/zh-CN/#spec-item-4)：`0.y.z` 是初始开发期，
公共 API 不承诺稳定。因此当前主版本为 `0` 时，`!` / `BREAKING CHANGE` 递增 **Minor** 而不是 Major。

`1.0.0` 代表 API 稳定承诺，须显式抬 `VERSION`，不能由一条破坏性提交自动触发。0.x 的破坏性变化仍列入 release notes，升级小版本须阅读。

## 分支 → 包类型

| 分支 / 触发 | 版本形态 | 发布目标 | 工作流 |
| --- | --- | --- | --- |
| push `main` | `x.y.z`（正式，自动递增） | nuget.org | `release.yml`（stable 通道） |
| push `develop` | `x.y.z-beta.<N>` | nuget.org（预发布） | `release.yml`（beta 通道） |
| 每工作日定时（develop） | `x.y.z-preview.<yyyyMMdd>.<run_number>` | GitHub Packages（内部） | `release.yml`（nightly 通道） |

> 三个通道由**单个** `release.yml` 内部按 `github.ref` / `github.event_name` 自动判定。

> 预发布后缀用**点分数字**（`-beta.12`、`-preview.20260623.123`），保证 NuGet 数值排序正确。

## 正式版发布（全自动）

**push 到 `main` 即自动发布**，无需手动打 tag：

1. 对同一候选提交复用 CI 的静态、Framework、Template 矩阵和真实 PostgreSQL 验证；全部通过后按提交推算新正式版本；
2. 在本地回写 `VERSION`、同步模板，创建 `chore: 发布 vX.Y.Z [skip ci]` 提交和 tag；
3. 打包并隔离消费最终版本，随后经 Trusted Publishing 推 nuget.org；
4. 包源写入后立即推送 tag 和版本提交，记录已发布产物；
5. 推送成功后直接创建 GitHub Release（自动生成 release notes）。推送接口返回 201/202 即表示已接收，`dotnet nuget push` 据此以退出码判定；nuget.org 随后异步校验并建索引（官方说明通常 15 分钟内），校验失败由 nuget.org 邮件通知所有者，流水线不等待包可见。包内容与依赖在推送前已用同一批 `.nupkg` 隔离消费验证。

机制要点：
- 触发发版的变更：`VERSION`、**`framework/` 源码**（框架内非 docs 的 `.md` 除外）、或 **`framework/docs/` 组件文档**（文档随包分发，故文档更新也发一版送达）。
- **不**触发 stable 正式版：`docs/framework/`、`template/` 与仓库根的 `*.md`（内部开发规范、模板文档、仓库元文档），避免非交付内容改动误发。develop 分支仍按 beta 通道策略执行。
- 回写提交带 `[skip ci]` 且过滤 `github-actions[bot]`，避免死循环。
- ⚠️ NuGet 包不可删（只能 unlist）。框架源码每次有效变更都会产出一个正式版，请把控合入 main 的节奏。

### 部分发布恢复

失败时先核对目标包源中已发布和缺失的包、tag 指向、`VERSION` 回写提交及 GitHub Release。tag 记录已发布版本，不能因发布后发现的问题而删除后复用版本号。只从原候选产物补齐缺失包；候选或产物改变时使用新版本并说明旧版本状态。恢复后手工确认目标包源能还原全部精确版本，并补齐缺失的 Release。不要用 `--skip-duplicate` 掩盖不同产物。

## 破坏性变更怎么让下游知道

破坏性变化通过 release notes 与按需升级指南交付，采用 ABP 同类的发布说明与迁移指南形式：

- release notes 由 `release.yml` 按 Conventional Commits 自动归类，`!` 与
  `BREAKING CHANGE` 脚注进「破坏性变更」小节；
- 需要写明"原来怎么写、现在怎么写"的，另出一份升级指南，见下文。

因此**提交信息必须如实**：脚注漏写就等于下游拿不到那一条。

不维护提交式的公共表面快照：XML 文档 ID 看不见 static↔实例、常量值与基类变化，不能充当兼容性真值，
且与发版日志职责重叠。随包译文键与占位符的比对由 `scripts/check-i18n-keys.ps1` 负责。

### 什么时候写升级指南

不是每版都写。**只有调用方必须动手改代码时才写**——否则 release notes 已经够了。

纳入发版评审的变化面（这些即使不改公共 API 也会让下游动手）：数据库迁移、配置键、
路由、授权策略、随包译文键、模板消费方式。Framework、随包文档与 Template 同步交付，
不允许某一面先行。

### 升级指南的位置与格式

- **位置**：每个版本一份，`docs/framework/upgrades/<版本>.md`，版本取正式版号（`0.13.0`）。
  它留在仓库里，**不进 NuGet 包**：包内只有 README 与指向它的 `PackageReleaseNotes` 链接，
  包体积不随历史版本增长。随包的 `framework/docs/` 只描述当前版本契约，不写迁移步骤（分发边界见 `docs/README.md`）。
- **结构**：分"框架"与"模板（生成项目）"两部分。框架部分按组件家族分小节，消费方只读自己引用的家族；
  跨家族的变化（包改名、命名空间、注册入口约定）放在框架部分开头，所有人都读。
  逐条成员对比等大清单作为同目录附录（如 `0.13.0-api-diff.md`），由正文链接。
- **每条写四项**：受影响的是什么、原来怎么写、现在怎么写、必须执行的迁移动作；
  并标注不兼容类型——**二进制**（已编译的调用方须重新编译）、**源码**（须改代码才能编译）、
  **行为**（照常编译，运行结果变化）。

### 版本映射与发布链接

- **版本映射**：预发布版本按去掉预发布后缀的基础版本对应指南：`0.13.0-beta.N`、
  `0.13.0-preview.<日期>.<序号>` 都对应 `docs/framework/upgrades/0.13.0.md`。
- **包元数据**：`release.yml` 打包时按渠道设置 `PackageReleaseNotes`，消费方离线也能从已安装包的 `.nuspec` 读到：
  - 基础版本有升级指南时，stable、beta、nightly 一律指向该指南**在本次发版 tag 下**的固定地址
    （`https://github.com/<仓库>/blob/<tag>/docs/framework/upgrades/<版本>.md`）；
  - 没有指南时，stable 与 beta 指向本次 GitHub Release（`…/releases/tag/<tag>`）；
    nightly 只推送 tag、不建 Release，指向该 tag 的提交页（`…/commit/<tag>`）。
  - 不用分支链接：分支上的文件会随后续提交变化，已发布的包要对应发版时的说明。
- **Release 正文**：有升级指南时，生成的 Release Notes 追加「升级指南」一节，链接同样固定到本次 tag；没有指南时不加。
- **验证**：链接规则由 `python scripts/test-workflow-change-scope.py` 执行 `release.yml` 的实际步骤验证——
  三个渠道各覆盖有、无指南两种情况，并打出夹具包，检查 `.nuspec` 的 `releaseNotes` 固定到本次 tag。
  改动这几步时运行它。本地 `framework/build/pack-local-feed.ps1` 不设置该属性。

### Package Validation：当前不是既定任务

.NET SDK 内置的 **Package Validation** 能对上一个已发布的包做二进制兼容性比对
（基类与接口、泛型约束、参数默认值、static↔实例、访问器可见性、常量值都在内），
有意的破坏性变更落进 `CompatibilitySuppressions.xml` 供评审。

当前不启用，也不作为发布前置条件：0.x 破坏性变化频繁、消费方为已知内部仓库，现有提交脚注、release notes 与升级指南足够，暂不增加兼容性闸门。

以下任一情况出现时重新评估：进入 `1.0.0` 承诺 API 稳定；外部消费方不可控；升级指南反复漏记。

接入时以已发布版本为基线，在 `framework/common.props` 设置 `EnablePackageValidation` 与 `PackageValidationBaselineVersion`；首次 pack 用 `/p:GenerateCompatibilitySuppressionFile=true` 生成抑制文件，逐条评审后提交。改名或新增包无基线，须逐包处理。

升级指南都在 [`upgrades/`](upgrades/) 目录，按版本号命名：

- [从 0.12.0 升级到 0.13.0](upgrades/0.13.0.md)

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
- monorepo 统一版本：所有可发布框架包共享同一版本。推包途中失败可能留下部分已发布的包；同版本不可覆盖，恢复前须核对实际包集合和 tag，不用 `--skip-duplicate` 掩盖不同候选产物。
- 版本机制仅使用 git、PowerShell 与 MSBuild，不依赖外部版本工具。
