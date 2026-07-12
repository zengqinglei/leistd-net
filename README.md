# leistd-net

> **为 AI 时代设计的 .NET 10 + Angular 21 全栈 DDD 基座**：框架让 AI 按真实 API 写代码，Skills 按场景提供执行知识，模板验证可运行的端到端组合。

[![Release](https://github.com/zengqinglei/leistd-net/actions/workflows/release.yml/badge.svg)](https://github.com/zengqinglei/leistd-net/actions/workflows/release.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

## 为什么是 leistd-net

传统脚手架解决"代码怎么起步"，但在 AI 参与开发的当下，真正的瓶颈变成了两件事：**AI 会不会用错框架 API（凭记忆臆造）**，和 **AI 交付的东西可不可信、可不可追溯**。leistd-net 把这两件事作为一等目标来设计：

- 🤖 **AI 按真实 API 编码，不臆造**——每个 `Leistd.*` 包内置随版本分发的用法文档，配套 Skill 引导 AI「先查随包文档 / 包内 XML，再写代码」，并用 CI 校验文档与源码不漂移。AI 用的是**你安装的那个版本的真实 API**，不是训练记忆里的猜测。
- 🔄 **AI 按任务风险完成闭环**——模板携带一个跨工具项目 Skill，按需加载方案、实现、审查、测试、协调和部署知识；简单改动直接实施，高风险动作由人确认。
- 🧩 **文档按价值自主沉淀**——AI 先读源码、配置和最新同类文档，只把需要跨会话、跨成员或长期复用的信息写入权威位置，不生成例行报告和占位模板。

技术上，它把"可复用框架能力"与"业务项目脚手架"清晰分离：

- **`framework/`** —— Leistd 框架基座（33 个 `Leistd.*` 包 / 15 个能力分组 + DDD 四层基础类型）：AOP、DI、事件总线、异常、分布式锁、对象映射、统一响应、安全、链路追踪、工作单元、通知、实时、审计、授权。统一版本、中央包管理（CPM）、Source Link 源码调试，以 NuGet 发布，**每个包内置随版本文档**。
- **`template/`** —— 基于 `dotnet new` 的全栈项目模板（.NET 10 后端 + Angular 21 前端，DDD 四层，可条件裁剪认证/权限）。生成的项目通过 NuGet 引用 framework，并自带跨工具项目 Skill 与工程规范。

---

## AI 协作开发（核心特性）

仓库维护与模板生成项目分别使用自己的项目级 Skill，并从所属交付面的源码和文档建立事实。

### 维护本仓库

仓库内部 Skill 位于 [`.agents/skills/`](.agents/skills/)，[Codex](https://developers.openai.com/codex/skills) 等原生发现该目录的 AI CLI 无需安装。使用只识别其他项目目录的 CLI 时，按需生成本地适配；例如项目 Skill 位于 `.claude/skills/` 的 [Claude Code](https://code.claude.com/docs/en/skills#where-skills-live) 执行：

```bash
npx skills add ./.agents/skills --agent claude-code --skill developing-leistd-framework developing-leistd-template maintaining-leistd-repository -y
```

`npx skills` 会根据平台能力选择链接或复制，并在结果中标明实际方式。需要强制复制时执行：

```bash
npx skills add ./.agents/skills --agent claude-code --skill developing-leistd-framework developing-leistd-template maintaining-leistd-repository --copy -y
```

复制的适配不会自动跟随权威源更新，修改 `.agents/skills/` 后应重新执行命令。生成的 `.claude/skills/` 和 `skills-lock.json` 是本地适配产物，不提交到仓库。

### 生成项目

模板生成的项目使用一个 Skill 路由最终意图，再按需加载场景知识：

| 机制 | 作用 |
| --- | --- |
| **项目 Skill** | `leistd-project-workflow` 覆盖规划、实现、审查、测试、协调和部署，并保持最终交付责任 |
| **项目事实源** | `docs/README.md` 是文档索引；源码、配置、测试和 CI 定义实际行为 |
| **事实优先** | 先读源码、配置、测试、框架随包文档和最新同类项目文档，不从训练记忆或固定模板猜测 |
| **按需沉淀** | Git、PR、CI 和当前答复承载一次性证据；长期共享的规则、需求、模块或运维事实才进入 `docs/` |
| **人工确认红线** | 生产部署 / 删数据 / 改密钥等高风险动作必须人类显式确认 |

> 总体原则见 [`docs/architecture/design-principles.md`](docs/architecture/design-principles.md)，三层使用场景和逐步文档链路见 [`docs/architecture/collaboration-scenarios.md`](docs/architecture/collaboration-scenarios.md)。

---

## 仓库结构

```
leistd-net/
├── .agents/skills/     # 仓库内部维护 Skill
├── framework/          # Leistd 框架（NuGet 化，独立版本）
│   ├── components/     #   共享组件（15 个能力分组，33 个包）
│   ├── ddd-struct/     #   DDD 四层基础类型（4 个包）
│   ├── docs/           #   面向使用者的组件文档（随包分发）
│   └── build/          #   pack / push / 文档-源码漂移校验 脚本
├── template/           # dotnet new 项目模板
│   ├── backend/        #   .NET 10 + DDD 后端
│   ├── frontend/       #   Angular 21 前端
│   ├── .agents/skills/ #   跨工具项目 Skill
│   └── docs/           #   项目规范及按需沉淀的长期文档
├── scripts/            # 本地开发与 CI 共用的仓库级验证脚本
├── skills/             # 面向 Leistd.* 使用者的可分发 Skill
├── docs/               # 仓库架构、框架/模板内部维护规范与变更记录
├── VERSION             # 版本基准（唯一版本来源，x.y.z）
└── .github/workflows/  # CI 与多通道发布流水线
```

---

## 快速开始

### 给 AI 装上框架知识（框架级 Skill）

让你的编码 Agent（Claude Code / Cursor / Codex 等）学会按 `Leistd.*` 真实 API 编码、不臆造——安装框架级 Skill `leistd-net-framework`：

```bash
# 跨代理安装（GitHub 即注册表，基于 Agent Skills 开放标准）
npx skills add https://github.com/zengqinglei/leistd-net/tree/main/skills/leistd-net-framework --global
```

安装后对 AI 说「用 leistd-net-framework skill」即可。它是**索引 Skill**：指向随每个 NuGet 包分发的版本正确文档，AI 用的始终是你安装的那个版本的真实 API。

> 模板生成项目已包含 `leistd-project-workflow`，无需重复安装。其他项目可从 `template/.agents/skills/leistd-project-workflow` 目录进行项目级安装；详见 [`skills/README.md`](skills/README.md)。

### 用模板创建项目

```bash
# 安装模板（本地）
dotnet new install ./template

# 生成项目（命名空间将替换为 Acme.Shop.*）
dotnet new fullstack-app -n Acme.Shop
```

生成的后端默认通过 NuGet 引用 `Leistd.*` 框架包，并自带按场景触发的 AI 协作能力。可用参数以 `dotnet new fullstack-app --help` 为准：

| 参数 | 默认值 | 能力 |
| --- | --- | --- |
| `--include-identity` | `true` | 本地账号、登录、注册与 Cookie 认证 |
| `--include-roles` | `true` | 角色与权限，仅在 Identity 启用时可用 |
| `--include-notifications` | `false` | 通知中心与 SignalR 实时消息，仅在 Identity 启用时可用 |
| `--include-openiddict` | `true` | OAuth 2.0/OIDC Server，仅在 Identity 启用时可用 |
| `--include-external-login` | `false` | GitHub/Google 等外部登录，仅在 Identity 启用时可用 |

例如，生成无认证的最小项目：

```bash
dotnet new fullstack-app -n Acme.Service --include-identity false
```

### 本地构建框架

```bash
# 构建
dotnet build framework/Leistd.Framework.slnx -c Release

# 本地包固定输出到 .tmp/local-feed（dotnet CLI 三平台命令一致）
dotnet pack framework/Leistd.Framework.slnx -c Release -o .tmp/local-feed
```

---

## 文档

| 我想…… | 看这里 |
| --- | --- |
| 给 AI 装上框架知识（安装 / 了解框架级 Skill） | [可分发 Skill 说明](skills/README.md) |
| 用框架的某个能力（锁 / 事件 / 响应 / 追踪……） | [框架文档首页](framework/docs/README.md) → [组件总览](framework/docs/components/README.md) |
| 用模板生成 / 配置项目 | [用模板创建项目](#用模板创建项目) |
| 了解三层架构与 AI 协作场景 | [三层交付与 AI 协作](docs/architecture/collaboration-scenarios.md) |
| 在业务项目中使用 AI 协作 | [项目协作 Skill](template/.agents/skills/leistd-project-workflow/SKILL.md) 与 [项目文档入口](template/docs/README.md) |
| 给框架新增或修改组件 | [开发规范](docs/framework/development-guide.md) |
| 修改模板、条件裁剪或生成项目工作流 | [模板开发规范](docs/template/development-guide.md) |
| 了解版本与发布机制 | [版本与发布](docs/framework/versioning.md) |
| 联调本地框架与模板 | [模板联调说明](docs/template/development-guide.md#5-本地框架联调) |

---

## 版本与发布

版本基准存于仓库根 [`VERSION`](VERSION) 文件；发布流水线（[`release.yml`](.github/workflows/release.yml)）按 **Conventional Commits** 推算递增——`fix:`→patch、`feat:`→minor、`BREAKING CHANGE`→major（默认 patch）。零外部版本工具，纯 git + PowerShell，逻辑内联于 workflow。

| 分支 / 触发 | 版本形态 | 发布目标 |
| --- | --- | --- |
| push `main` | `x.y.z`（正式，自动递增） | nuget.org |
| push `develop` | `x.y.z-beta.N` | nuget.org（预发布） |
| 每工作日定时 | `x.y.z-preview.<date>` | GitHub Packages（内部） |

提交请遵循 [Conventional Commits](https://www.conventionalcommits.org/)（它直接决定版本递增）。完整发布流程见 [版本与发布](docs/framework/versioning.md)。

---

## 技术栈

| 层 | 技术 |
| --- | --- |
| 后端 | .NET 10 · ASP.NET Core · EF Core · OpenIddict |
| 前端 | Angular 21 · PrimeNG · Tailwind CSS |
| 数据 | PostgreSQL 15+ / 内存（开发）· Redis 7+（可选） |
| 部署 | Docker · Docker Compose |

---

## 许可

[MIT](LICENSE) © zengql
