# 前端开发规范

本文档为模板项目默认的 Angular 22、Spartan UI（@spartan-ng/brain + helm）、Tailwind CSS 4 前端开发规范。若新项目未采用该技术栈，不应把本文规则作为通用默认事实。

> **注意**: 本文档中的所有开发活动，都必须同时遵循 **[项目通用开发规范](./coding-common.md)** 中定义的 Git 工作流和提交规范。

---

## 目录

1. [核心技术栈](#1-核心技术栈)
2. [目录结构](#2-目录结构)
3. [编码规范](#3-编码规范)
4. [组件与样式规范](#4-组件与样式规范)
5. [开发原则](#5-开发原则)
6. [共享资源](#6-共享资源)
<!--#if (IncludeLocalization)-->
7. [多语言（i18n）](#7-多语言i18n)
<!--#endif-->

---

## 1. 核心技术栈

- **前端框架**: Angular v22+
- **UI 组件库**: Spartan UI（`@spartan-ng/brain` 无头基元 + helm 样式层）
- **原子化CSS**: Tailwind CSS v4+
- **命令行工具**: Angular CLI v22+
- **开发语言**: TypeScript 6.0+
- **状态管理**: Angular Signals
- **表单**: Angular Signal Forms（`@angular/forms/signals`，在 Angular 22 仍为 experimental）；禁止 `FormsModule`/`ReactiveFormsModule`/`ngModel`（eslint 静态拦截）
- **数据表格**: TanStack Table（`@tanstack/angular-table` headless 引擎，服务端 `manualPagination`/`manualSorting`/`rowCount`）；分页/筛选展示复用 `shared/components/table-paginator`、`faceted-filter`，列按优先级响应式裁剪
- **表格操作列**: 按钮按「常用优先、破坏性置后」排序；操作 ≤3 项桌面端全部平铺（icon 按钮 + tooltip），>3 项显示 2 个高频操作 + `…` 溢出菜单；`<sm` 一律收进 `…` 菜单省列宽，破坏性操作在菜单内用 `variant="destructive"` 且分隔线隔开
- **列表查询状态**: 分页/排序/筛选以 **URL query params 为唯一来源**（`queryParamMap` 派生 + `router.navigate({queryParams})` 回写），刷新/分享/前进后退可恢复、非法参数回退默认；不用 localStorage 存查询状态
- **HTTP 错误**: 拦截器只做 401 跳转 + 归一化为类型化 `ApplicationHttpError`（RFC 9457/7807 Problem Details），不发全局 Toast；反馈由发起操作的 feature 决定，全局 Toast 直接用 `@spartan-ng/brain/sonner`

---

## 2. 目录结构

遵循关注点分离原则，组织清晰的目录结构。

```
frontend/
├── _mock/                                    # Mock 服务 (完全独立于源码，用于前端独立开发和测试)
│   ├── api/                                  # API Mock 处理器 (模拟后端API端点)
│   │   └── *.ts
│   ├── data/                                 # 纯粹的模拟数据源
│   │   └── *.ts
│   └── index.ts                              # Mock 服务启动入口（用于导出api）
├── src/
│   ├── app/
│   │   ├── core/                             # 核心逻辑 (非UI, 应用级单例服务和配置)
│   │   │   ├── guards/                       # 路由守卫
│   │   │   │   └── auth-guard.ts             #   - e.g., 检查用户是否登录
│   │   │   ├── interceptors/                 # HTTP 拦截器
│   │   │   │   └── token-interceptor.ts      #   - e.g., 自动为请求附加认证Token
│   │   │   ├── handlers/                     # 处理程序
│   │   │   │   └── global-error-handler.ts   #   - e.g., 全局异常处理
│   │   │   └── services/                     # 应用级核心服务 (非业务，提供基础能力)
│   │   │       └── startup-service.ts        #   - 应用初始化服务 (用于 APP_INITIALIZER)
│   │   ├── features/                         # 业务功能模块 (按业务领域划分)
│   │   │   └── {module-name}/                # 单个业务模块 (e.g., products, users)
│   │   │       ├── components/               # 页面级"智能"组件 (Smart Components)
│   │   │       │   └── {page-name}/          #   - 负责业务逻辑、状态管理和与服务交互
│   │   │       │       ├── {page-name}.html
│   │   │       │       ├── {page-name}.css
│   │   │       │       └── {page-name}.ts    # v20+ 规范: 移除 .component 后缀
│   │   │       ├── widgets/                  # 特性内可复用的"哑"组件 (Dumb Components)
│   │   │       │   └── {widget-name}/        #   - 只在此特性内部复用，不具备全局性
│   │   │       │       ├── {widget-name}.html
│   │   │       │       ├── {widget-name}.css
│   │   │       │       └── {widget-name}.ts
│   │   │       ├── resolvers/                # 路由数据解析器 (在路由激活前预先获取数据)
│   │   │       │   └── {feature}-resolver.ts
│   │   │       ├── services/                 # 业务服务 (实现该特性的业务逻辑和API调用)
│   │   │       │   └── {feature}-service.ts
│   │   │       ├── models/                   # 数据模型 (定义该特性的数据结构)
│   │   │       │   ├── {feature}.dto.ts      #   - DTO (Data Transfer Object): 精确匹配API契约
│   │   │       │   ├── {feature}.model.ts    #   - 领域模型 (Domain Model): 前端使用的丰富模型，可带方法
│   │   │       │   └── {feature}.enum.ts     #   - 枚举 (Enums): 该特性相关的状态、类型等
│   │   │       └── {module-name}.routes.ts   # 路由定义 (该特性的所有子路由)
│   │   ├── layout/                           # 应用布局 (负责应用的整体视觉结构)
│   │   │   ├── components/                   # 布局共享组件 (e.g., header, footer, sidebar)
│   │   │   ├── default/                      # 默认布局 (用于大部分需要登录的后台页面)
│   │   │   ├── empty/                        # 空白布局 (用于登录、404、打印等无导航的页面)
│   │   │   ├── landing/                      # 落地页布局 (用于无需登录的营销或产品介绍页)
│   │   │   └── services/                     # 布局服务 (管理布局状态和主题)
│   │   │       ├── layout-service.ts         #   - 管理侧边栏开关、面包屑等状态
│   │   │       └── theme-service.ts          #   - 管理应用主题 (e.g., light/dark mode)
│   │   ├── shared/                           # 全局共享资源 (跨所有业务特性复用，必须是纯粹、通用的)
│   │   │   ├── components/                   # 全局可复用的"哑"组件 (UI-Kit, 只负责展示和交互，不含业务逻辑)
│   │   │   │   ├── button/                   #   - 自定义按钮
│   │   │   │   ├── card/                     #   - 通用卡片容器
│   │   │   │   └── modal/                    #   - 模态框/对话框框架
│   │   │   ├── directives/                   # 全局可复用的属性/结构指令
│   │   │   │   └── highlight.ts              #   - v20+ 规范: 移除 .directive 后缀
│   │   │   ├── pipes/                        # 全局可复用的管道
│   │   │   │   └── format-date-pipe.ts       #   - v20+ 规范: {pipe-name}-pipe.ts
│   │   │   └── models/                       # 全局共享的数据模型 (仅当模型被多个不相关特性使用时)
│   │   │       ├── user.model.ts             #   - e.g., User模型几乎在所有地方都可能用到
│   │   │       └── product.model.ts          #   - e.g., Product模型可能在订单、购物车、推荐等多个特性中用到
│   │   ├── app.config.ts                     # 应用级配置 (依赖注入、提供商、拦截器注册)
│   │   ├── app.ts                            # 应用根组件 (v20+ 规范)
│   │   ├── app.routes.ts                     # 应用主路由 (定义布局与特性模块的懒加载关系)
│   │   └── main.ts                           # 应用启动文件 (bootstrapApplication)
│   └── environments/                         # 环境配置 (用于区分不同部署环境的变量)
│       ├── environment.base.ts               # 基础环境配置 (所有环境共享的通用变量)
│       ├── environment.ts                    # 默认开发环境 (ng serve 时使用)
│       ├── environment.prod.ts               # 生产环境 (ng build --configuration production 时使用)
│       ├── environment.test.ts               # 测试环境 (e.g., 用于QA服务器或自动化测试)
│       └── environment.debug.ts              # 本地调试环境 (用于需要开启特殊调试标志的本地开发)
├── public/                                   # 静态资源 (构建后位于站点根；无 src/assets)
│   ├── images/                              # 图片
<!--#if (IncludeLocalization)-->
│   └── i18n/                                # 多语言词条 ({lang}.json，运行时 fetch /i18n/)
<!--#endif-->
└── ... (package.json, angular.json, etc.)
```

<!--#if (IncludeLocalization)-->
> 静态资源用 Angular `public/` 约定（构建后映射到站点根），**没有 `src/assets`**。多语言词条 `public/i18n/{lang}.json` 存放各语言文案（见 §7）。
<!--#else-->
> 静态资源用 Angular `public/` 约定（构建后映射到站点根），**没有 `src/assets`**。
<!--#endif-->

---

## 3. 编码规范

### 3.1 编码风格

- **必须** 遵循 **[Angular 官方代码风格指南](https://angular.io/guide/styleguide)**
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

**文件命名风格**:
- Component: `xxx.ts`（参考现有组件命名风格）
- Service: `xxx-service.ts`（参考现有服务命名风格）

### 3.3 类型驱动

- 所有 API 的请求参数和响应数据 **必须** 使用 `interface` 或 `class` 进行严格定义
- JSDoc 应用于描述方法的功能和业务逻辑，**严禁** 在 JSDoc 中重复 TypeScript 的类型定义

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

### 5.4 Mock 开发 ⚠️

- 每个后端新端点**同步补 mock**（数据 + 处理器 + 注册三件套：`_mock/data` + `_mock/api` + `_mock/index.ts`），让前端**脱离后端独立跑**。
- Mock 代码**必须与业务源码分离**，存放在 `_mock` 目录下。
- ⚠️ **mock 的校验/守卫要与后端逐道一致**：后端有几道守卫，mock 里就复刻几道（含过滤匹配语义：子串 vs 精确等），保证独立跑时行为与真后端一致——**空壳 mock 会掩盖真实行为差异**。

### 5.5 测试

- `service`、`pipe` 和包含复杂业务逻辑的函数 **必须** 有单元测试覆盖
- 核心的共享组件和业务流程 **应** 编写组件测试或端到端测试

### 5.6 连锁字段"改一路改全" ⚠️

新增/改一个贯穿前后端的字段（如一个可筛选项），**从后端到前端到 mock 的每一环都要改到**，漏任一环即前后端不一致：

```
后端入参 DTO → 应用层过滤逻辑 → 前端 DTO → 前端 service 传参 → mock 处理器 → mock 数据
```

AI 极易只改一端，务必六环全改。

---

## 6. 共享资源

### 6.1 公共组件位置

前端公共组件在目录 `src/app/shared` 中：

- **分页相关 DTO**: 分页请求和响应数据模型
- **Logo 组件**: 应用 Logo 组件
- **平台图标**: 各平台的图标组件
- **主题配置**: 主题相关的配置和服务
- **公共常量**: 全局共享的常量定义

### 6.2 使用原则

- 只有被多个不相关特性使用的组件才应放入 `shared`
- 特性内部复用的组件应放在特性的 `widgets` 目录下
- 保持 `shared` 目录的纯粹性和通用性

---

<!--#if (IncludeLocalization)-->
## 7. 多语言（i18n）

> 本项目已启用多语言（`--include-localization true`）。默认语言英语，支持 en + zh-CN 运行时切换。

### 7.1 方案与默认语言

- **运行时库 Transloco**（`@jsverse/transloco`）：JSON 词条运行时加载，用户即时切换语言、单包部署——**不用** Angular 编译期 `$localize`（那是按 locale 出多包、无法运行时切换）。
- **默认语言英语（`en`）**，支持 `en` + `zh-CN`；回落语言 `en`。
- 词条文件 `public/i18n/{en,zh-CN}.json`，运行时按 `{baseHref}i18n/{lang}.json` fetch（loader 用 `APP_BASE_HREF` 前缀，兼容子路径部署）。

### 7.2 文案归属（三类，各一处权威）

| 类别 | 归属 | 用法 |
| --- | --- | --- |
| UI 静态文案（菜单、按钮、标签） | 前端词条（权威） | 模板 `{{ 'menu.users' \| transloco }}` / 服务 `transloco.translate('key')` |
| 业务错误消息 | **后端资源**（权威，见 [`api.md`](./api.md)） | 前端直接显示后端已本地化的 `message`，不在前端重复维护业务错误词条 |

- **业务错误不在前端翻译**：后端按 `Accept-Language` 已产出本地化 `message`，前端 `http-error-interceptor` 优先显示它。默认按 **HTTP 状态码**统一处理即可，**无需**消费细分业务 `code`；仅在极少数需要对某个具体错误做差异化 UI 行为（如高亮某输入框）时，才读 `code` 分支——对应后端那处 `WithCode("46")`。前端词条只保留纯客户端兜底（网络断开、后端不可达）。

### 7.3 关键接线（`core/`）

- `core/services/language-service.ts`：`setActiveLang(lang)` 驱动 `TranslocoService.setActiveLang`、同步 `<html lang>`；活动语言持久化到 localStorage（镜像 `theme-service` 形态：signal + `isPlatformBrowser` 守卫）。语言只驱动 Transloco，不联动任何 UI 组件库文案。
- `core/i18n/transloco-loader.ts`：按 `{baseHref}i18n/{lang}.json` 取词条（用 `APP_BASE_HREF` 前缀而非绝对 `/i18n/`，以支持子路径部署）。
- `core/interceptors/accept-language-interceptor.ts`：注入 `Accept-Language` 头，**置拦截器数组首位**，使后端消息按当前语言返回。
- `app.config.ts`：`provideTransloco`（`defaultLang: 'en'`）+ `TranslocoHttpLoader`。
- 语言选择器用 Spartan **dropdown-menu**（`hlmDropdownMenuTrigger` + `ng-template` 模板驱动菜单项，触发按钮用 `hlmBtn`，与铃铛/主题按钮风格一致），封装在 `shared/components/language-switcher`，挂在 `layout/components/default-header` 最右图标区（后台页在铃铛右侧）。

### 7.4 新增文案

- UI 文案：在 `public/i18n/{en,zh-CN}.json` 各加一条键（`模块.语义`，如 `menu.orders`），模板用 `| transloco`。**两语言必须同时加**（CI 有 `scripts/check-i18n-keys.ps1` 键一致性闸门，缺一即红）。
- **响应式**：`.ts` 里要随语言切换更新的文案，别在字段初始化时 `translate()` 定死；改为在 `computed`/getter 里调用 `translate()` 并读一次 `transloco.langChanges$`（或 `languageService.activeLang()`）建立依赖，切换时自动重算。
- 业务错误文案：改后端资源（见 `api.md` §异常本地化），**不在前端加**。
- 数据格式（日期/货币/数字）用 Angular `DatePipe`/`CurrencyPipe`/`DecimalPipe`。注意 **`LOCALE_ID` 是启动期注入、不随运行时语言切换自动改变**；如需格式也跟随切换，需自行传 locale 参数或重建相关视图，别假设它会自动联动。
<!--#endif-->

---

## 附录：规范检查清单

### ✅ 编码规范检查

- [ ] 遵循 Angular v22 最佳风格指南
- [ ] 文件命名符合项目风格
- [ ] 使用 `inject()` 进行依赖注入
- [ ] 所有 API 数据使用类型定义

### ✅ 组件规范检查

- [ ] 优先使用 Spartan UI 组件（helm 层，`libs/ui/`）
- [ ] 表单使用 Signal Forms + Spartan Field（`hlm-field`）
- [ ] 自定义样式使用 Tailwind CSS v4
- [ ] 组件遵循单一职责原则
- [ ] 展示型组件使用 OnPush 策略

### ✅ 代码质量检查

- [ ] 通过 ESLint 检查
- [ ] 通过 Stylelint 检查
- [ ] 通过 Prettier 格式化
- [ ] 核心服务有单元测试

---

