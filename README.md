# leistd-net

> .NET 10 + Angular 21 全栈 DDD 脚手架：一套可独立版本化、NuGet 发布的 **Leistd 框架**，加一个 `dotnet new` **项目模板**。

[![CI](https://github.com/zengqinglei/leistd-net/actions/workflows/ci.yml/badge.svg)](https://github.com/zengqinglei/leistd-net/actions/workflows/ci.yml)
[![Release Stable](https://github.com/zengqinglei/leistd-net/actions/workflows/release-stable.yml/badge.svg)](https://github.com/zengqinglei/leistd-net/actions/workflows/release-stable.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

leistd-net 把"可复用的框架能力"与"业务项目脚手架"分离：

- **`framework/`** —— Leistd 框架基座（26 个 `Leistd.*` 包：AOP、DI、事件总线、异常、分布式锁、对象映射、统一响应、安全、链路追踪、工作单元 + DDD 四层基础类型）。统一版本、中央包管理（CPM）、Source Link 源码调试，以 NuGet 包发布。
- **`template/`** —— 基于 `dotnet new` 的全栈项目模板（.NET 10 后端 + Angular 21 前端，DDD 四层，支持条件裁剪认证/权限）。生成的项目通过 NuGet 引用 framework。

参考 [Volo.ABP](https://abp.io) 的工程化模式构建。

---

## 仓库结构

```
leistd-net/
├── framework/          # Leistd 框架（NuGet 化，独立版本）
│   ├── components/     #   共享组件（11 个分组）
│   ├── ddd-struct/     #   DDD 四层基础类型
│   ├── docs/           #   📖 框架文档（从这里开始：docs/README.md）
│   └── build/          #   pack / push 脚本
├── template/           # dotnet new 项目模板
│   ├── backend/        #   .NET 10 + DDD 后端
│   └── frontend/       #   Angular 21 前端
├── scripts/            # 版本同步、框架引用切换等辅助脚本
├── GitVersion.yml      # 版本号推算规则（唯一版本来源）
└── .github/workflows/  # CI 与多通道发布流水线
```

---

## 快速开始

### 用模板创建项目

```bash
# 安装模板（本地）
dotnet new install ./template

# 生成项目（命名空间将替换为 Acme.Shop.*）
dotnet new fullstack-app -n Acme.Shop
```

生成的后端默认通过 NuGet 引用 `Leistd.*` 框架包。详见 [模板文档](template/README.md)。

### 本地构建框架

```bash
dotnet build framework/Leistd.Framework.slnx -c Release
pwsh framework/build/pack.ps1     # 打包到 framework/artifacts/
```

---

## 文档

| 我想…… | 看这里 |
| --- | --- |
| 用框架的某个能力（锁 / 事件 / 响应 / 追踪……） | [框架文档首页](framework/docs/README.md) → [组件总览](framework/docs/components/README.md) |
| 用模板生成 / 配置项目 | [模板文档](template/README.md) |
| 给框架新增或修改组件 | [开发规范](framework/docs/development-guide.md) |
| 了解版本与发布机制 | [版本与发布](framework/docs/versioning.md) |
| 在业务项目里切换"框架源码调试" | [框架文档首页](framework/docs/README.md#在项目中使用框架) |

---

## 版本与发布

版本号由 [GitVersion](https://gitversion.net)（配置见 [`GitVersion.yml`](GitVersion.yml)）从 git 历史按 **Conventional Commits** 自动推算——`fix:`→patch、`feat:`→minor、`BREAKING CHANGE`→major（默认 patch）。

| 分支 / 触发 | 版本形态 | 发布目标 |
| --- | --- | --- |
| tag `v*.*.*` | `x.y.z`（正式） | nuget.org |
| 推送 `develop` | `x.y.z-beta.N` | nuget.org（预发布） |
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
