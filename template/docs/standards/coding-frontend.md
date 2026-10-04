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
- **数据表格**: TanStack Table（`@tanstack/angular-table` headless 引擎，服务端 `manualPagination`/`manualSorting`/`rowCount`）；分页/筛选展示复用 `shared/components/table-paginator`、`faceted-filter`，列按优先级（`primary`/`secondary`/`tertiary`）裁剪：视口档位（`tableViewportSignal()`）给上限，带右侧吸附操作列的表格在外框挂 `appTableFit`，容器放不下时再降一档，免得吸附列压住被横向滚走的内容（写法 `[appTableFit]="tableViewport()" [appTableFitContent]="rows()"`；指令按数据引用判断数据是否变了，更新数据要换新数组，不要原地改数组里的字段）；被裁剪的列由行展开补偿，展开状态用 TanStack 的行展开特性（`row.getIsExpanded()` / `row.toggleExpanded()`，按 `getRowId` 给出的实体 id 记，翻页、刷新不收起），不另建展开状态
- **表格操作列**: 按钮按「常用优先、破坏性置后」排序；操作 ≤3 项桌面端全部平铺（icon 按钮 + tooltip），>3 项显示 2 个高频操作 + `…` 溢出菜单；`<sm` 一律收进 `…` 菜单省列宽，破坏性操作在菜单内用 `variant="destructive"` 且分隔线隔开
- **列表查询状态**: 分页/排序/筛选以 **URL query params 为唯一来源**（`queryParamMap` 派生 + `router.navigate({queryParams})` 回写），刷新/分享/前进后退可恢复、非法参数回退默认；不用 localStorage 存查询状态
- **HTTP 错误**: 拦截器只做 401 跳转 + 归一化为类型化 `ApplicationHttpError`（RFC 9457 Problem Details；`errors` 认本框架的数组与官方 `HttpValidationProblemDetails` 的字典两种形状），不发全局 Toast；反馈由发起操作的 feature 决定，全局 Toast 直接用 `@spartan-ng/brain/sonner`

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

多语言全局词条放在 `public/i18n/{lang}.json`，功能词条放在 `public/i18n/<scope>/{lang}.json`，见 §9。
<!--#endif-->

---

## 3. 编码规范

### 3.1 编码风格

