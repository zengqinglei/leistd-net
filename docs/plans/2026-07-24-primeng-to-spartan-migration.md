# 前端组件库迁移实施计划：PrimeNG → Spartan UI

> 目标：把模板前端（`template/frontend`）的 UI 组件层从 **PrimeNG 21** 整体迁移到 **Spartan UI（spartan.ng v1.x）**。触发原因是 PrimeNG 自 v22 起转为 PrimeUI 商业双许可、MIT 版仓库于 2026-06-29 归档；本项目体量不满足 Community 免费资格，继续走 PrimeNG 意味着商业许可成本 + 供应商锁定。经四路官方文档调研与全项目用法盘点后确定迁移。
>
> **本文是可执行任务分解与决策记录，不是最终代码；每阶段附验收入口。** 相关一手调研事实见文末「附录：调研来源」。

---

## 一、决策与依据

### 1.1 为什么弃 PrimeNG

| 维度 | 事实 |
| --- | --- |
| 许可 | PrimeNG ≥22 转 PrimeUI 双许可；Community 免费需同时满足「年营收 <$1M、开发者 <5、员工 <10、融资 <$3M」，本项目所属组织不满足；Commercial $599/开发者（2027 起 $799），且商业包只发编译产物、不含源码、调试库代码被视为违约。 |
| 维护 | MIT 版 PrimeNG（≤21）永久 MIT 但仓库已归档，不再有 bug/安全修复；且 `primeng@21` peer 锁 Angular 21，把整个前端栈钉死在 Angular 21（支持窗口约 2026-11 到期）。 |
| 结论 | 留 v21 是买时间不是终态；升 v22 是引入商业依赖 + 锁定。两者都不接受，故换库。 |

### 1.2 为什么选 Spartan UI

在用户候选（Spartan / HyperUI / DaisyUI / Flowbite / Preline）与补充候选（Angular Material / ng-zorro / Taiga / PrimeNG 社区 fork）中，只有 Spartan 同时满足「真 Angular 组件库 + MIT + 支持 Angular 21/22 + zoneless-ready + Tailwind 4 一等公民」：

- **HyperUI/DaisyUI** 是静态 HTML 片段 / 纯 CSS 插件，无交互行为；**Flowbite** 的 Angular 封装锁 v21 且近乎停更；**Preline** 是 vanilla JS 插件且许可含 Fair Use 限制条款——均不合格。
- **社区 fork（Optimus UI）** 仅 313 stars、无发布、无企业赞助，按历史 fork 存活率不可依赖。
- **Material/ng-zorro/Taiga** 可用但均非 Tailwind-first，深色模式/主题需额外桥接；且仍是「换一个第三方 npm 依赖」，未解决锁定问题。

**Spartan 的决定性优势 = 架构免疫锁定**：helm 样式层通过 CLI **复制进本仓库**（`libs/ui/`），成为我们自有代码；即使 Spartan 上游停摆，已生成的组件仍在手、仍可改。这与 PrimeNG 事件的教训直接对冲。叠加：与现有 Tailwind 4 零摩擦、`.dark` class 深色模式逐字同构、命中 2025–2026 headless/shadcn 主流趋势（State of JS 2025 shadcn 使用率两年 20%→56%，Angular 官方亦在 v21 推出 `@angular/aria` headless 基座）。

**已知代价（已接受）**：Spartan 1.0 才发布一个月（2026-06-24），较年轻——以 helm 代码自持 + 锁定 brain/CLI 小版本 + 只走官方 `healthcheck` 升级来对冲。

### 1.3 四项前置决策（已拍板）

