# Leistd 可分发 AI Skills

本目录存放**对外发布**的 AI agent skills，供消费 `Leistd.*` 框架包的下游项目安装。基于 [Agent Skills 开放标准](https://agentskills.io/specification)（SKILL.md），可用于 Claude Code / Cursor / Codex 等支持该标准的编码代理。

> 与 `.claude/skills/`（本仓库自身开发用的内部 skill）区分：本目录的 skill 是给**下游消费者**的。

## 可用 skill

| skill | 作用 | 受众 |
| --- | --- | --- |
| `leistd-net-framework` | Leistd .NET 框架（通用组件 + DDD 基座）组件用法索引，指向随包分发的权威文档 | 消费 `Leistd.*` NuGet 包的下游项目 |

## 安装

### 通过 npx skills（跨代理，GitHub 即注册表）

```bash
npx skills add zengqinglei/leistd-net
```

安装后对代理说："用 leistd-net-framework skill" 即可让其按框架真实 API 编码。

### .NET 原生（随包，版本锁定）

框架组件文档已随对应 `Leistd.*` NuGet 包分发到包内 `docs/` 目录（默认路径 `~/.nuget/packages/<包名>/<版本>/docs/`，实际缓存根以 `dotnet nuget locals global-packages --list` 为准，见 `leistd-net-framework/SKILL.md` 的定位配方）。若使用 `nuget-skills` 等 .NET 原生 skill 工具，可从已安装包版本对应的文档加载，天然与安装版本对齐、防漂移。

## 版本对齐

`leistd-net-framework` 是**索引 skill**，指向随包分发的文档；权威用法内容以你实际安装的 `Leistd.*` 包版本内的 `docs/*.md` 为准，而非本 skill 正文。这样即使 skill 与包版本不同步，AI 也会被引导去读版本正确的随包文档。
