# Leistd 框架 Skill

本目录存放供 `Leistd.*` NuGet 消费项目安装的框架 Skill。它遵循 [Agent Skills 开放标准](https://agentskills.io/specification)，可用于支持该标准的编码代理。

> 与根 `.agents/skills/`（本仓库自身开发用的内部 Skill）区分：本目录的 Skill 面向**下游消费者**。

## 能力

| Skill | 作用 | 受众 |
| --- | --- | --- |
| `leistd-net-framework` | Leistd .NET 框架（通用组件 + DDD 基座）组件用法索引，指向随包分发的权威文档 | 消费 `Leistd.*` NuGet 包的下游项目 |

## 安装

```bash
npx skills add https://github.com/zengqinglei/leistd-net/tree/main/skills/leistd-net-framework --global
```

必须直接指定 Skill 目录；从仓库根安装会发现模板内的项目 Skill，不能保证选择框架 Skill。description 仅在消费、配置或排查 `Leistd.*` 时触发，不应介入 Java、Python 或非 Leistd 项目；支持 Skill 发现的代理仍会加载少量元数据。

## 版本事实

`leistd-net-framework` 只定义消费流程。权威 API 以项目实际安装的 `Leistd.*` 包中 `docs/*.md` 和 XML 为准，缓存根通过 `dotnet nuget locals global-packages --list` 获取。

模板生成项目另带项目级 [`leistd-project-workflow`](../template/.agents/skills/leistd-project-workflow/SKILL.md)。它管理项目交付闭环，不替代框架 API Skill。需要在其他项目中使用时，在该项目目录执行：

```bash
npx skills add https://github.com/zengqinglei/leistd-net/tree/main/template/.agents/skills/leistd-project-workflow
```

不加 `--global`，避免对无关项目增加 Skill 元数据和触发范围。
