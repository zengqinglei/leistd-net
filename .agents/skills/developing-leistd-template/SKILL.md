---
name: developing-leistd-template
description: 在 leistd-net 仓库中为 template 的后端、前端、条件参数、生成项目文档、项目 Skill、Mock 或部署资产设计方案、制定实施计划、修改或审查时使用，也用于适配 Leistd 公共 API 并验证生成场景。不能替代框架组件开发；生成后的业务功能开发应使用项目内 Skill。
---

# 开发 Fullstack App Template

## 建立事实

先读取：

1. `docs/architecture/design-principles.md` 和 `docs/template/development-guide.md`。
2. `template/.template.config/template.json`、受影响模板源码和邻近条件块。
3. 涉及框架时读取当前源码、随包文档和 `.tmp/local-feed` 中实际包。
4. 修改生成项目 Skill 时同时使用官方 `skill-creator`。

模板通过 `PackageReference` 消费框架；`Api` 是组合根，Application 不依赖 Infrastructure。模板源可编辑不等于交付完成，必须验证实际生成结果。

模板源码不产随包 XML，非公开成员可按需使用 XML 注释。注释只保留职责和非显然契约，XML 不使用 Markdown 强调。

## 方案与实施计划

涉及 Template 技术选型、结构调整、参数迁移或跨场景改造时，先读取 `docs/README.md` 并搜索同主题最新文档：

- 仍在比较候选或诊断现状时，跨会话材料写入 `docs/assessments/YYYY-MM-DD-<topic>.md`，**选定方案后删除**；
- 方案已经选定且需要任务分解、实施顺序和验收时，写入 `docs/plans/YYYY-MM-DD-<topic>.md`，**任务全部完成后删除**；
- 长期模板维护规则才写入 `docs/template/`；
- 当前实现已经成立后，生成项目必须知道的工程事实才写入 `template/docs/`。

临时分析默认留在当前答复。即使计划只影响 Template，也不得将方案比较、迁移计划、任务状态、分支记录或仓库验证过程写入 `template/`。

## 对外分发边界

`template/` 是 `dotnet new` 对外发布载荷，只包含生成项目运行和持续开发真正需要的内容：当前源码、配置、测试、Mock、部署资产、工程规范和项目级 Skill。以下 leistd-net 仓库信息不得进入 `template/`：

- Framework 或 Template 的候选方案、选型过程和实施计划；
- 仓库分支、MR、提交、任务状态、阶段报告和历史结论；
- `.tmp/local-feed`、模板矩阵等仅供 leistd-net 维护者使用的命令和路径；
- 尚未由当前模板源码和验证证明的未来技术栈或迁移说明。

`template/.agents/skills/leistd-project-workflow/` 只描述生成后业务项目自身的分析、实现、审查、测试和部署协作，不承载 leistd-net 的 Framework/Template 维护流程。仓库过程统一留在根 `.agents/skills/` 与 `docs/`。

## 工作流

1. 检查分支、工作区和用户已有改动，列出受影响参数、条件组合、替换项和排除文件。
2. 需要某种基础能力（锁、缓存、事件、本地化、审计等）时，先读 `framework/docs/components/` 下对应组件的文档，
   按它给出的入口接入；不要只看实现源码就自行判断，更不要在模板里另建平行抽象。
   组件文档写明的部署前提（例如分布式锁要求多副本部署配置 Redis）属于契约，不是待修的缺陷；
   确有缺口就反馈到框架组件，由那一层收口。
3. 沿用最新同类模板实现，修改源码、测试和确有长期价值的文档。
4. 涉及框架契约时先打包到 `.tmp/local-feed`，再走 NuGet 消费路径。
5. 在 `.tmp/` 下生成受影响场景，检查占位符、条件裁剪与格式；只改文案时不重复运行无关测试。
6. 代码或配置变化时，对生成项目执行相关还原、构建与测试。
7. 权限、审计、通知、实时或数据库变化时补充对应业务闭环验证。

维护规则归 `docs/template/`，项目协作流程归 `template/.agents/skills/leistd-project-workflow/`，生成项目的长期事实由 `template/docs/README.md` 索引。只沉淀已验证、需复用的信息。

## 验证入口

```powershell
pwsh scripts/check-all.ps1                                 # 全部静态闸门（唯一清单来源）
pwsh scripts/test-template-matrix.ps1
```

参数裁剪或共享资产变化使用完整矩阵。未执行的条件场景和风险必须说明。