| # | 决策项 | 结论 |
| --- | --- | --- |
| **D1** | 主题能力 | **方案 A**：收敛为「亮/暗/系统」三态 + 单一品牌主题。删除 PrimeNG 的 4 preset × 17 主色 × 5 surface 运行时换色能力（Spartan 无对应 API）。模板只提供基线，业务项目按品牌改 CSS 变量。 |
| **D2** | 表单机制 | **直接终局，无中间态**：采用 **Angular Signal Forms**（`[formField]` 绑定，import 自 `@angular/forms/signals`）。不保留 `[(ngModel)]`/Reactive 过渡态。<br>⚠️ **已确认 Signal Forms 在 Angular 22 仍是 experimental**（官方明示 API 可能小版本间 breaking）。用户 2026-07-24 复核后仍选坚持。对冲：① 锁定 Angular 版本，升级后跑 `@spartan-ng/cli:healthcheck` + 手工回归表单；② 生成项目文档标注此实验性风险。Spartan Field 组件族表单策略无关（不 import `@angular/forms`，靠 `BrnField` 读校验态），UI 层与 Reactive 版一致，未来若回退仅换引擎、不动 UI。 |
| **D3** | 滚动条 | **终局主流做法**：采用 **Tailwind v4.3+ 原生滚动条工具类**（`scrollbar-thin` / `scrollbar-thumb-*` / `scrollbar-track-*`）。删除现有手写 `::-webkit-scrollbar` + `--app-scrollbar-*` 变量方案，也不引入第三方 `ngx-scrollbar` / Spartan scroll-area（后者是逐容器方案，非全局）。模板已在 Tailwind 4.3.3，官方 blog 明确推荐原生工具类替代插件与手写伪元素。 |
| **D4** | 决策记录落点 | 见 [§六 决策记录](#六决策记录d4)。 |

---

## 二、迁移范围总览

### 2.1 组件映射（26 类 PrimeNG → Spartan）

图例：🟢 直接对应 ｜ 🟡 形态变化需改写 ｜ 🔴 需自建/重设计

| PrimeNG（用量） | Spartan 对应 | 关键差异 | 难度 |
| --- | --- | --- | --- |
| p-button | `hlmBtn` | **无内置 loading**：改 `disabled` + `<hlm-spinner data-icon="inline-start"/>` | 🟡 |
| pTooltip | `hlmTooltip` | 全局默认走 `provideBrnTooltipDefaultOptions()` | 🟢 |
| p-dialog（7 处） | `HlmDialogService` 编程式 | signal `[visible]` → `open()`+`closed$`；`injectBrnDialogContext` 传值；`dialogRef.close(result)` 回传 | 🟡 |
| pInputText | `hlmInput` | 同构 | 🟢 |
| p-card | `HlmCardImports`（7 件套） | `pTemplate` → 语义子组件 | 🟢 |
| MessageService（32 处） | `toast()`（`@spartan-ng/brain/sonner`）+ `<hlm-toaster/>` | DI 服务 → 函数；仅用 3 severity、无 sticky/clear；`life` → `duration` | 🟡 |
| ConfirmationService（4 处） | `hlm-alert-dialog` + **自建 ConfirmService** | 官方无服务式 confirm，需薄封装 `BrnDialogService` | 🔴 |
| p-password（11 处） | `hlm-input-group` + 眼睛图标按钮 | 现状全 `feedback=false`，无强度条需求 | 🟡 |
| p-select（10 处） | `hlm-select`（`*hlmSelectPortal`） | `appendTo=body` → portal；组合式结构 | 🟡 |
| p-multiselect（3 处） | `HlmSelectMultiple` | 现状无 filter 需求，Select multiple 足够 | 🟡 |
| p-table（2 处） | `hlmTable` 指令 + **TanStack Table** | lazy 分页 → `manualPagination`+`rowCount`（官方未演示，spike 验证）；排序/操作列组件需自收进 `libs/ui` | 🔴 |
| p-tag | `hlmBadge`（6 变体） | 同构 | 🟢 |
| p-avatar | `hlm-avatar` + image/fallback | fallback 自动驱动 | 🟢 |
| p-popover（3 处） | `hlm-popover`（`*hlmPopoverPortal`） | `.toggle()` → trigger 指令 / `state` | 🟡 |
| p-menu popup（2 处） | `hlmDropdownMenuTrigger` + `ng-template` | **MenuItem[] model → 模板驱动** | 🟡 |
| pStyleClass（3 处） | signal + `tw-animate-css` | 用途单一（切配置器显隐），改 signal/动画或 popover | 🟡 |
| p-divider | `hlm-separator` | 同构 | 🟢 |
| iconfield/inputicon | `hlm-input-group` + addon | 同构增强 | 🟢 |
| p-fileupload（2 处） | `<input type=file>` + 现有自绘 UI | 现状是 customUpload + 全自绘模板，实际只需文件选择 | 🟡 |
| p-progressspinner | `hlm-spinner` | 尺寸改文字类 | 🟢 |
| p-badge/overlaybadge | `hlmBadge` + `hlm-avatar-badge` | 通知红点官方有内置角标 | 🟡 |
| p-drawer（1 处） | `hlm-sidebar` | <768px 自动 sheet；内置 `HlmSidebarService`、折叠、快捷键；结构按 `hlm-sidebar-wrapper` 重排 | 🟡 |
| p-toggleswitch | `hlm-switch` | CVA，同构 | 🟢 |
| pTextarea | `hlmTextarea` | 同构 | 🟢 |
| p-selectbutton | `hlm-toggle-group type="single"` | 同构；宿主 theme-configurator 按 D1 重设计 | 🟡 |

### 2.2 横切迁移面

- **图标**：约 37 个 primeicons → Lucide（`<ng-icon name="lucideXxx">` + 组件级 `provideIcons` 按需注册）；清除 TS 中所有 `pi pi-*` 字符串（`modeIcon`、`MenuItem.icon`、confirm `icon`、`http-status-severity` 等）。建完整映射表。
- **全局样式**（`styles.css`，见 §四）。
- **主题体系**：ThemeService 按 D1 大幅精简（删 preset/palette 逻辑约 -100 行）；theme-configurator 重设计为模式切换器。
- **表单**：全站按 D2 迁 Signal Forms。
- **模板双面**：基础版 + localization 变体两套 `package.json`/`package-lock.json` 同步；`//#if` 条件块在重写模板时逐一核对不丢失。

---

## 三、全局样式判决（styles.css，74 行）

| 现状 | 判决 | 依据 |
| --- | --- | --- |
| `@import 'primeicons/primeicons.css'` | **删** | ng-icons 为组件内 SVG，无全局 CSS |
| `@import 'tailwindcss'` | **保留** + 追加 `@import "@spartan-ng/brain/hlm-tailwind-preset.css"` | 官方安装要求 |
| `@plugin "tailwindcss-primeui"` | **删**（连 npm 依赖） | Spartan 变量经 preset `@theme inline` 自动成 Tailwind 类 |
| `@custom-variant dark (...)` | **删** | preset 已内置 `@custom-variant dark (&:is(.dark *))` |
| `html,body{height:100%}` | **保留** | 应用级布局 |
| `font-size: 14px` | **删** | 官方全库无 root font-size 声明，token 全 rem（隐含 16px）；保留会缩小并偏离官方视觉 |
| `color/background: var(--p-*)` | **删** | 由 theme 生成器 `@layer base{body{@apply bg-background text-foreground}}` 取代 |
| 自定义滚动条（`--app-scrollbar-*` + `*` 伪元素，36 行） | **删，改 Tailwind v4.3+ 原生工具类**（D3） | Tailwind 官方推荐；去掉手写伪元素与自定义变量 |
| （新增） | `ng g @spartan-ng/cli:ui-theme` 生成 `:root`/`:root.dark` oklch 变量集（约 30 个：--background/--primary/--sidebar-* 等） | 官方安装流程 |

**无障碍**：无需在全局样式保留任何东西——a11y 在 brain 层组件行为中（Field 自动 aria-describedby/role=alert、dialog aria-modal、menu 键盘导航），迁移后是**增强**。用 Spartan MCP 的 a11y 校验 tool + Lighthouse 抽查。

**index.html**：首帧防闪脚本（`.dark` class + `theme_config`）原样保留；preloader 的 `surface-*` 类改 `bg-background`/`border-border`/`border-t-primary`。

---

## 四、代码逻辑对齐 Spartan 最佳实践

| 现有模式 | 官方最佳实践 | 处置 |
| --- | --- | --- |
| Toast 走 DI `MessageService`（8 文件） | `toast()` 纯函数 | 收薄封装 `notify.success/error/warn()`（内部调 toast()，保可测性），8 文件改导入 |
| Confirm 走 DI + `<p-confirmDialog>` | 声明式 alert-dialog，无官方服务 | 自建 `ConfirmService`（包 `BrnDialogService` + 自有 confirm 组件），4 调用点签名基本不动 |
| 表单 `[(ngModel)]` | **Signal Forms `[formField]`**（D2） | 全站迁 Signal Forms，一步到位 |
| Dialog signal `[visible]`+`(visibleChange)` | `HlmDialogService.open()` + `closed$` | 7 个 dialog 统一编程式，消灭开关样板；breakpoints → `contentClass` 响应式类 |
| 菜单 `MenuItem[]` model | 模板驱动 `ng-template` | header 用户菜单/语言切换器展开为模板，权限项用 `@if` |
| 主题运行时换色 API | 无（D1 方案 A） | ThemeService 删 preset/palette；configurator → toggle-group 模式切换 |
| `pStyleClass` 动画 | 无对应 | signal + `tw-animate-css`（preset 内置）或 popover |
| 单测 | — | 现无 PrimeNG 相关单测；新增 confirm/notify 封装补测 |

---

## 五、自有基础设施层迁移

除页面组件外，`core/` 与 `shared/` 下有一批**我们自己写的**拦截器、异常处理、服务、共享组件、管道、常量。全项目逐文件盘点结论如下（这一层的迁移优先级高于页面——错误链断则整站提示失效）。

### 5.1 关键事实

- **自有模型/工具/DTO 层完全干净**：`shared/models`、`shared/utils`、DTO 无任何 PrimeNG 类型（`MenuItem`/`MessageService`/severity）泄漏。泄漏只发生在拦截器、异常处理、theme/language 服务、3 个共享组件。
- **错误提示三件套是一条必须同步改的链**：`http-error-interceptor` + `global-error-handler` + `app.config.ts` 的 `MessageService` provider + `app.ts` 的 `<p-toast/>`——四处联动，任一未改则错误提示断裂，**必须在页面迁移前作为一个原子改动完成**。
- **一处白捡简化**：`shared/pipes/http-status-severity.pipe.ts` 是 PrimeNG severity 专用，且 grep 确认**当前无任何消费者**——直接删除，零成本。

### 5.2 逐文件处置表

| 文件 | 耦合 | Spartan 是否有替代 | 迁移动作 |
| --- | --- | --- | --- |
| `core/interceptors/http-error-interceptor.ts` | 深 | toast() 替代 MessageService | **改写**：`messageService.add({severity:'error',summary,detail})` → `notify.error(summary,{description:detail})`（经 5.3 封装）；去 severity |
| `core/interceptors/accept-language-interceptor.ts` | 无 | — | 保留 |
| `core/interceptors/url-format-interceptor.ts` | 无 | — | 保留 |
| `core/interceptors/response-interceptor.ts` | 无 | — | 保留 |
| `core/interceptors/http-context-tokens.ts` | 无 | — | 保留 |
| `core/handlers/global-error-handler.ts` | 深 | toast() | **改写**（与拦截器同构，toast 化） |
| `core/services/theme-service.ts` | PrimeNG 专用 | 无运行时换色 API | **重设计**（`@primeuix/themes`→CSS 变量；dark class/持久化/系统监听保留；按 D1 删 preset/palette） |
| `core/services/language-service.ts` | 深 | Transloco 本身不依赖 UI 库 | **改写**：删 `primeng.setTranslation()` 联动（L77-84），语言状态与 Transloco 驱动保留 |
| `core/services/startup/auth/signalr/permission/native-fetch-service.ts` | 无 | — | 保留 |
| `core/services/notification-service.ts` | 浅（`pi pi-*` 字符串） | ng-icon | 保留逻辑，`getIcon` 图标名换 lucide |
| `core/i18n/transloco-loader.ts`、`translation-ready.ts`(+spec) | 无 | — | 保留（词条 JSON 内 `primeng` 段删除） |
| `core/guards/*`（auth/role/super-admin） | 无 | — | 保留 |
| `shared/components/dialog-loading/dialog-loading.ts` | 浅 | `hlm-spinner` | **改导入**（换 spinner，逻辑保留） |
| `shared/components/theme-configurator/theme-configurator.ts` | PrimeNG 专用 | toggle-group | **重设计**（随 theme-service；`p-selectbutton`+palette→模式切换器） |
| `shared/components/language-switcher/language-switcher.ts` | 深 | dropdown-menu | **改写**：`MenuItem`/`p-menu`/`p-button`/`@ViewChild Menu` → dropdown-menu 模板驱动 + `hlmBtn` |
| `shared/components/logo/*` | 无 | — | 保留 |
| `shared/pipes/http-status-severity.pipe.ts` | PrimeNG 专用，**无消费者** | — | **删除** |
| `shared/pipes/role-label.pipe.ts` | 无 | — | 保留 |
| `shared/constants/dialog-config.constants.ts` | PrimeNG 专用 | HlmDialogService `contentClass` | **重设计**：`breakpoints/style/draggable/resizable` → 响应式 `contentClass` 常量 |
| `shared/constants/tooltip.constants.ts` | 浅（Tailwind 类） | 值可复用 | **改注释/命名**：`TOOLTIP_STYLE` 值原样喂 Spartan tooltip 的 class |
| `shared/models/*`、`shared/utils/*`(+spec)、`shared/services/*` | 无 | — | 保留 |
| `app.config.ts`（合成根） | 深 | — | **改写**：删 `providePrimeNG`/`@primeuix/themes` Aura/`MessageService` provider；加 `provideSpartanHlm()` |
| `app.ts`（合成根） | 深 | — | **改写**：`<p-toast>`→`<hlm-toaster>`；启动页 card/spinner/button→Spartan；`pi pi-refresh`→lucide |

### 5.3 错误提示链的封装策略

现状 8 个文件、约 32 处 `messageService.add`。Spartan 的 `toast()` 是全局函数、无 DI。为保可测性与未来可替换性，**收一层薄封装 util `notify`**（非注入服务）：`notify.success/error/warn(summary, opts?)`，内部调 `toast.*`。基础设施两处（拦截器、handler）与页面各处统一改为调 `notify.*`，而非散落直接调 `toast()`。封装本身补单测。

---

## 六、命名规范纠正

迁移中顺带纠正「以 PrimeNG 术语命名、迁到 Spartan/sonner 后会误导或不一致」的符号（作为贯穿阶段 2–4 的规则，非独立阶段）：

| 现有命名 | 问题 | 纠正 |
| --- | --- | --- |
| `severity`（`http-status-severity.pipe`、拦截器/handler 的 `severity:'error'`） | sonner 无 severity 概念（用 `toast.error/success` 或 `type`） | 管道整体删除；字面量随封装消失 |
| `MessageService` 注入名（`messageService` / `this.messageService`） | Spartan 无此注入式服务 | 改为调 `notify.*` util，删注入 |
| `LangMenuItem extends MenuItem`（language-switcher） | `MenuItem` 是 PrimeNG 类型 | 自定义 item 类型（如 `LangOption`），不继承 PrimeNG |
| `pi pi-*` 图标类字符串（theme-service.modeIcon、notification-service.getIcon、language-switcher、app.ts） | PrimeIcons 术语 | 换 lucide 图标名（见阶段 2 图标映射表） |
| `TOOLTIP_STYLE`、`DIALOG_CONFIGS` 注释/字段名绑定 `pTooltip`/`p-dialog` | 语义绑 PrimeNG | 更新注释与字段，对齐 Spartan 机制 |

原则：新引入的变量、类型、常量一律采用 Spartan/主流约定命名（`hlm`/`brn` 前缀遵循库约定，自有符号避免任何 PrimeNG 术语残留）。每页迁移的 PR 自检包含「无 PrimeNG 术语残留」一项，与阶段 5 的 grep 门禁（`p-[a-z]+`/`pi pi-`/`primeng`/`--p-` 零命中）呼应。

---

## 七、实施阶段

> **验证策略（2026-07-24 调整）**：全矩阵（pack + 生成 + 后端 build + 前端 ci）单次约十余分钟，反馈环过长。改为**分层验证 + 全部改完统一测**：
> - **快速内环**（改造进行中）：维护一个持久生成项目（`.tmp/work`），改动同步过去跑 `tsc --noEmit` + `ng build` +（按需）`stylelint`，分钟级反馈，不重新生成、不碰后端。
> - **完整外环**（全部阶段改完后一次）：`test-template-matrix.ps1` default + localization 跑一次，含后端 build + 生成正确性 + 前端 ci + 浏览器实测，然后一次性提交。
> - 阶段 0 已单独提交（46a5555）；阶段 1–5 按此策略连续推进，中途不跑全矩阵。

### 阶段 0：地基（前置，±3 天）

- **0.1 Angular 22 先行升级**（独立提交）：Spartan 只支持最近两个 Angular 大版本，Angular 21 支持窗口 2026-11 截止——迁移中途出窗是最差情形，故必须先升。Node 22.22+/TS 6.0.x/`ng update @angular/core @angular/cli`；Transloco 7.6→8.x（localization 变体）；angular-eslint 22；ESLint 10 需确认 typescript-eslint/angular-eslint 支持后再升，否则停 9.x；**两套 package.json + lock 同步**。验收：两生成场景构建+测试+lint 全绿。
- **0.2 AI 工装**：仓库 `.mcp.json` 加 `@spartan-ng/mcp`（`npx -y @spartan-ng/mcp`）；`npx skills add spartan-ng/spartan`（落 `.claude/skills/spartan`）；`.prettierrc` 加 `"tailwindFunctions":["hlm","cva","classes"]`。评估 `leistd-project-workflow` skill 是否引用 spartan skill/MCP（下游项目开发同需）。

### 阶段 1：Spartan 基座进场（与 PrimeNG 共存，±2 天）

1. `npm i @spartan-ng/brain @angular/cdk tailwind-merge` + devDep `@spartan-ng/cli tw-animate-css`（两套 package.json）。
2. `ng g @spartan-ng/cli:init`：生成 `components.json`（`componentsPath: libs/ui`、`importAlias: @spartan-ng/helm`、style 6 选 1 **生成期锁定**，默认 `vega`）、写主题变量、tsconfig paths。
3. styles.css 按 §三 改造：加 preset import 与变量集；共存期**暂不删** primeicons/tailwindcss-primeui。⚠️ 共存期两套 `@custom-variant dark` 是否冲突为首验项。
4. appConfig 加 `provideSpartanHlm()`；`<hlm-toaster/>` 挂 app.ts（与 `<p-toast/>` 暂并存）。
5. **Spike（本阶段核心）**：在 `.tmp/` 生成项目里打通两个最难样张——① users 表格换 TanStack（`manualPagination`+`rowCount`+signal 状态）；② 一个编辑 dialog 换 `HlmDialogService`。通过才继续。
6. 模板工程适配：`template.json` 确认 `libs/ui/**` 进打包源；`//#if` 条件块落点核查。

**验收**：default + localization 两场景生成→构建→测试全绿；spike 页面浏览器实测（chrome-devtools MCP 截图佐证）。

### 阶段 2：横切基元 + 自有基础设施（±4 天）

本阶段先于任何页面迁移，把 §五 基础设施层和横切基元改完——错误链、主题、图标是所有页面的公共依赖。

1. **图标**：映射表（37 个 primeicons → lucide）+ `provideIcons` 按组件注册 + 清除全库 TS 中 `pi pi-*`（含 theme-service.modeIcon、notification-service.getIcon）。
2. **错误提示链（原子改动）**：`notify` 封装（toast() 薄壳，补单测）→ 同步改 `http-error-interceptor` + `global-error-handler` + `app.config.ts`（删 MessageService provider）+ `app.ts`（`<p-toast>`→`<hlm-toaster>`）→ 再替换 8 文件 32 处调用。去 `severity` 命名（§六）。
3. **ConfirmService**：自建（alert-dialog + `BrnDialogService`，补单测）→ 替换 4 调用 + 2 处 `<p-confirmDialog>`。
4. **主题**：ThemeService 重设计（D1 方案 A，删 preset/palette，保留 dark class/持久化/系统监听）+ theme-configurator 重设计（toggle-group 模式切换）+ language-service 删 `primeng.setTranslation` 联动 + index.html preloader 类替换。
5. **共享常量/组件**：`dialog-config.constants.ts` → `contentClass` 常量；`tooltip.constants.ts` 注释/命名更新；`dialog-loading` 换 spinner；**删除** `http-status-severity.pipe`（无消费者）。
6. **命名纠正**：`LangMenuItem extends MenuItem` → 自有 `LangOption` 类型等（§六），随各文件改动一并落。

### 阶段 3：布局骨架（±3 天）

1. default-layout + default-sidebar：`p-drawer` → `hlm-sidebar` 体系（wrapper/inset/menu/trigger，`HlmSidebarService` 取代自管状态；⚠️ 其 cookie 持久化与现有偏好存储一致性）。
2. default-header：avatar/badge/overlaybadge → `hlm-avatar`+`hlm-avatar-badge`；用户菜单 → dropdown-menu 模板；通知 popover → `hlm-popover`；pStyleClass → signal+动画。
3. language-switcher、theme-configurator 联调。

### 阶段 4：功能页迁移（±5 天，可并行）

按依赖顺序，每页一提交，每页表单同步迁 Signal Forms：

1. **app.ts 启动壳**（card/spinner/button/toast，首个完整样张）
2. **login / register**（input-group + password 眼睛按钮、card、styleclass→动画）
3. **account 三 dialog**（change-password / profile-settings / reset-password；fileupload→`<input type=file>`+自绘 UI）
4. **platform/users**（表格 TanStack 化、筛选 select、edit dialog、multiselect→Select multiple）
5. **platform/open-applications**（复制 users 模式）
6. **workspace/dashboard、landing**（card/tag/styleclass，量小）

### 阶段 5：清退与收口（±2 天）

1. 删依赖：`primeng`/`@primeuix/themes`/`primeicons`/`tailwindcss-primeui`（两套 package.json+lock）；styles.css 删 primeicons import、`@plugin`、自写 `@custom-variant`、`font-size:14px`（正式移除，全站视觉回归）、滚动条改原生工具类。
2. **grep 门禁**：`p-[a-z]+`/`pi pi-`/`primeng`/`--p-` 全库零命中（纳入 lint 或 CI 脚本）。
3. **文档同步**：`template/docs/standards/coding-frontend.md`（12 处）、`tech-stack.md` 等 4 文件；新增 Spartan 维护约定——helm 升级流程「升 brain/CLI → `ng g @spartan-ng/cli:healthcheck` → 对照上游手动合入，**改过的组件禁用 `migrate-helm-libraries`（会覆盖）**」。
4. 完整矩阵 `scripts/test-template-matrix.ps1`；两场景浏览器实测亮/暗/移动端；a11y 抽查（Spartan MCP a11y tool + Lighthouse）。

### 里程碑与工作量

| 阶段 | 内容 | 估时 | 关键风险 |
| --- | --- | --- | --- |
| 0 | Angular 22 + 工装 | 3 天 | TS6/ESLint10 兼容 |
| 1 | 基座共存 + spike | 2 天 | **TanStack 服务端分页无官方示例**（spike 兜底）；共存期样式冲突 |
| 2 | 横切基元 + 自有基础设施（§五） | 4 天 | 错误链原子改动；theme-service 重设计；confirm/notify API |
| 3 | 布局骨架 | 3 天 | sidebar 结构重排 + cookie/偏好一致性 |
| 4 | 六批功能页（含 Signal Forms） | 5 天 | 表格×2 是大头；Signal Forms 全站铺开 |
| 5 | 清退 + 文档 + 矩阵 | 2 天 | 16px 基准放大的视觉回归 |
| **合计** | | **±19 人日** | |

### 贯穿性风险

1. **16px 字号回归**是全局视觉变化，阶段 5 统一整站过一遍，不每页零敲。
2. **Signal Forms 全站铺开**（D2 无中间态）叠加组件迁移，阶段 4 每页是「组件 + 表单」双重改写，工作量集中于此，需留足缓冲。
3. **Spartan 1.x 年轻**：锁 brain/CLI 小版本，只走 `healthcheck`；helm 代码自持，上游停摆不失能。
4. **模板双面**：每阶段收尾必双场景（default/localization）生成验证，防 `//#if` 丢失。

---

## 八、决策记录（D4）

「弃用 PrimeNG、选定 Spartan UI」是长期架构事实，记录在三处：

1. **本方案文档**（`docs/plans/2026-07-24-primeng-to-spartan-migration.md`）——过程、依据、任务分解的完整记录。方案会随执行推进而过时，作为历史留存。
2. **架构决策记录（ADR）**：在 `docs/architecture/` 下新增一条「前端组件库选型」ADR，只记「决定 + 依据 + 影响 + 备选」，作为不随实施过时的权威落点。这是团队评审与未来回溯的第一入口。
3. **助手持久记忆**（memory）：记「模板前端已决定从 PrimeNG 迁至 Spartan UI 及核心原因与四项决策」，供后续会话直接对齐，不必重新调研。

> 三者分工：ADR = 权威结论（长效）；本文 = 执行细节（阶段性）；memory = 助手上下文（跨会话）。ADR 与 memory 在本文定稿后补建。

---

## 附录：调研来源

- **PrimeNG 许可**：https://primeui.dev/nextchapter · https://primeui.dev/licenses/community · https://primeui.dev/licenses/commercial · https://primeng.dev/migration/v22 · GitHub primefaces discussions #4802/#4807
- **Spartan 工程集成**：https://spartan.ng/documentation/installation · /mcp · /skills · /theming · /styles · /version-support · /update-guide · GitHub spartan-ng/spartan（`libs/cli`、`libs/brain/hlm-tailwind-preset.css`）
- **Spartan 组件用法**：https://spartan.ng/components/{button,input,input-group,field,select,combobox,textarea,switch,toggle-group,dialog,alert-dialog,sheet,drawer,sidebar,popover,dropdown-menu,tooltip,sonner,data-table,table,card,badge,avatar,separator,spinner,progress,scroll-area,skeleton} · /forms/signal-forms · /forms/reactive-forms
- **趋势/滚动条**：State of JS 2025（stateofjs.com）· https://blog.angular.dev/announcing-angular-v21-57946c34f14b · https://tailwindcss.com/blog/tailwindcss-v4-3 · https://tailwindcss.com/docs/scrollbar-color · Angular 版本兼容矩阵 https://angular.dev/reference/versions
- **项目现状**：本仓库 `template/frontend/src` 全量用法盘点（见对话记录）
