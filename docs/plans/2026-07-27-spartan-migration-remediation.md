# Spartan 迁移收敛计划（遗留清理 · 规范化 · 交付面纯净）

> 承接 [`2026-07-24-primeng-to-spartan-migration.md`](2026-07-24-primeng-to-spartan-migration.md) 的落地结果，对迁移提交（`46a5555` 阶段0、`c019aac` 阶段1-5）做诊断后的收敛。架构决策见 [`../architecture/frontend-ui-library.md`](../architecture/frontend-ui-library.md)（D1–D4 本轮不动）。前端规范见 [`../../template/docs/standards/coding-frontend.md`](../../template/docs/standards/coding-frontend.md)，Spartan 用法以 `.agents/skills/spartan/` 与 `@spartan-ng/mcp` 为准。

## 背景

模板（`template/`）是对外通用产品。迁移功能上已完成，但诊断发现仍残留：过程/历史痕迹、死代码死依赖、命名与语义色不合规、主题控件冗余、以及两处随裁剪组合才暴露的隐性 bug。本计划把迁移收敛到"对外可交付"的终局。

## 一、目标

| # | 目标 | 验收标准 |
| --- | --- | --- |
| G1 | 交付面纯净 | template 内无 PrimeNG 字样/迁移过程痕迹、无死代码/死依赖 |
| G2 | 规范一致 | 命名合 Angular 2025 风格 + Spartan；样式只用语义 token；`readonly` 一致 |
| G3 | UI 收敛 | 主题控件唯一（三态切换），移除冗余面板；确认框 a11y（role/aria）到位 |
| G4 | 规范沉淀 | 维护入口（dev-guide / skill / MCP）正确指向 Spartan 方案 |
| G5 | 面向未来 | 为业务项目迁移预置组件替代策略，不提前引死依赖 |
| G6 | 质量门禁 | 条件矩阵生成 + lint + build（+ 运行时）全绿；D1–D4 不动 |

非目标：不重开迁移既定决策；不改后端；不为完整感批量造文档。

## 二、影响点

| 影响面 | 内容 | 条件依赖 | 风险 |
| --- | --- | --- | --- |
| 前端组件 | 去 `Component`/`Page` 后缀；`delete`→`deleteRequested`；补 `readonly`；`model_`→`formModel` | Identity/OpenIddict | 中 |
| 主题 UI | 删 `ThemeConfigurator` 面板 + popover；抽 `ThemeModeToggle`；4 页统一 | Localization/Notifications | 中 |
| 样式 | `styles.css` 加 `--warning` 语义 token + `@theme inline`；删失效注释；替换 `orange-*` | — | 低 |
| i18n | 删死 `primeng` 词条、死 `theme.config`（含 primary/surface/presets 换色残留）；landing 文案去 PrimeNG；加 `theme.toggle`；首帧预加载活动语言消除 ~15 处首帧缺翻译告警；`{totalRecords}` 单括号 → `{{total}}` 插值修复 | Localization | 低 |
| 依赖/锁 | 两 `package.json` + 两 lock 去 `chart.js`、`@tanstack/angular-table` | Localization(overlay) | 低 |
| 文件重命名 | `role-label.pipe.ts`、`filter-state.service.ts` → 去点后缀 | Roles/Identity | 低 |
| a11y | `ConfirmDialog` role=alertdialog + aria；`confirm` 默认文案 i18n 化 | Localization | 低 |
| Skills/文档 | `development-guide §4` 回链 Spartan | — | 低 |
| 隐性 bug | ① `default-header` 在 Notifications 开启时重复 lucide import（全功能 lint 挂）；② 三态循环按钮图标在 auth 页未注册（空白） | Notifications | 中 |
| no-identity 回归 | 迁移 `c019aac` 引入：`startup-service` `//#else` 导入被清空、`auth-service` `//#else` 用 `User` 未导入、`http-error-interceptor` 的 `inject` 变为条件性使用却无条件导入 → `--include-identity false` 编译/lint 失败。S7 发现并修复 | Identity/Localization | 中 |
| sidebar 品牌头 | 迁移未按官方模式：品牌头用自绘 `<div class="px-2 …">` + 固定 32px logo，嵌在已有 `p-2` 的 `hlmSidebarHeader` 内。折叠为 icon（rail=`--sidebar-width-icon` 3rem/48px）时双层内边距把 32px logo 挤到右缘、`overflow-hidden` 裁掉右侧 → logo 偏移+裁剪。改用官方 `hlmSidebarMenuButton size=lg` 基元（折叠自动 `size-8! p-0!` 居中），logo 居中不裁。用户测试发现 | 全条件 | 中 |
| 下游 | 生成项目继承以上；`dotnet new` 全新脚手架，无存量迁移负担 | — | 低 |

面向未来（G5，不在本轮改 template）：表格 = Spartan `data-table` recipe + TanStack（需要时再引）；图表 = Spartan 无，用 ngx-echarts / Chart.js + Spartan CSS 变量配色；tree/treetable/stepper/timeline/rating/file-upload/galleria = Spartan 无，须第三方。

