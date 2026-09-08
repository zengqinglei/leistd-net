# 前端开发规范

本文档为模板项目默认的 Angular 22、Spartan UI（@spartan-ng/brain + helm）、Tailwind CSS 4 前端开发规范。若新项目未采用该技术栈，不应把本文规则作为通用默认事实。

同时遵循 [项目通用开发规范](./coding-common.md)。

## 1. 核心技术栈

- **前端框架**: Angular v22+
- **UI 组件库**: Spartan UI（`@spartan-ng/brain` 无头基元 + helm 样式层）
- **原子化CSS**: Tailwind CSS v4+
- **命令行工具**: Angular CLI v22+
- **开发语言**: TypeScript，版本遵循项目依赖及 Angular 编译器的 peer 范围
- **状态管理**: Angular Signals
- **表单**: Angular Signal Forms（`@angular/forms/signals`，在 Angular 22 仍为 experimental）；禁止 `FormsModule`/`ReactiveFormsModule`/`ngModel`（eslint 静态拦截）
- **数据表格**: TanStack Table（`@tanstack/angular-table` headless 引擎，服务端 `manualPagination`/`manualSorting`/`rowCount`）；分页/筛选展示复用 `shared/components/table-paginator`、`faceted-filter`，列按优先级响应式裁剪
- **表格操作列**: 按钮按「常用优先、破坏性置后」排序；操作 ≤3 项桌面端全部平铺（icon 按钮 + tooltip），>3 项显示 2 个高频操作 + `…` 溢出菜单；`<sm` 一律收进 `…` 菜单省列宽，破坏性操作在菜单内用 `variant="destructive"` 且分隔线隔开
- **列表查询状态**: 分页/排序/筛选以 **URL query params 为唯一来源**（`queryParamMap` 派生 + `router.navigate({queryParams})` 回写），刷新/分享/前进后退可恢复、非法参数回退默认；不用 localStorage 存查询状态
- **HTTP 错误**: 拦截器只做 401 跳转 + 归一化为类型化 `ApplicationHttpError`（RFC 9457/7807 Problem Details），不发全局 Toast；反馈由发起操作的 feature 决定，全局 Toast 直接用 `@spartan-ng/brain/sonner`

---

## 2. 目录结构

目录按职责划分，按需新增子目录，不预建空层级。

```text
frontend/
├── _mock/                 # 独立开发用的 API 处理器与数据
├── libs/ui/               # 项目持有的 Spartan Helm 组件
├── src/
│   ├── app/
│   │   ├── core/          # 应用级服务、认证、拦截器与启动逻辑
│   │   ├── features/      # 业务页面及其服务、模型、路由
│   │   ├── layout/        # 布局与导航
│   │   ├── shared/
│   │   │   ├── dtos/      # 跨特性、跨层共享的 API 契约
│   │   │   ├── models/    # 前端模型，不承载 API 契约
│   │   │   ├── components/
│   │   │   ├── directives/
│   │   │   └── pipes/
│   │   ├── app.config.ts
│   │   ├── app.routes.ts
│   │   └── app.ts
│   ├── environments/     # 公共配置与各部署环境的覆盖
│   └── main.ts            # bootstrapApplication 入口
└── public/                # 构建后映射到站点根的静态资源
```

特性内的组件、服务与契约就近组织；跨特性契约放入 `shared/dtos`，避免 `core` 反向依赖 `features`。基础按钮、卡片、对话框优先使用 `libs/ui`，不在 `shared` 重建组件库。

<!--#if (IncludeLocalization)-->
多语言词条放在 `public/i18n/{lang}.json`，见 §8。
<!--#endif-->

---

## 3. 编码规范

### 3.1 编码风格

