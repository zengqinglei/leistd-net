---
name: developing-leistd-template
description: 在 leistd-net 仓库中为 template 的后端、前端、条件参数、生成项目文档、项目 Skill、Mock 或部署资产设计方案、制定实施计划、修改或审查时使用，也用于适配 Leistd 公共 API 并验证生成场景。不能替代框架组件开发；生成后的业务功能开发应使用项目内 Skill。
---

# 开发 Fullstack App Template

## 建立事实

从目标文件和直接依赖建立事实，先参考同类实现和测试；关键约束再核对相关规范章节。涉及框架用法时核对实际消费包及随包文档，框架源码或契约变化时再读对应源码。生成后的业务开发使用项目内 Skill。

| 场景 | 补充事实与规范 | 验证重点 |
| --- | --- | --- |
| 后端或前端骨架 | 同类实现、测试与 `template/docs/standards/` 对应章节；Spartan 组件用专项 Skill | 受影响场景生成、构建、测试；交互变化做浏览器验证 |
| 参数、条件、文件裁剪 | `template.json`、相邻条件块、`docs/template/development-guide.md` | 按质量规范区分局部源条件与全局生成输入，验证实际产物 |
| 架构边界、权限、租户、数据库 | 调用链、Migrator、`docs/architecture/design-principles.md` 及相关后端规范 | 真实 PostgreSQL 与受影响生成场景 |
| Dockerfile、Compose、部署入口 | 实际容器配置、`template/docs/deploy/README.md` | 镜像可构建、.NET 运行时层可用；实际启动另验 |
| 项目 Skill 或规范 | 生成项目入口、官方 `skill-creator`、`template/docs/README.md` | Skill 校验和代表性生成结果 |

模板通过 `PackageReference` 消费框架；`Api` 是组合根，Application 不依赖 Infrastructure。模板源可编辑不等于交付完成，必须验证实际生成结果。

模板源码不产随包 XML，非公开成员可按需使用 XML 注释。注释只保留职责和非显然契约，XML 不使用 Markdown 强调。

## 方案与实施计划

涉及 Template 技术选型、结构调整、参数迁移或跨场景改造时，先搜索同主题最新文档；评估、计划与稳定规范的归属和生命周期见 [`docs/README.md`](../../../docs/README.md)。即使计划只影响 Template，也只写在根 `docs/`。

## 对外分发边界

`template/` 是 `dotnet new` 对外发布载荷，只包含生成项目运行和持续开发真正需要的内容：当前源码、配置、测试、Mock、部署资产、工程规范和项目级 Skill。哪些仓库信息不得进入分发载荷见 [`docs/README.md` 分发边界](../../../docs/README.md#分发边界)。

`template/.agents/skills/leistd-project-workflow/` 只描述生成后业务项目自身的分析、实现、审查、测试和部署协作，不承载 leistd-net 的 Framework/Template 维护流程。

## 工作流

1. 检查分支、工作区和用户已有改动，列出受影响参数、条件组合、替换项和排除文件。
2. 需要某种基础能力（锁、缓存、事件、本地化、审计等）时，先读 `framework/docs/components/` 下对应组件的文档，
   按它给出的入口接入；不要只看实现源码就自行判断，更不要在模板里另建平行抽象。
   组件文档写明的部署前提（例如分布式锁要求多副本部署配置 Redis）属于契约，不是待修的缺陷；
   确有缺口就反馈到框架组件，由那一层收口。
3. 沿用最新同类模板实现，修改源码、测试和确有长期价值的文档。
4. 框架包内容变化或无法确认现有包对应当前源码时，打包到 `.tmp/local-feed`；否则复用已验证的包，经 NuGet 路径消费。
5. 在 `.tmp/` 下生成受影响场景，检查占位符、条件裁剪与格式；只改文案时不重复运行无关测试。
6. 代码或配置变化时，对生成项目执行相关还原、构建与测试。
7. 权限、审计、通知、实时或数据库变化时补充对应业务闭环验证。

用户要求提交时只暂存本任务文件，核对生成场景与验证结果；提交格式按 `docs/framework/versioning.md` 的相关章节执行。仓库版本交付由 `maintaining-leistd-repository` 负责；Template 维护完成不等于生成项目已部署。

维护规则归 `docs/template/`，项目协作流程归 `template/.agents/skills/leistd-project-workflow/`，生成项目的长期事实由 `template/docs/README.md` 索引；三者分工见 `docs/architecture/collaboration-scenarios.md` §2。改 `template/docs/` 时同步索引的按任务读取表，并按 `docs/template/development-guide.md` §4 控制核心规范篇幅。只沉淀已验证、需复用的信息。

## 验证入口

按改动选择的入口、本地场景集合与真实依赖检查见[质量规范](../../../docs/framework/quality-assurance.md#分层执行与时间预算)的 L1 表；阶段完成验证实际生成产品，矩阵失败用对应场景复现。不在每次编辑后跑全集，也不凭示例手选一个场景认证全部变化；全集由合入后的 CI 承担。未执行的场景、档位和风险必须说明。
