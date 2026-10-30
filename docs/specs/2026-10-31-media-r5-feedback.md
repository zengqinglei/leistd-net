# 表单提示与指针测试隔离反馈

状态：已完成 · 风险档：中

## 1. 需求

来源：只读反馈 `/Users/quincy/orca/workspaces/.crm-v2-adaptation/media-r5-20261030/framework-feedback.md`；参考 abroker CRM 提交 `cb323903`。用户于 2026-10-31 已确认 F1、F2 均采纳。基线为 `origin/develop`（`ee7a7402`），在新分支实施并自验，不提交、不推送；下游三仓及反馈目录保持只读，回执按本次任务指定位置 `.tmp/media-r5/result.md` 交付。

| 项 | 落位与决定 | 理由 |
| --- | --- | --- |
| F1 | 模板表单规范：同字段无效时只渲染错误（含服务端字段错误），有效时渲染说明；用 `@if` 销毁说明并注销 `aria-describedby`，不用 CSS 隐藏。错误独立可读；带单位数值用自定义 `kind` 写完整单位、范围与可操作要求，通用 `min`/`max` 只用于无单位且无需说明的字段。 | 避免重复提示及错误依赖说明才能理解。只改规范，不改模板用例或 Helm。Spartan brain 1.8 的 `BrnFieldA11yService` 错误登记为私有，没有公开“当前是否显示错误”；在 Field 层判断需复刻 `hlm-field-error` 显示逻辑并兼顾 `forceShow` 服务端错误。模板无数值范围校验；设置页状态与来源已在同一描述位二选一；验证码说明与服务端错误语义不重复，增加公共机制收益不足。 |
| F2 | 模板 Vitest 配置按源码调用 `setCoarsePointer(` 分细指针与触屏实例，保留 Angular 精确 include、实例身份与选项，细指针先运行；非触屏实例调用仿真立即失败；真正依赖细指针的用例独立并检查真实媒体查询。 | CDP 仿真属于顶层页面；Vitest `isolate` 只隔离 iframe，不能隔离输入设备状态。分实例使用官方浏览器机制；失败守卫防止 Angular 内部项目形状变化造成静默失效。 |

已核实的上游实测（Claude 探针，非本次重新执行）：Playwright 1.64.1 与无头 Chromium 在 macOS 上开启再关闭触屏可恢复 `(pointer: fine)` 与 `(hover: hover)`；Linux `node:24-bookworm-slim` 上关闭后为 `pointer: none`、`hover: none`，导航新文档仍不恢复。`setEmulatedMedia` 的 pointer/hover、`setEmitTouchEventsForMouse(false)` 与再次关闭均无效，只有新页面或浏览器上下文恢复。探针路径由任务书提供；macOS 的成功不能替代 Linux 证据。

模板基线中 16 个 spec 使用触屏仿真，5 个 spec 使用 `userEvent.hover`；Tailwind v4 的 `hover:` 依赖 `(hover: hover)`。旧助手关闭时只检查 coarse 已关，无法发现 pointer:none。公开 API 使用 `vitest/browser` 的 `server.config.name`（[Context API](https://vitest.dev/api/browser/context#server)）；实例 `include` 与 `sequence.groupOrder` 使用 [browser.instances](https://vitest.dev/config/browser/instances)、[sequence](https://vitest.dev/config/sequence#sequence-grouporder)，并核对锁定版本类型和 Angular 构建器配置阶段源码。

验收标准：

- F1：Given 同字段说明与校验提示，When 阅读表单规范，Then 明确互斥渲染、可访问性注销、服务端错误和带单位完整范围规则（文档人工验收与读取成本闸门）。
- F2：Given Angular 精确发现清单及多个原始实例，When 分组，Then 每个原实例内各 spec 恰运行一次，不扩大 include、不产生空组且保留选项，细指针组先行（Node 配置单测）。
- F2：Given 插件未分组或细指针实例，When 调用 `setCoarsePointer`，Then 在发送 CDP 前确定性抛错（生成工作区回归及临时变异验证）。
- F2：Given 真实细指针实例，When 执行依赖设备的用例，Then `requireFinePointer()` 同时断言 fine 与 hover，不替换 matchMedia、不放宽断言（真实 Chromium 单测）。
- 整体：Given 同步生成场景，When 构建、全量前端单测、lint/format 与相关静态检查，Then 全部通过；Linux 原始故障前提本机不可复现，交 PR Ubuntu CI 确认。

方案采用配置阶段插件就近分组，配置函数用 Node 内置 `node:test` 覆盖；模板当前没有前端 Node 测试入口，使用现有 Node 24 的 TypeScript 加载与内置测试器，无新增依赖，并接入 `npm test`。逐个检查全部触屏及悬停 spec，只有真正依赖媒体条件的用例拆分；JS 指针事件用例保持原位。规范仅保留必要规则与代码入口；完整指针约定放在测试助手及配置注释。同篇只删重复示例或提示，不提高读取成本上限。

## 4. 结果

F1、F2 已完成，按用户要求保留在 `zengqinglei/media-r5-feedback-20261031` 的未提交工作区，等待 Claude 审查；无 PR、提交或推送。完整实施回执与逐命令证据在 `.tmp/media-r5/result.md`。

表单规范已补互斥渲染和独立可读的单位／范围错误；未改 Helm 或表单代码。测试配置已分离细指针与触屏实例，保留发现范围与实例选项；助手通过公开实例名在发送 CDP 前拒绝错误实例调用，细指针前提同时检查 pointer 与 hover。原细指针控件尺寸、导航鼠标尺寸两条断言迁至独立 spec；其余悬停断言经检查属于 JS 事件，不依赖媒体条件，保留原位。配置测试使用 Node 内置机制并接入两份 package.json 的 `npm test`，无新增依赖。

验收结论：生成工作区的全量前端单测、配置单测、生产构建、lint/format、配置类型检查与相关静态闸门通过；本地化开关、Resource 前端与纯 API 形态生成通过。实际矩阵前端阶段确认 Node 配置测试前置输出兼容 spec 发现和浏览器测试计数，因此保留现有 `npm test` 入口；本地定向计划仅驱动该阶段，不作为 PR 选测或 CI 放行证明。修复前回归已失败，临时禁用分组后守卫确定性失败，真实 fine 实例中的开启／关闭拒绝探针通过，临时改动均已撤销。常驻文档同步到 `template/docs/standards/frontend-forms.md`、`template/docs/standards/testing.md`；完整指针写法在 `coarse-pointer.testing.ts` 与 `vitest-base.config.ts` 注释维护。保留原有必须级别和未涉及的段落，仅去除重复示例／提示以维持读取成本；README 沿用既有文档路由，与基线一致。

遗留边界：macOS 不能证明 Linux Chromium 原始恢复故障已消除，须在后续 PR 的 Ubuntu CI 确认；本次未创建 PR 或运行 CI。lint 中未改动的样式有既有排序警告；配置加载器的模块提示不影响执行，详见回执。未运行无关后端、真实部署 E2E、多场景完整构建测试与发布。