- 遵循 [Angular 官方代码风格指南](https://angular.dev/style-guide)
- 使用 ESLint 和 Prettier 进行静态检查与自动格式化

### 3.2 命名约定

遵循 Angular v22+ 的简化风格：

| 类型 | 命名规范 | 示例 |
|------|---------|------|
| Component | `{name}.ts` | `user-profile.ts` |
| Service | `{name}-service.ts` | `user-service.ts` |
| Directive | `{name}.ts` | `highlight.ts` |
| Pipe | `{name}-pipe.ts` | `format-date-pipe.ts` |
| Guard | `{name}-guard.ts` | `auth-guard.ts` |

### 3.3 类型驱动

- 所有 API 的请求参数和响应数据 **必须** 使用 `interface` 或 `class` 进行严格定义
- 优先让命名表达意图；JSDoc 只补充不明显的使用契约，行内注释只解释关键约束，不复述类型或语句。

---

## 4. 组件与样式规范

### 4.1 组件库优先

- **必须** 首先在 [Spartan UI 官方文档](https://spartan.ng/) 中寻找现成组件（也可查仓库内 `spartan` skill 或本地 `libs/ui` 源码）
- 组件通过 `ng g @spartan-ng/cli:ui --name=<comp>` 添加，会把 helm 样式层复制进 `libs/ui/`；helm 层是本项目自有代码，可按需修改
- 尽量沿用 Spartan 的默认组件与风格（`hlm*` 指令 / `hlm-*` 组件）
- 仅在无法满足需求时才可创建自定义组件

### 4.2 样式方案

- **必须** 优先使用 Tailwind CSS v4 的原子类进行布局和微调
- 自定义样式使用 Tailwind CSS v4
- 任何自定义样式都**必须**与 Spartan UI 的主题风格保持一致（基于 Spartan 主题的 CSS 变量与 `.dark` class）
- 登录/注册/落地页属**品牌隔离层**：允许使用具体色值（`blue-*`、hex 等）营造品牌视觉，这些页面的样式**不是**业务页面可复制的范式；业务页面一律使用语义化主题变量

### 4.3 有限语义色板

- 状态/分类标签**只用项目约定的有限几个语义色**，不随意扩色。
- **分类标签用中性色，不用告警色表达"非告警"的分类**（不要用 warn 橙表达一个普通分类）。

> `例`：用 Spartan badge 的有限变体 `default` / `secondary` / `destructive` / `outline`（`hlmBadge`）；分类标签一律用中性变体（`secondary`）。

### 4.4 组件设计

- **单一职责 (SRP)**: 每个组件应只关注一个功能点
- **数据流**: 遵循"单向数据流"原则（`[input]` 向下, `(output)` 向上）
- **变更检测**: 为提高性能，所有共享/展示型组件都应使用 `changeDetection: ChangeDetectionStrategy.OnPush`

### 4.5 图标 / 文案跨页一致

- 跨页的图标尺寸、文案措辞要统一；**对齐用实测（浏览器/像素）确认，不靠目测**。
- 图标使用 `@ng-icons` + lucide：模板写 `<ng-icon name="lucideXxx">`，并在组件级用 `provideIcons({ lucideXxx })` 按需注册（不全局注册全部图标）。

### 4.6 表单规范

- **必须** 使用 Angular Signal Forms（`@angular/forms/signals`）：以 `form()` 构建表单模型，模板用 `[formField]` 绑定字段，不用 `[(ngModel)]` / Reactive Forms。
- 表单 UI **必须** 走 Spartan Field 组件族：`hlm-field` 容器 + `hlmFieldLabel` 标签 + `hlm-field-error` 错误展示，配合 `hlmInput` / `hlm-select` 等输入组件。校验态由 brain 层（`BrnField`）读取，与具体表单引擎解耦。
- ⚠️ **Signal Forms 在 Angular 22 仍为 experimental**（官方明示 API 可能在小版本间 breaking）。因此**必须锁定 Angular 版本**；升级 Angular 后需手工回归所有表单，确认 `@angular/forms/signals` API 未破坏。

### 4.7 Spartan 维护约定

- **两层结构**：brain 层 `@spartan-ng/brain` 是无头基元，作为 npm 依赖引入、不改；helm 层是样式实现，通过 CLI **复制进本项目** `libs/ui/`，属自有代码，可自由修改。
- **加组件**：`ng g @spartan-ng/cli:ui --name=<comp>`，把对应 helm 组件生成到 `libs/ui/`。
- **升级**：升级 `@spartan-ng/brain` + `@spartan-ng/cli` 后跑 `ng g @spartan-ng/cli:healthcheck` 检查兼容性；**已改过的 helm 组件禁用 `migrate-helm-libraries`**（它会用上游版本覆盖自定义改动），需对照上游变更**手动合入**。为保稳定，锁定 brain / CLI 的小版本，只走官方 `healthcheck` 流程升级。

---

## 5. 开发原则

### 5.1 依赖注入

- **必须** 使用 `inject()` 函数进行依赖注入
- 构造函数 (`constructor`) **仅用于** 执行简单的属性赋值

### 5.2 状态管理

- 对于组件内部或简单父子组件间的状态，**必须** 优先使用 Angular 内置的 **Signals**

### 5.3 错误处理

- **必须** 实现一个全局错误处理机制 (`ErrorHandler`)
- 业务代码中**可以**通过 `catchError` 优先处理特定异常，但**严禁**"吞噬"异常

### 5.4 Mock 开发

- 每个后端新端点**同步补 mock**（数据 + 处理器 + 注册三件套：`_mock/data` + `_mock/api` + `_mock/index.ts`），让前端**脱离后端独立跑**。
- Mock 代码**必须与业务源码分离**，存放在 `_mock` 目录下。
- Mock 与后端保持可观察行为一致，包括认证、权限、参数校验、过滤语义和失败响应；不要求复制后端内部实现。

### 5.5 测试

- `service`、`pipe` 和包含复杂业务逻辑的函数 **必须** 有单元测试覆盖
- 核心的共享组件和业务流程 **应** 编写组件测试或端到端测试

### 5.6 跨层字段同步

新增/改一个贯穿前后端的字段（如一个可筛选项），**从后端到前端到 mock 的每一环都要改到**，漏任一环即前后端不一致：

```
后端入参 DTO → 应用层过滤逻辑 → 前端 DTO → 前端 service 传参 → mock 处理器 → mock 数据
```

按字段影响范围逐项核对，不遗漏消费方。

---

## 6. 共享资源

### 6.1 公共组件位置

跨特性展示组件放入 `src/app/shared/components`，API 契约与前端模型按 §2 分开；主题等应用级服务放入 `core/services`。

### 6.2 使用原则

- 只有被多个不相关特性使用的组件才应放入 `shared`
- 特性内部复用的组件应放在特性的 `widgets` 目录下
- 保持 `shared` 目录的纯粹性和通用性

---

## 7. 日期与时区

时间一律以 UTC 传输与存储，只在展示时按会话时区换算。时区取自 `Display.TimeZone` 设置（IANA 名），由 `SettingContextService` 在启动时载入。

- **业务日期一律用 `appDate` 管道，不要用 Angular 的 `date`**：`date` 的时区参数只接受 `+0800` 这类固定偏移，传 IANA 名（`Asia/Shanghai`）会在内部解析失败后**静默回落到浏览器时区**——界面照常渲染，时区设置却没生效，编译和端到端都发现不了；固定偏移也表达不了夏令时。`appDate` 基于原生 `Intl.DateTimeFormat`，直接接受 IANA 名并自带夏令时规则。用法是把会话时区显式传进去：

  ```html
  {{ row.creationTime | appDate: 'full' : displayTimeZone() }}
  ```

  ```ts
  protected readonly displayTimeZone = inject(SettingContextService).timeZone;
  ```

时区值只收 IANA 名。后端会连 Windows 时区 ID（`China Standard Time` 之类）一起解析出来，但浏览器的 `Intl.DateTimeFormat` 对它抛错——所以写入端已按 `HasIanaId` 卡住，前端不必再判一次，但也不要绕过接口自己塞值。

---

<!--#if (IncludeLocalization)-->
## 8. 多语言（i18n）

> 本项目已启用多语言（`--include-localization true`）。默认语言英语，支持 en + zh-CN 运行时切换。

### 8.1 方案与默认语言

- **运行时库 Transloco**（`@jsverse/transloco`）：JSON 词条运行时加载，用户即时切换语言、单包部署——**不用** Angular 编译期 `$localize`（那是按 locale 出多包、无法运行时切换）。
- **默认语言英语（`en`）**，支持 `en` + `zh-CN`；回落语言 `en`。
- 词条文件 `public/i18n/{en,zh-CN}.json`，运行时按 `{baseHref}i18n/{lang}.json` fetch（loader 用 `APP_BASE_HREF` 前缀，兼容子路径部署）。

### 8.2 文案归属

| 类别 | 归属 | 用法 |
| --- | --- | --- |
| UI 静态文案（菜单、按钮、标签） | 前端词条（权威） | 模板 `{{ 'menu.users' \| transloco }}` / 服务 `transloco.translate('key')` |
| 业务错误消息 | **后端资源**（权威，见 [`api.md`](./api.md)） | 前端直接显示后端已本地化的 `message`，不在前端重复维护业务错误词条 |

- 后端按 `Accept-Language` 返回本地化消息；`http-error-interceptor` 归一化错误，由发起操作的 feature 展示。仅在需要差异化 UI 行为时按业务 `code` 分支。前端词条提供网络断开、后端不可达等客户端兜底。

### 8.3 关键接线（`core/`）

- `core/services/language-service.ts`：`setActiveLang(lang)` 驱动 `TranslocoService.setActiveLang`、同步 `<html lang>`；活动语言持久化到 localStorage（镜像 `theme-service` 形态：signal + `isPlatformBrowser` 守卫）。语言只驱动 Transloco，不联动任何 UI 组件库文案。
- `core/i18n/transloco-loader.ts`：按 `{baseHref}i18n/{lang}.json` 取词条（用 `APP_BASE_HREF` 前缀而非绝对 `/i18n/`，以支持子路径部署）。
- `core/interceptors/accept-language-interceptor.ts`：注入 `Accept-Language` 头，**置拦截器数组首位**，使后端消息按当前语言返回。
- `app.config.ts`：`provideTransloco`（`defaultLang: 'en'`）+ `TranslocoHttpLoader`。
- 语言选择器用 Spartan **dropdown-menu**（`hlmDropdownMenuTrigger` + `ng-template` 模板驱动菜单项，触发按钮用 `hlmBtn`，与铃铛/主题按钮风格一致），封装在 `shared/components/language-switcher`，挂在 `layout/components/default-header` 最右图标区（后台页在铃铛右侧）。

### 8.4 新增文案

- UI 文案：在 `public/i18n/{en,zh-CN}.json` 各加一条键（`模块.语义`，如 `menu.orders`），模板用 `| transloco`。**两语言必须同时加**（CI 有 `scripts/check-i18n-keys.ps1` 键一致性闸门，缺一即红）。
- **响应式**：`.ts` 里要随语言切换更新的文案，别在字段初始化时 `translate()` 定死；改为在 `computed`/getter 里调用 `translate()` 并读一次 `transloco.langChanges$`（或 `languageService.activeLang()`）建立依赖，切换时自动重算。
- 业务错误文案：改后端资源（见 `api.md` §异常本地化），**不在前端加**。
- 货币/数字用 Angular `CurrencyPipe`/`DecimalPipe`。注意 **`LOCALE_ID` 是启动期注入、不随运行时语言切换自动改变**；如需格式也跟随切换，需自行传 locale 参数或重建相关视图，别假设它会自动联动。
<!--#endif-->

---