- 遵循 [Angular 官方代码风格指南](https://angular.dev/style-guide)
- 使用 ESLint 和 Prettier 进行静态检查与自动格式化

### 3.2 命名约定

遵循 Angular v22+ 的简化风格：

| 类型      | 命名规范            | 示例                  |
| --------- | ------------------- | --------------------- |
| Component | `{name}.ts`         | `user-profile.ts`     |
| Service   | `{name}-service.ts` | `user-service.ts`     |
| Directive | `{name}.ts`         | `highlight.ts`        |
| Pipe      | `{name}-pipe.ts`    | `format-date-pipe.ts` |
| Guard     | `{name}-guard.ts`   | `auth-guard.ts`       |

### 3.3 类型驱动

- 所有 API 的请求参数和响应数据 **必须** 使用 `interface` 或 `class` 进行严格定义
- 优先让命名表达意图；JSDoc 只补充不明显的使用契约，行内注释只解释关键约束，不复述类型或语句。

---

## 4. 组件与样式规范

### 4.1 组件库优先

- **必须** 先检查本地 `libs/ui` 是否有现成组件及其锁定版本；需要新增组件或核对 API 时，再查项目内 `spartan` Skill 与匹配版本的 [Spartan UI 官方文档](https://spartan.ng/)
- 组件通过 `ng g @spartan-ng/cli:ui --name=<comp>` 添加，会把 helm 样式层复制进 `libs/ui/`；helm 层是本项目自有代码，可按需修改
- 尽量沿用 Spartan 的默认组件与风格（`hlm*` 指令 / `hlm-*` 组件）
- 仅在无法满足需求时才可创建自定义组件

### 4.2 样式方案

- **必须** 优先使用 Tailwind CSS v4 的原子类进行布局和微调
- 自定义样式使用 Tailwind CSS v4
- 任何自定义样式都**必须**与 Spartan UI 的主题风格保持一致（基于 Spartan 主题的 CSS 变量与 `.dark` class）
- 所有页面一律使用语义化主题变量，不写具体色值（`blue-*`、hex 等）。换品牌改 `src/styles.css` 里的令牌：品牌色亮暗成对改（同一色相，亮色 L≈0.52、暗色 L≈0.72），中性色带一点跟随品牌色相的彩度
- 登录、注册、强制启用两步验证共用 `features/account/components/auth-shell`（品牌标识 + 居中卡片 + 主题/语言切换）。这几页在一次登录里连续经过，外观必须一致

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
- **校验提示按错误类型取词条**：验证器不带 `message`，模板按 `error.kind` 取 `validation.<kind>`，错误对象整个作参数：

  ```html
  @for (error of form.email().errors(); track error.kind) {
    <hlm-field-error [validator]="error.kind">{{ t('validation.' + error.kind, error) }}</hlm-field-error>
  }
  ```

  内置错误的参数就是错误对象上的字段，占位符照它命名（`minLength` 错误的 `{{minLength}}`、`maxLength` 错误的 `{{maxLength}}`）。字段特有的说法给自定义类型：内置验证器用 `error` 选项（`pattern(path.username, /^[a-zA-Z0-9_]{3,64}$/, { error: { kind: 'usernamePattern' } })`），跨字段规则由 `validate()` 返回 `{ kind: 'passwordMismatch' }`。同一字段只让一个验证器报同一类型——长度与字符集共用一句时合成一条正则，分开写同一句会出现几遍。
<!--#if (IncludeLocalization)-->
- 新的错误类型在 `public/i18n/{en,zh-CN}.json` 的 `validation` 段各加一句；键是拼出来的，静态引用查不到，由 `scripts/check-i18n-keys.ps1` 核对表单用到的类型（自定义 `kind`、所用内置验证器、没给 `error` 的 `pattern`）都有句子。服务端返回的字段错误不走这里（见 §5.4）。
<!--#else-->
- 新的错误类型在 `shared/utils/english-text.ts` 的校验提示表里加一句，它并入每个组件的 `t`。服务端返回的字段错误不走这里（见 §5.4）。
<!--#endif-->
- ⚠️ **Signal Forms 在 Angular 22 仍为 experimental**（官方明示 API 可能在小版本间 breaking）。因此**必须锁定 Angular 版本**；升级 Angular 后需手工回归所有表单，确认 `@angular/forms/signals` API 未破坏。

### 4.7 Spartan 维护约定

- **两层结构**：brain 层 `@spartan-ng/brain` 是无头基元，作为 npm 依赖引入、不改；helm 层是样式实现，通过 CLI **复制进本项目** `libs/ui/`，属自有代码，可自由修改。
- **加组件**：`ng g @spartan-ng/cli:ui --name=<comp>`，把对应 helm 组件生成到 `libs/ui/`。
- **升级**：升级 `@spartan-ng/brain` + `@spartan-ng/cli` 后跑 `ng g @spartan-ng/cli:healthcheck` 检查兼容性；**已改过的 helm 组件禁用 `migrate-helm-libraries`**（它会用上游版本覆盖自定义改动），需对照上游变更**手动合入**。为保稳定，锁定 brain / CLI 的小版本，只走官方 `healthcheck` 流程升级。
- **已定制的 helm 组件**（升级时逐个对照上游手动合入）：

  | 组件                                              | 改动                                                                                      | 原因                                                                                                                                                                                                                             |
  | ------------------------------------------------- | ----------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
  | `button`                                          | `default` / `lg` 加 `pointer-coarse:h-11`，`icon` / `icon-lg` 加 `pointer-coarse:size-11` | 触屏设备的点按目标不小于 44px；`xs` / `sm` 是刻意选的紧凑尺寸，不改                                                                                                                                                              |
  | `input`、`input-group`                            | 加 `pointer-coarse:h-11`                                                                  | 与按钮同高，表单里并排时对齐                                                                                                                                                                                                     |
  | `select`（trigger）                               | `data-[size=default]` 下加 `pointer-coarse:h-11`                                          | 同上                                                                                                                                                                                                                             |
  | `dialog`、`alert-dialog`、`sheet`、`popover`、`tooltip`、`select`、`combobox`、`navigation-menu`（内容与遮罩） | 进出场动画加 `motion-safe:` 前缀；`sheet` 内容、`navigation-menu` 内容与触发器箭头的过渡另加 `motion-reduce:transition-none` | 系统开启"减少动效"时不播放缩放、滑入。写法与上游 `dropdown-menu` 一致；Brain 关闭浮层时只等待正在播放的动画，没有动画就立即关闭 |
  | `sidebar`（`hlm-sidebar`、`-menu-button`、`-group-label`、`-group-action`、`-menu-action`、`-rail`） | 宽度、位置、外边距与位移过渡加 `motion-reduce:transition-none` | 同上：折叠、展开侧栏时不播放滑动；颜色等非位移反馈不受影响 |
  | `dropdown-menu`（`hlm-dropdown-menu-trigger.ts`） | 改 `menuPosition` 后调用 CDK 触发器的 `ngOnChanges`，让已建好的 overlay 更新定位策略      | 上游直接赋值，不经过 `ngOnChanges`，菜单打开过一次后再改 `side` / `align` 不生效；侧栏内容在桌面与手机抽屉间复用同一实例，用户菜单与区域切换器的方向随断点变化，会被摆错。由 `dropdown-side-switch.spec.ts` 钉住，上游修复后删除 |

  用 `pointer-coarse` 而不是屏幕宽度判断：平板横屏很宽，但仍是手指操作。

  手机端侧栏抽屉的读屏名称曾是本项目定制，spartan 1.5.0 起由上游提供（[#1758](https://github.com/spartan-ng/spartan/issues/1758)），定制已删除；文案经 `hlm-sidebar` 的 `srOnlySheetTitle` / `srOnlySheetDescription` 传入。

  升级步骤：升级 brain / CLI 后跑 `healthcheck`；未定制的组件用 `ng g @spartan-ng/cli:migrate-helm-libraries --libraries=<name>` 同步到新版本（传 `--libraries` 即非交互）；表中组件同样先同步，再对照上表把定制补回（`git diff` 可看出被覆盖的那几行）。helm 与 CLI 版本脱节时，新参数与无障碍改进不会自动到位——升级 CLI 不等于 helm 已更新。

### 4.8 视觉规范

字号按**层级**用，每一档只有一个职责，不出现档外值（`text-[10px]` 之类）。正文基准是 14px——按钮、输入框、表格、卡片描述都是这个字号，这是组件已经定下的。

| 档位 | 用途                   | 写法                                    |
| ---- | ---------------------- | --------------------------------------- |
| 24px | 页面标题（有则一个）   | `text-2xl font-semibold tracking-tight` |
| 16px | 面板标题、卡片标题     | `text-base font-semibold`               |
| 14px | 分区标题               | `text-sm font-semibold`                 |
| 14px | 正文、表格、按钮、输入 | `text-sm`                               |
| 12px | 辅助说明、时间戳、徽章 | `text-xs`                               |
| 30px | 仅仪表盘大数字         | `text-3xl` + `tabular-nums`             |

- 页面不必都有可见标题：面包屑与浏览器标签页已经说明身在何处的列表页，可以直接从工具栏开始；有页面标题时用 24px 这一档，一页只出现一个。
- 字重只用 400 / 500 / 600，不用 `font-bold` / `font-extrabold`。
- 加在图标、`hlm-spinner` 上的 `text-*` 是图标尺寸，不受上表约束。
- 输入框的 `text-base md:text-sm` 不要改：iOS Safari 遇到小于 16px 的输入框会在聚焦时放大页面。

圆角三档，由元素的角色决定：

| 档位           | 用于                                                                         |
| -------------- | ---------------------------------------------------------------------------- |
| `rounded-xl`   | 页面级容器：卡片、对话框、表格外框、设置面板的分区卡片                       |
| `rounded-lg`   | 控件（按钮、输入框，组件已是）；嵌在卡片或对话框里的块、代码框、行项、图标块 |
| `rounded-full` | 徽章、头像                                                                   |

嵌套的块用 `lg` 而不是 `xl`：内外同一个圆角，层次就分不出来。组件内部的小元素（菜单项、清除按钮）跟随所在组件的写法。

- **当前项**：侧栏、顶栏、设置面板导航统一用 `bg-primary/10 text-primary font-semibold`（按组件的状态属性挂，如 `data-active:`、`aria-[current=page]:`）。
- **间距**：兄弟元素之间用 `flex` / `grid` + `gap-*`，不用 `space-y-*` 与逐个元素的外边距。页面外框统一 `p-4 sm:p-6`。
- **暗色**：表面分五层逐级提亮（`sidebar` < `background` < `card` < `popover` < `muted`），不用纯黑；层级都在 `styles.css` 里定好，页面不写 `dark:` 颜色覆盖。
- **图标 + 文案的空态一律用 flex 列**（`flex flex-col items-center justify-center gap-2`），**不要**靠在 `ng-icon` 上加 `block` 让图标独占一行：`ng-icon` 组件自身的 host 样式把它按行内盒渲染，`block` 压不住，结果是图标与文字挤在一行、基线还错开。用 flex 列之后对齐不再取决于哪一条 `display` 规则赢。同款写法见通知面板的空态。
- **字段说明不要做成独立方框。** 口令规则、格式提示这类说明贴在字段下方的描述位（`hlm-field-description`），与输入框同宽同起止。做成带边框底色的独立块会在视觉上像另一个输入控件，而它的宽度由容器决定、与下方真正的输入框对不齐。

---

## 5. 开发原则

### 5.1 依赖注入

- **必须** 使用 `inject()` 函数进行依赖注入
- 构造函数 (`constructor`) **仅用于** 执行简单的属性赋值

### 5.2 状态管理

- 对于组件内部或简单父子组件间的状态，**必须** 优先使用 Angular 内置的 **Signals**
- 媒体查询（视口断点、系统暗色偏好）用 CDK `BreakpointObserver` 转成信号，不手写 `matchMedia` 监听
- 启动流程、拦截器里判断「当前在哪条路由上」用 `core/routing/entry-route-service.ts`（读 Angular `Location`，路径与哈希路由通用），不读 `Router.url`：初始导航之前它恒为 `/`

### 5.3 显示偏好：设置项与切换器必须同源

语言、主题、时区这类"显示偏好"常有两个入口：顶栏的切换器，和个人偏好页里的那一行。
两者写的必须是**同一处存储**，否则用户在一处改完、另一处不认。

判据只有一条，按"有没有做成 setting definition"分：

| 形态                               | 写入口径                                                              | 例                         |
| ---------------------------------- | --------------------------------------------------------------------- | -------------------------- |
| 已做成 setting definition          | 切换器**必须**走同一个 `setForCurrentUser`，不得旁路写 `localStorage` | 语言（`Display.Language`） |
| 未做成 setting definition          | 允许纯 `localStorage`，但**不得**同时出现在偏好页                     | 主题                       |
| 跨服务只读方（本服务不拥有该偏好） | 切换器只作用于本会话；设置页不放这一行，只放去签发方的外链            | 资源服务里的账户偏好       |

**最容易踩的是第一行的反面**：给某个显示偏好补了 setting definition，却忘了改切换器。
那一刻起切换器仍写本地存储，偏好页写服务端，两个值各自为政——而且不会报错，
只表现为"我明明改了，刷新又变回去"。加 setting definition 时**同一个提交里**必须改掉切换器。

第三行的理由值得单独说：跨服务时本服务**不拥有**那个偏好，写它就是造第二份真相。
签发方改了、本服务的副本不会跟着变，而用户看到的是本服务这一份——排查时无从判断哪边是对的。
所以只作用于本会话（刷新即回到签发方的值），并在设置页给外链，把"去哪里改"说清楚。

### 5.4 错误处理

- **必须** 实现一个全局错误处理机制 (`ErrorHandler`)
- 业务代码中**可以**通过 `catchError` 优先处理特定异常，但**严禁**"吞噬"异常
- HTTP 层统一将 Problem Details 解析为 `ApplicationHttpError`；页面不自己猜测后端响应形状。前端不兼容框架可选的响应信封（`AddResponseWrapper()`）：开启它要同时改两处——成功响应在拦截器里解开 `data`，失败响应从信封的 `errorCode` / `message` 读错误码与消息。
- 表单校验错误的 `errors` 优先回填到字段；已被局部流程明确处理的错误不再重复 toast，其余可展示的 4xx 按 `detail`、`title` 的顺序取安全文案统一 toast。
- 5xx 不展示后端技术细节；使用通用文案，并在响应含 `traceId` 时附上按当前语言本地化的追踪 ID 标签供支持人员排查。
- 不以单个 HTTP 状态码代替业务分支；需要特定交互时使用稳定 `code`。

### 5.5 Mock 开发

- 每个后端新端点**同步补 mock**（数据 + 处理器 + 注册三件套：`_mock/data` + `_mock/api` + `_mock/index.ts`），让前端**脱离后端独立跑**。
- Mock 代码**必须与业务源码分离**，存放在 `_mock` 目录下。
- Mock 与后端保持可观察行为一致，包括认证、权限、参数校验、过滤语义和失败响应；不要求复制后端内部实现。

### 5.6 测试

- `service`、`pipe` 和包含复杂业务逻辑的函数 **必须** 有单元测试覆盖
- 核心的共享组件和业务流程 **应** 编写组件测试或端到端测试
- 用例名规则见 `docs/standards/testing.md` §3：英文句子，中文只出现在注释与测试数据里。
- **单测不得触发真实的下载、打印或页面跳转。** 单测跑在真实的 Chromium 里，
  这些动作会作用到跑测试的那台机器上：调一次真实下载路径就往开发者的下载目录落一个文件，
  一天下来能攒出上百个。这类失败不会让用例变红，所以只能靠约定。
  对副作用的那一步打桩（`saveBlob`、或 `vi.spyOn(HTMLAnchorElement.prototype, 'click').mockReturnValue(undefined)`——`vi.spyOn` 默认仍会调用原实现，必须显式替换），
  断言"发起了什么"而不是"文件存下来了"——见 `shared/utils/download-file.spec.ts`。

### 5.7 跨层字段同步

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

时间一律以 UTC 传输与存储，只在展示时按会话时区换算。时区取自 `Display.TimeZone`（IANA 名），书写方式取自界面语言，两者都由 `SettingContextService` 提供。

- **业务日期一律用 `appDate` 管道，不要用 Angular 的 `date`**：`date` 的时区参数只接受 `+0800` 这类固定偏移，传 IANA 名（`Asia/Shanghai`）会在内部解析失败后**静默回落到浏览器时区**——界面照常渲染，时区设置却没生效，编译和端到端都发现不了；固定偏移也表达不了夏令时。`appDate` 基于原生 `Intl.DateTimeFormat`，直接接受 IANA 名并自带夏令时规则。用法是把时区与 locale 都显式传进去：

  ```html
  {{ row.creationTime | appDate: 'full' : displayTimeZone() : displayLocale() }}
  ```

  ```ts
  protected readonly displayTimeZone = inject(SettingContextService).timeZone;
  protected readonly displayLocale = inject(SettingContextService).displayLocale;
  ```

- **三个参数管三件互不相干的事，由三个不同的地方决定，别互相替代**：
  - **槽位**决定"要什么、多细"，由调用点定：`full`（到秒）/ `short`（到分）/ `date` / `time` / `monthDayTime`（通知列表这类窄位置，不带年也不带秒）。
  - **时区**决定"哪一刻"。
  - **locale** 决定"怎么写"（字段顺序、月份写法、12/24 小时制），取界面语言。
- **精度不要做成设置项**：一列要不要秒是这一列的用途决定的，通知列表要窄、审计详情要能对时。给它一个全局开关只会让两处被同一个值拽着走，而用户不会为了看通知去改一个叫"日期格式"的开关。
- **界面语言取"此刻正在用的那个"，不要从持久化的设置里再推一份**：账户设置只是这份偏好的持久化，运行时还有设备选择与系统语言两个来源。从设置快照推导，切语言后文案立刻变、日期要等快照回来才跟上，写回失败时更是一直分叉，访客在登录页切的语言则根本推不出来。
- **书写方式由界面语言驱动，不要由时区驱动**：时区回答"哪一刻"，locale 才编码"日期怎么读"。Web 平台也没有时区→地区的映射：`Intl` 只给时区 id，要从 `Asia/Shanghai` 推出 `zh-CN` 得自带一份 IANA `zone.tab` 的时区→国家表，而它既随时区拆分改名而漂移，又不是个函数（`Europe/Zurich` 对应德/法/意三种写法，`UTC` 没有国家）。
- **不要新增"自定义格式串"这类设置**：月日顺序、12/24 小时本就是 locale 属性；让人手填 `yyyy-MM-dd` 这样的 pattern，填错既不报错也不好查，只会静默渲染出错误日期。要给某个语种改写法，就在管道的 `WRITING_OVERRIDES` 里加一条并写清依据（目前只有一条：中文按 GB/T 7408 用短横线）。真要让用户独立选 12/24 小时制，用 `Intl` 自己的表达方式——`hourCycle` 选项或 locale 扩展 `en-US-u-hc-h23`——而不是自造格式串。
- **时区候选项按"能否真的渲染"判定**，不要拿 `Intl.supportedValuesOf('timeZone')` 当合法清单去过滤：它只列**规范名**，`UTC`、`Asia/Kolkata`、`America/Argentina/Buenos_Aires` 都不在其中却都能用，过滤会把它们静默删掉。

时区值只收 IANA 名。后端会连 Windows 时区 ID（`China Standard Time` 之类）一起解析出来，但浏览器的 `Intl.DateTimeFormat` 对它抛错——所以写入端已按 `HasIanaId` 卡住，前端不必再判一次，但也不要绕过接口自己塞值。

---

## 8. 导航与菜单分组

导航菜单是**信息架构**，不是控件清单：分组摆错了既不报错也不影响功能，只是让人找不到入口。两个区各有一套菜单，都定义在 `layout/services/navigation-service.ts` 一处，管理平台的侧栏与工作空间的顶栏都从这里读，判据相同。

| 分组               | 放什么                                     | 现有入口                                                                             |
| ------------------ | ------------------------------------------ | ------------------------------------------------------------------------------------ |
| 工作               | 进来先看的东西：本区落地页、待办、我的任务 | 平台「仪表盘」/ 工作空间「工作台」                                                   |
| 业务               | 这个系统"做业务"的地方                     | 模板只放一个「示例模块」占位，下游项目在这里加自己的                                 |
| 个人（仅工作空间） | 关于"我自己"的：账户、安全、偏好、通知     | 设置                                                                                 |
| 身份与访问         | 谁能用、能用什么                           | 用户管理、角色管理、租户管理                                                         |
| 开发者             | 面向集成方的东西                           | 开放应用（OAuth 客户端）                                                             |
| 审计               | 谁在什么时候做了什么                       | 操作记录（登录记录、导出记录同属这一类）                                             |
| 系统               | 本租户（或宿主）的默认值与策略             | 系统设置（注册验证、登录安全、邮件发送、运维、审计等面板，一个后端设置分组一个面板） |

- **不设「其它」这类兜底组。** 兜底组会把不相干的入口越塞越多，最后谁也说不清该往哪找。新入口必须落进上面某一类；落不进就说明该新开一类，并把判据补进这张表。
- **按用户的目的分组，不按后端模块或表结构分组。** 用户找的是"我要做什么"，不是"这属于哪个服务"。
- **管理平台的菜单分组与后端的权限分组同名同序。** 每个菜单项只挂它对应的根权限，菜单项标题与根权限显示名一致，这是与后端共同遵守的命名约定，见 [后端开发规范 §3.8.1](./coding-backend.md#381-权限定义与界面一一对应)。菜单项的权限必须列进 `PLATFORM_ENTRY_PERMISSIONS`，由 `default-sidebar.spec.ts` 核对。
- **个人设置只有一处：工作空间「个人」组下的「设置」**（个人资料、账户与安全、偏好、通知是它的面板）。管理人员也是用户，从头像菜单的「个人设置」进同一处，管理平台不另放一份——两个入口就是两份状态。系统设置是管理动作，放在管理平台的「系统」组，与个人设置的作用域不同，不要合并。
- 监控、健康检查、后台任务这类"系统怎么跑"的东西以后若要进导航，新开「运维」组；日志级别这类运维**配置**放在系统设置的「运维」面板里，不进导航。
- **头像菜单里不放「切换租户」。** 已登录会话的租户由 cookie claim 定案，换租户只能重新登录——那一项做的事其实就是退出登录，单列一个入口只会让人以为存在会话内切换。换租户统一走登录页：退出后按域名定案，或在域名不表态时于登录页清掉 / 换一个租户。
- 菜单项只按权限裁剪（`permissions` 任一命中即可见），整组为空时**整组消失**，不留空标题——空标题看起来像加载失败。
- **按"能不能做"分支，不按"我是谁"分支。** 需要区分宿主侧与租户侧时，判据是权限——服务端下发的
  权限列表**已按侧别过滤**（宿主专属的 `App.Tenants` 不会出现在租户用户的列表里），所以"有没有这个权限"
  就是那个问题的答案，且与服务端同一判据。**不要从 `TenantContextService` 推导侧别**：本地身份形态下
  它只是登录页写进 localStorage 的路由提示，与已认证会话的租户可以不一致（宿主超管在另一标签页的登录页
  确认过一个租户名，它就有值了，而会话仍是宿主）。权限表达不了的显示决定（"这个租户叫什么"、
  "你能选哪些库"）由拥有那个页面的业务端点连同业务数据一起下发结论，不要让前端自己拼。
- 上面这些都有用例钉住：侧栏分组骨架在 `default-sidebar.spec.ts`，头像菜单的构成（含"不放切换租户"）在 `user-menu.spec.ts`，后者按**完整序列**断言——多一项少一项都会红。改导航要同时改用例，避免"顺手挪一个入口"没人察觉。
- **页面标题只设 `LayoutService.title`**（面包屑末级）：每个布局页在构造函数或 `effect` 里设一次，本地化形态用 `translateSignal`，语言切换随之更新。浏览器标签页标题由根组件统一合成为「页面标题 · 应用名」（`Title` 服务），页面不要自己调 `Title`；页头随布局销毁时清空页面标题，登录页、落地页等不设标题的页因此只显示应用名。应用名是 `app.ts` 的 `APP_NAME`，本地化形态取词条 `app.name`、缺词条时回落到它；改项目名时两处连同 `index.html` 的 `<title>` 一起改。
- 设置页的面板（个人设置、系统设置各一套）走子路由，面板名进 URL：刷新、分享、头像菜单直达都落在同一面板。
- **布局按服务对象选**：管理平台面向内部员工，条目多、会增长、需要按权限整组裁剪，用侧栏（`DefaultLayout`）；工作空间面向业务用户，内容优先，用顶栏（`WorkspaceLayout`），「个人」组（`placement: 'end'`）靠右、以图标按钮呈现（与主题、通知、语言同排，名称放在提示与可访问名里），其余靠左、用文字链接。工作空间的主导航超过 7 项，或需要分组标题与按权限整组裁剪时，把路由换回 `DefaultLayout`——菜单定义不用动。

---

<!--#if (IncludeLocalization)-->

## 9. 多语言（i18n）

> 本项目已启用多语言（`--include-localization true`）。默认语言英语，支持 en + zh-CN 运行时切换。

### 9.1 方案与默认语言

- **运行时库 Transloco**（`@jsverse/transloco`）：JSON 词条运行时加载，用户即时切换语言、单包部署——**不用** Angular 编译期 `$localize`（那是按 locale 出多包、无法运行时切换）。
- **默认语言英语（`en`）**，支持 `en` + `zh-CN`；回落语言 `en`。
- 全局词条 `public/i18n/{en,zh-CN}.json` 只放跨功能文案（common、validation、layout、menu、app、httpError、theme、language、impersonation）。功能词条放在 `public/i18n/<scope>/{en,zh-CN}.json`，当前 scope 为 account、settings、users、roles、permissions、tenants、openApp、operationRecords、workspace、platform、landing、forbidden。
- scope 文件直接放该命名空间的内容，不重复包一层 scope 名；例如 `users/en.json` 的 `page.title` 对应运行时键 `users.page.title`。loader 按 `{baseHref}i18n/{scope}/{lang}.json` fetch，兼容子路径部署。

### 9.2 文案归属

| 类别                            | 归属                                          | 用法                                                                       |
| ------------------------------- | --------------------------------------------- | -------------------------------------------------------------------------- |
| UI 静态文案（菜单、按钮、标签） | 前端词条（权威）                              | 模板结构指令 `*transloco="let t"` 内 `{{ t('menu.users') }}`；TS 见 §9.4 |
| 业务错误消息                    | **后端资源**（权威，见 [`api.md`](./api.md)） | 前端直接显示后端已本地化的 `detail`，不在前端重复维护业务错误词条          |

- 后端按 `Accept-Language` 返回本地化消息；`http-error-interceptor` 归一化错误，由发起操作的 feature 展示。仅在需要差异化 UI 行为时按业务 `code` 分支。前端词条提供网络断开、后端不可达等客户端兜底。

### 9.3 关键接线（`core/`）

- `core/services/language-service.ts`：切换语言的唯一入口（`setDeviceLang` / `applyAccountLang` / `resetToDeviceLang`）。先加载全局及所有已访问 scope 的目标语言词条，成功后才激活 Transloco 并同步服务信号和 `<html lang>`；加载期间保留原语言，失败保持原语言并上报，连续切换只有最后一次生效。保留已访问 scope 是为了覆盖仍打开的弹窗，代价是后续切换也会加载离开页面的词条；不预载尚未访问的功能。失败后的再次选择会重试。返回的 Promise 等到加载落定且不 reject，启动流也等待它。设备偏好存 localStorage，账户偏好仅在内存生效。
- 同文件的 `provideLanguageFallbackStrategy()` 在官方策略扩展点抛出 `TranslationLoadError`，阻止 Transloco 8.4 的自动回落及失败后下一次 scope 成功时的自行激活；应用与测试共用这项装配。服务复用每条加载路径的 options，失败后清理 8.4 留下的回落计数，保证连续失败也能重试。`provideLanguageInitializer()` 在首帧前等待初始全局词条，失败退回英语，保留启动失败页。
- `core/i18n/translation-scopes.ts`：路由解析器从 `provideTranslocoScope` 读取完整登记（scope、alias、inline loader），组件创建前等待活动语言的词条；切换语言复用同一登记，加载期间语言变化会重新等待最终语言。alias 在合并词条前应用到 Transloco 映射，inline loader 先等待目标资源及配置的缺词回落资源，成功后通过官方 load options 传入，避免库缓存拒绝的 Promise；在途与成功资源复用，失败资源在显式重试时重新调用加载函数，不访问库的私有缓存。失败上报并取消导航，已有页面时保留原页面；首次深链没有原页面时，根组件复用启动失败卡片提供持久说明与重试，保留目标 URL（含查询与锚点），恢复后进入原目标，不创建缺词条的功能组件。重试单次导航使用 `onSameUrlNavigation: 'reload'`，使根地址 `/` 首次失败也能重新执行解析器，不改全局策略。scope 与语言切换重叠时，两边都会等待新增词条。
- `core/i18n/transloco-loader.ts`：加载全局 `{baseHref}i18n/{lang}.json` 与功能 `{baseHref}i18n/{scope}/{lang}.json`。生产 `postbuild` 递归展平整个 i18n 目录，配合 `flatten.aot`。
- `core/interceptors/accept-language-interceptor.ts`：注入 `Accept-Language` 头，**置拦截器链首位**（链定义在 `app.interceptors.ts`，应用与启动用例共用），使后端消息按当前语言返回。
- `app.config.ts`：`provideTransloco`（`defaultLang: 'en'`）+ `TranslocoHttpLoader` + `provideLanguageFallbackStrategy()`；单测装配 `core/i18n/transloco.testing.ts` 同样登记该策略。
- 语言选择器用 Spartan **dropdown-menu**（`hlmDropdownMenuTrigger` + `ng-template` 模板驱动菜单项，触发按钮用 `hlmBtn`，与铃铛/主题按钮风格一致），封装在 `shared/components/language-switcher`，挂在 `layout/components/default-header` 最右图标区（后台页在铃铛右侧）。

### 9.4 新增文案

- **词条随功能归属，这是业务项目持续扩展的长期约定。** 不按词条数量选择性拆分，每个独立功能都拥有自己的 scope；通用布局和真正跨功能的文案才放全局。权限授权弹窗属于 permissions 功能，即使被其它管理页嵌入，也由 permissions scope 持有词条，由宿主路由一起预加载。
- 先分析引用归属：只有单一功能使用的文案放该功能 scope，真正跨功能的文案留在全局。新的独立懒加载功能新建 scope，两语言文件同时维护；不要把同一文案复制到多个 scope。路由登记 `providers: [provideTranslocoScope('orders')]` 和 `resolve: { translations: resolveTranslationScopes }`，注册与预加载读取同一来源；嵌套功能在自己的路由登记 scope。
- UI 文案在对应的 en、zh-CN 文件同时加键。scope 键集合、两语言非空字符串及插值、占位符、静态引用、组件英文表与动态对象前缀由 i18n 闸门校验。嵌入组件的宿主关系按实际模板 selector 逐层追溯，从路由 loadComponent/component 进入，继承父路由及 loadChildren 的 scope 登记；每个入口都必须登记其整棵组件树需要的 scope。动态创建、非字面量路由或登记表达式不属于该静态判据，新用法需补充相应检查，不得绕过预加载约定。
- **模板文案用结构指令**：组件模板最外层包 `<ng-container *transloco="let t">`（组件 `imports` 引 `TranslocoDirective`），块内一律 `t('key', params)`。这是 Transloco 官方推荐的写法：一个模板只建一个订阅，`t` 带记忆化；切换语言时指令换掉 `t`、整个模板随之重绘（`reRenderOnLangChange: true`）。传给子组件的文案（如分页器的 `labels`）也在模板里用 `t` 组装。不要给模板写"返回译文的组件方法"——那需要自己读语言信号才会重绘，漏读一次整块就停在旧语言；按状态选文案时让模板写 `cond ? t('a') : t('b')`。
- 只使用一个功能命名空间的模板可写 `*transloco="let t; prefix: 'orders'"`，正文用 `t('title')`；`prefix` 是 8.4 的正式输入，`read` 已弃用。同时使用 common/validation 的模板保留完整键 `t('orders.title')` 与 `t('common.save')`。配置 `scopes.autoPrefixKeys: false`，避免 scope 注入后完整键被重复加前缀。关闭多语言时，外层裁成普通 ng-container，英文表使用与正文相同的键；使用 prefix 的模板英文表也必须用相对键。
- **TS 里随语言变化的文案**（导航菜单、筛选项、页面标题等由 `computed` / `effect` 产出、不经模板翻译的）一律用信号 API：固定的键用 `translateSignal('orders.title', {}, { scope: 'orders' })`（全局键用 `translateSignal('common.save')`）（在字段或构造函数里建，`computed` 里读）；一组键或键随数据变化（类别、动作码）用 `translateObjectSignal('orders.status', {}, { scope: 'orders' })` 取成对象再索引（显式 scope 负责加载，键仍用完整前缀），带点号的键按 `core/i18n/translation-text.ts` 的 `textAt` 逐级取。词条未到时前者是空串、后者是空对象，到达后自行更新。**不要**在 `computed` / `effect` 里「读 `activeLang()` + 同步 `translate()`」：要自己追踪语言，漏读一处就停在旧语言；首帧前、词条未到时取到的裸键还会被缓存。参数随每行数据变化的句子（`{{target}}` 之类）在 TS 里只定键与参数，由模板 `t(key, params)` 取。
- **事件发生时的一次性文案**（toast、确认框）直接 `transloco.translate()`：`LanguageService` 先加载全局与已访问 scope 再激活语言，路由在创建组件前预加载 scope，活动功能的词条总已就位，切换途中返回的请求取到的是原语言的真实文案；这类文案不需要随语言重算。要留在界面上的文案（写进 signal、随后渲染的错误说明）存键，由模板 `t` 取，否则切换语言时它停在旧语言。
- 不要直接调 `transloco.setActiveLang()` 切换语言——绕过 `LanguageService` 就没有设备偏好、`<html lang>` 与加载失败的退回。
- 业务错误文案：改后端资源（见 `api.md` §异常本地化），**不在前端加**。
- 货币/数字用 Angular `CurrencyPipe`/`DecimalPipe`。注意 **`LOCALE_ID` 是启动期注入、不随运行时语言切换自动改变**；如需格式也跟随切换，需自行传 locale 参数或重建相关视图，别假设它会自动联动。
<!--#else-->
## 9. 界面文案

- 组件模板里的界面文案写成 `t('模块.语义')`，解析到组件的 `protected readonly t = englishText(ENGLISH)`：按组件文件末尾的英文表 `ENGLISH` 取值，`{{name}}` 占位按参数替换（`shared/utils/english-text.ts`）。新增文案在同一组件的表里加一条键；表里没有的键会原样显示成键名。
- 组件 TS 里的提示文案（toast、确认框）直接写英文，或经同一个 `t` 取表里的键。
<!--#endif-->

---
