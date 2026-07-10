# leistd-net

> **为 AI 时代设计的 .NET 10 + Angular 21 全栈 DDD 基座**：让人和 AI 在同一套规范下协作开发——框架让 AI 按真实 API 写代码而非臆造，模板让 AI 端到端交付需求且全程可追溯。

[![Release](https://github.com/zengqinglei/leistd-net/actions/workflows/release.yml/badge.svg)](https://github.com/zengqinglei/leistd-net/actions/workflows/release.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

## 为什么是 leistd-net

传统脚手架解决"代码怎么起步"，但在 AI 参与开发的当下，真正的瓶颈变成了两件事：**AI 会不会用错框架 API（凭记忆臆造）**，和 **AI 交付的东西可不可信、可不可追溯**。leistd-net 把这两件事作为一等目标来设计：

- 🤖 **AI 按真实 API 编码，不臆造**——每个 `Leistd.*` 包内置随版本分发的用法文档，配套 Skill 引导 AI「先查随包文档 / 包内 XML，再写代码」，并用 CI 校验文档与源码不漂移。AI 用的是**你安装的那个版本的真实 API**，不是训练记忆里的猜测。
- 🔄 **AI 端到端交付，全程留痕**——模板携带一套「想法 → 方案 → 开发 → 审查 → 测试 → 部署 → 验收」的 AI 协作工作流（6 个阶段 Skill + 规范文档），每一步产出可追溯的证据文档，人在关键节点确认，AI 负责执行。
- 🧩 **人机共用同一套规范**——分层约定、编码规范、API 契约、文档分类既是给人看的规则，也是 AI 读取后据以行动的指令。规范即代码的"护栏"。

技术上，它把"可复用框架能力"与"业务项目脚手架"清晰分离：

- **`framework/`** —— Leistd 框架基座（33 个 `Leistd.*` 包 / 15 个能力分组 + DDD 四层基础类型）：AOP、DI、事件总线、异常、分布式锁、对象映射、统一响应、安全、链路追踪、工作单元、通知、实时、审计、授权。统一版本、中央包管理（CPM）、Source Link 源码调试，以 NuGet 发布，**每个包内置随版本文档**。
- **`template/`** —— 基于 `dotnet new` 的全栈项目模板（.NET 10 后端 + Angular 21 前端，DDD 四层，可条件裁剪认证/权限）。生成的项目通过 NuGet 引用 framework，并**自带 AI 协作工作流骨架**（`.claude/skills/` + `docs/standards/`）。

参考 [Volo.ABP](https://abp.io) 的工程化模式构建。

---

## AI 协作开发（核心特性）

模板生成的项目开箱即带一套面向 AI Agent 的开发闭环，把「想法 → 方案 → 开发 → 审查 → 测试 → 部署 → 验收」沉淀为可追溯文档：

| 机制 | 作用 |
| --- | --- |
| **阶段 Skill**（6 个） | requirement-plan · coding · code-review · test-runner · deploy · task-manager——覆盖需求到验收全流程，各司其职、职责不重叠 |
| **工作流事实源** | `docs/standards/agent-workflow.md` 定义 8 阶段状态机、阶段门禁、交接包与高风险人工确认点 |
| **上下文驱动选择** | Skill 不互相硬调用，由模型按上下文 + 用户意图 + Skill description 自主选择能力 |
| **证据闭环** | 需求 Plan、任务上下文、各阶段报告统一沉淀 `docs/`，需求可从 plan 追溯到 acceptance |
| **降级不阻塞** | 缺项目级规范时 Skill 用内置默认继续执行并提示补齐，不卡死 |
| **人工确认红线** | 生产部署 / 删数据 / 改密钥等高风险动作必须人类显式确认 |

> 设计原则（AI 优先、单一事实源、文档即索引、可追溯闭环）沉淀于 [`docs/assessments/2026-07-10-design-principles.md`](docs/assessments/2026-07-10-design-principles.md)。

---

## 仓库结构

```
leistd-net/
├── framework/          # Leistd 框架（NuGet 化，独立版本）
│   ├── components/     #   共享组件（15 个能力分组，33 个包）
│   ├── ddd-struct/     #   DDD 四层基础类型（4 个包）
│   ├── docs/           #   📖 框架文档（随包分发；从 docs/README.md 开始）
│   └── build/          #   pack / push / 文档-源码漂移校验 脚本
├── template/           # dotnet new 项目模板
│   ├── backend/        #   .NET 10 + DDD 后端
│   ├── frontend/       #   Angular 21 前端
│   ├── .claude/skills/ #   🤖 AI 协作工作流骨架（6 个阶段 Skill，随项目分发）
│   └── docs/           #   AI 协作、需求、规范、报告与部署文档
├── scripts/            # 版本同步、框架引用切换等辅助脚本
├── VERSION             # 版本基准（唯一版本来源，x.y.z）
└── .github/workflows/  # CI 与多通道发布流水线
```

---

## 快速开始

### 给 AI 装上框架知识（框架级 Skill）

让你的编码 Agent（Claude Code / Cursor / Codex 等）学会按 `Leistd.*` 真实 API 编码、不臆造——安装框架级 Skill `leistd-net-framework`：

```bash
# 跨代理安装（GitHub 即注册表，基于 Agent Skills 开放标准）
npx skills add zengqinglei/leistd-net
```

安装后对 AI 说「用 leistd-net-framework skill」即可。它是**索引 Skill**：指向随每个 NuGet 包分发的版本正确文档，AI 用的始终是你安装的那个版本的真实 API。

> 只有**框架级 Skill 需要这样安装**。模板生成项目所带的 6 个阶段 Skill 随 `dotnet new` 一起落到项目 `.claude/skills/`，无需单独安装。详见 [`skills/README.md`](skills/README.md)。

### 用模板创建项目

```bash
# 安装模板（本地）
dotnet new install ./template

# 生成项目（命名空间将替换为 Acme.Shop.*）
dotnet new fullstack-app -n Acme.Shop
```

生成的后端默认通过 NuGet 引用 `Leistd.*` 框架包，并自带 AI 协作工作流骨架。详见 [模板文档](template/README.md)。

### 本地构建框架

```bash
# 构建
dotnet build framework/Leistd.Framework.slnx -c Release

# 打包到 framework/artifacts/（dotnet CLI 三平台命令一致）
dotnet pack framework/Leistd.Framework.slnx -c Release -o framework/artifacts
```

---

## 文档

| 我想…… | 看这里 |
| --- | --- |
| 给 AI 装上框架知识（安装 / 了解框架级 Skill） | [可分发 Skill 说明](skills/README.md) |
| 用框架的某个能力（锁 / 事件 / 响应 / 追踪……） | [框架文档首页](framework/docs/README.md) → [组件总览](framework/docs/components/README.md) |
| 用模板生成 / 配置项目 | [模板文档](template/README.md) |
| 在业务项目中使用 AI 协作开发闭环 | [AI Native 开发模式](template/docs/quick-start/ai-native-model.md) · [Agent 工作流规范](template/docs/standards/agent-workflow.md) |
| 给框架新增或修改组件 | [开发规范](framework/docs/development-guide.md) |
| 了解版本与发布机制 | [版本与发布](framework/docs/versioning.md) |
| 在业务项目里切换"框架源码调试" | [框架文档首页](framework/docs/README.md#在项目中使用框架) |

---

## 版本与发布

版本基准存于仓库根 [`VERSION`](VERSION) 文件；发布流水线（[`release.yml`](.github/workflows/release.yml)）按 **Conventional Commits** 推算递增——`fix:`→patch、`feat:`→minor、`BREAKING CHANGE`→major（默认 patch）。零外部版本工具，纯 git + PowerShell，逻辑内联于 workflow。

| 分支 / 触发 | 版本形态 | 发布目标 |
| --- | --- | --- |
| push `main` | `x.y.z`（正式，自动递增） | nuget.org |
| push `develop` | `x.y.z-beta.N` | nuget.org（预发布） |
| 每工作日定时 | `x.y.z-preview.<date>` | GitHub Packages（内部） |

提交请遵循 [Conventional Commits](https://www.conventionalcommits.org/)（它直接决定版本递增）。完整发布流程见 [版本与发布](framework/docs/versioning.md)。

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