## 三、策略

1. 事实优先 + 官方对齐：改前端先查 `spartan` skill / MCP / 官方 docs，不臆造 Helm/Brain API。
2. 最小充分：只收敛遗留，不动架构与后端；每处改动可追溯到一条诊断。
3. 分组降风险、可独立回退：纯清理 → 规范修正 → 命名/重构 → 主题收敛 → 文档 → a11y。
4. 条件矩阵为门禁：改动横跨 `//#if`，必须 full（全开）与 default（裁剪）及关键裁剪组合 lint+build 全绿。
5. 过程留痕不入交付面：诊断/计划留在 `docs/plans`、git、PR；template 内零过程信息。
6. 未来能力写指南、不提前引依赖（chart.js 死依赖的教训）。

## 四、步骤与状态

| 阶段 | 内容 | 门禁 | 状态 |
| --- | --- | --- | --- |
| S0 | 诊断：8 项审视 + 命名/主题/组件替代三路审计 | — | 完成 |
| S1 | 纯清理：死常量、死依赖、PrimeNG 注释/文案/i18n、失效注释、palette 图标 | 生成+lint+build | 完成 |
| S2 | 规范修正：`--warning` 语义 token 替换 `orange-*` | 构建产物含 warning 工具类 | 完成 |
| S3 | 命名一致：去 Component/Page 后缀、readonly、model_、delete、旧后缀文件 | full+default 绿 | 完成 |
| S4 | 文档回链：development-guide §4 指向 Spartan | — | 完成 |
| S5 | 主题收敛：删冗余面板、抽 `ThemeModeToggle`、清 theme.config、修 2 个隐性 bug | full+default 绿 | 完成 |
| S6 | a11y：`ConfirmDialog` role=alertdialog + aria；`confirm` 默认文案 i18n 化 | full+default 绿 | 完成 |
| S7 | 全量矩阵：官方 `test-template-matrix.ps1 -SkipRuntime`，10 场景 Backend build+test / Frontend install+lint+format+build 全 pass；发现并修复 no-identity 回归、theme.toggle 首帧告警、theme-mode-toggle 掉 providers | 矩阵全绿 | 完成 |
| S8 | i18n 收敛：`provideAppInitializer` 首帧前预加载活动语言（消除 ~15 处首帧缺翻译告警）；修复 `currentPageReport` 的 `{totalRecords}` 单括号未插值（PrimeNG 分页器遗留）→ `{{total}}` + 传参 | full+default 绿 + 浏览器验证 | 完成 |
| S9（原 G5 沉淀顺延） | （可选）业务项目组件替代指南写入 `docs/` | — | 待定 |
| S9 | 提交：Conventional Commit / PR 走 CI | CI 绿 | 待办 |

## 五、验证

- 门禁命令：`pwsh scripts/test-template-matrix.ps1`（矩阵）；单场景 `dotnet new fullstack-app` → `npm ci` → `npm run lint` → `npm run build`。
- 已验证（官方 `test-template-matrix.ps1 -SkipRuntime`，10 场景全 pass）：default、minimal、no-roles、notifications、no-openiddict、external-login、localization、no-localization、localization-notifications、localization-external-login —— 每场景 Backend build + IntegrationTests + Frontend install/lint/format/build 全 pass。构建产物确认 `--warning` 工具类生成（light/dark 双值）；残留扫描（primeng / orange / lucidePalette / model_ / ThemeConfigurator / theme.config）归零。
- 浏览器功能走查（full-feature mock）：主题三态切换 + `.dark`/localStorage/token、登录、用户表、编辑对话框、sonner toast（aria-live）、删除确认框（已验证 `role=alertdialog` + aria-labelledby/describedby）全部通过。
- S7 发现并修复 3 类问题：① 迁移引入的 no-identity 回归（startup/auth/interceptor 导入，见影响点表）；② 我引入的 `theme.toggle` 首帧缺翻译告警（改用 `| transloco` 管道，异步安全）；③ 我引入的 `theme-mode-toggle` 掉 `providers: [provideIcons(...)]`（会致图标不渲染 + lint 失败，已补回为无条件项）。
- 运行时烟测（Runtime）：官方矩阵中 `skipped`。原因是该步 pwsh `Invoke-WebRequest` 探测 `127.0.0.1:$port/api/health` 在本机稳定超时，但直接 `curl` 生成 API 的 `/api/health` 2s 内返回 200 Healthy —— 确认为 harness/环境探测问题，非本批（纯前端）改动、非后端缺陷。

## 六、提交建议

单 commit：`refactor(frontend): 收敛 Spartan 迁移遗留（清理死代码/主题冗余/命名规范/a11y）`；或拆 `清理 / 命名 / 主题 / a11y / 文档` 多 commit。lockfile 变更随各自 package.json。
