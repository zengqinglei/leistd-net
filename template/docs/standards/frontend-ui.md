# 前端界面规范

编码见[前端](./coding-frontend.md)；专题：[列表](./frontend-lists.md)、[表单](./frontend-forms.md)、[导航](./frontend-navigation.md)、[路由](./frontend-routing.md)、[数据](./frontend-data.md)、[设置](./settings.md#3-设置页)。

## 1. 设计原则

- 动作、状态与辅助信息按用户任务分层。
- **界面稳定**：整页离开前保持外观；导航成功时一次应用偏好；静态 HTML 决定首帧；晚到内容等高占位，二维码预留尺寸。
- 区分加载/失败/空态；失败说明原因并可重试，刷新失败保留原内容并提示。加载态跟随当前请求（翻页、改筛选取消旧请求）。
- **标签不折行，内容可以折行**：导航、按钮、徽章、表头、快捷入口，以及固定文案的短标题（卡片与面板标题、指标与待办事项名称、指标卡的对比说明）属于标签，写短、单行（`whitespace-nowrap` 或 `truncate`）；截断会丢信息时，完整说法放进可聚焦元素的 Tooltip（截断才提示用 `appTruncatedTooltip`），数量等关键数据单独显示、不随名称截断。正文、字段说明、操作记录的句子，以及由用户或业务数据生成的标题（通知标题、业务对象名称）属于内容，允许折行，摘要处可限定行数（如两行）并给出展开入口。
<!--#if (IncludeLocalization)-->
- **按代表语言组复核**：改动界面后在 390、768、1024、1440 宽，每种书写系统（拉丁、CJK 等）取最宽的语言各看一遍。更长的语言由[伪语言](./frontend-i18n.md#5-伪语言与加语言)代表：宽度受限的区域新增或改动时写伪语言 spec，受压时单行截断（丢信息的给全文入口）或收进「更多」，不撑出横向滚动。短文本折成多行是复核线索，按上面的分类判断，不是一律算错。
<!--#else-->
- 改动界面后在 390、768、1024、1440 宽各看一遍：短文本折成多行是复核线索，按上面的分类判断，不是一律算错。
<!--#endif-->
- 可访问性：交互元素可键盘操作，颜色不是唯一信息，错误与字段关联。减少动效时禁位移/缩放，保留颜色与焦点反馈（见[Spartan 维护](./frontend-spartan.md)）。触屏目标至少44px：Spartan 按钮（除 `xs`）、输入、菜单项、选项、导航链接、侧栏按钮、分段项和验证码格已在组件或 `styles.css` 补足；自写目标用 `pointer-coarse:min-h-11`，必要时补宽。复选框/开关/单选保留视觉尺寸，粗指针伪元素扩为44×44；在滚动容器内部按各轴伸出量留空间（16px复选框每侧14px，开关按尺寸），外层内边距不能防止 `overflow-*` 裁切。表格选择列的末端及表头高度由 `styles.css` 保证，行首内边距与行高由页面保证；背景空白不计入目标，自定义布局仍要验证裁切与邻项。用 `coarse-pointer.testing.ts` 的 `expectTouchTarget` 量尺寸、`tapAt` 在外沿真实点按。
- 响应式同时复核短高度（390×667）、长内容、滚动边界与软键盘；宽度合格仍须验证操作可达。

## 2. 组件与样式

### 2.1 组件库优先

先查 `frontend/libs/ui` 与锁定版本，沿用 Spartan 默认组件/风格（`hlm*` / `hlm-*`）。新增或核对 API 再读项目 `spartan` Skill 和[匹配版本文档](https://spartan.ng/)。现有组件无法满足时才自建，不在 `shared` 重建组件库。

### 2.2 样式方案

- **必须** 优先使用 Tailwind CSS v4 的原子类进行布局和微调
- 任何自定义样式都**必须**与 Spartan UI 的主题风格保持一致（基于 Spartan 主题的 CSS 变量与 `.dark` class）
- 页面只用语义令牌，不写 `blue-*`、hex 等色值。换品牌改 `frontend/src/styles.css`：亮暗同色相、中性略带品牌彩度；`--input` 控件边界比装饰线 `--border` 深，聚焦由 `--ring` 强调。ToggleGroup 默认变体与 Tabs 同用灰轨道、浮起选中项（底色/轻阴影/文字色），不用深色描边；统一按 `data-slot` 写在样式表，数值与对比度依据见其注释。
  - 例外只有三类：图像遮罩及其上文字（`bg-black/25 text-white`）、二维码底色（`bg-white`，扫码需浅底）、第三方品牌标识 SVG 的官方色值
- 居中或带 `text-balance` 的标题与导语加 `break-keep wrap-anywhere`：中文优先在标点和空格处换行，超长连续文本才在内部断开以免溢出；正文段落不加
<!--#if (LocalIdentity)-->
- 登录、注册、强制两步验证共用 `frontend/src/app/features/account/components/auth-shell`，同次登录外观一致。五五分栏：左栏贴边，固定深色 `hero-*`（亮暗同色），有标识、居中两行标题、导语、三条不随参数变化的亮点及底部信任语；右栏放主题/语言开关与居中表单，不套卡片。lg 以下隐藏品牌面板，标识移到表单栏顶部。
<!--#endif-->

### 2.3 有限语义色板

状态/分类仅用 `hlmBadge` 的有限变体：`default`、`secondary`、`destructive`、`success`、`warning`、`outline`。分类只用中性的 `secondary`，不用告警色；`success` 用于启用、已验证、成功。状态（含锁定、邮箱未验证、被拒、当前设备）加 `appStatusDot`，圆点跟随文字色；角色、客户端类型、“内置”等分类不加。

### 2.4 组件设计

- **单一职责**：组件不混合业务流程、请求与展示
- **数据流**: 遵循"单向数据流"原则（`[input]` 向下, `(output)` 向上）
- **变更检测**：共享与展示型组件使用 `ChangeDetectionStrategy.OnPush`

### 2.5 图标 / 文案跨页一致

- 跨页的图标尺寸、文案措辞要统一；**对齐用实测（浏览器/像素）确认，不靠目测**。
- 图标使用 `@ng-icons` + lucide：模板写 `<ng-icon name="lucideXxx">`，并在组件级用 `provideIcons({ lucideXxx })` 按需注册（不全局注册全部图标）。
<!--#if (IncludeNotifications)-->
- 通知图标从 `frontend/src/app/core/services/notification-service.ts` 的映射派生 `NOTIFICATION_ICONS`，使用方整组注册。
<!--#endif-->

### 2.6 视觉规范

字号按**层级**用，每一档只有一个职责，不出现档外值（`text-[10px]` 之类）。正文基准是 14px——按钮、输入框、表格、卡片描述都是这个字号。

| 档位 | 用途 | 写法 |
| --- | --- | --- |
| 24px | 页面标题（有则一个） | `text-2xl font-semibold tracking-tight` |
| 16px | 面板标题、卡片标题 | `text-base font-medium` |
| 14px | 分区标题 | `text-sm font-semibold` |
| 14px | 正文、表格、按钮、输入 | `text-sm` |
| 12px | 辅助说明、时间戳、徽章 | `text-xs` |
| 30px | 仅仪表盘大数字 | `text-3xl` + `tabular-nums` |

- 营销页（着陆页、认证页品牌面板）可以用更大的展示字号（`text-4xl` 到 `text-6xl` 的 Tailwind 档位，仍不写任意值）、光晕、同心细环与渐变字；这些装饰只限营销页，应用内页面不用。唯一例外是用户工作台的欢迎区：问候与搜索背后一层顶部居中的主色淡光晕（约 12%），数据区仍按普通表面规则。
- 页面不必都有可见标题：列表页可直接从工具栏开始（面包屑与标签页已说明位置）；有页面标题时用 24px 一档，一页一个。
- 字重只用 400 / 500 / 600，不用 `font-bold` / `font-extrabold`。
- 16px 一档即 card、dialog、sheet、alert-dialog 标题组件的默认样式，不再手加 `font-semibold`。
- 加在图标、`hlm-spinner` 上的 `text-*` 是图标尺寸，不受上表约束。
- 输入框的 `text-base md:text-sm` 不要改：iOS Safari 遇到小于 16px 的输入框会在聚焦时放大页面。

圆角三档，由元素的角色决定：

| 档位 | 用于 |
| --- | --- |
| `rounded-xl` | 页面级容器：卡片、对话框、表格外框 |
| `rounded-md` | 控件（按钮、输入框，组件已是）；嵌在卡片或对话框里的块、代码框、行项、图标块 |
| `rounded-full` | 徽章、头像 |

嵌套的块用 `md` 不用 `xl`：内外同圆角就分不出层次。组件内部的小元素（菜单项、清除按钮）跟随所在组件的写法。

- **当前项用中性色，不用品牌色**：侧栏菜单沿用 Spartan 的 `data-active` 样式（`sidebar-accent` 底），细描边由 `styles.css` 统一补；顶栏导航沿用 navigation-menu 的默认当前态；抽屉与设置面板里的 ghost 按钮挂 `aria-[current=page]:bg-muted`。品牌色留给主操作、焦点环与链接。
- **间距**：兄弟元素之间用 `flex` / `grid` + `gap-*`，不用 `space-y-*` 与逐个元素的外边距。页面外框统一 `p-4 sm:p-6`。
- **暗色**：表面分五层逐级提亮（`sidebar` < `background` < `card` < `popover` < `muted`），不用纯黑；层级都在 `styles.css` 里定好，页面不写 `dark:` 颜色覆盖。
- **图标 + 文案的空态一律用 flex 列**（`flex flex-col items-center justify-center gap-2`），不靠给 `ng-icon` 加 `block`：它的 host 样式按行内盒渲染，`block` 压不住，图标与文字会挤在一行、基线错开。同款写法见通知面板的空态。

### 2.7 弹窗生命周期

业务弹窗打开前静态 `disableClose`、禁外点并隐藏内置关闭钮。`app-dialog-close` 的 `requestClose()` 统一处理 Esc、关闭与取消；dirty/集合变化时确认放弃，busy 时拒绝用户退出。成功、父级强制关闭和销毁直接关闭；凭据不确认，关闭即清空。

同步内容聚焦主输入；异步先聚焦标题，`appDialogFocus` 仅在焦点仍留在标题时迁入输入。关闭应返回有意义的业务入口，须验证菜单项销毁及弹窗接力时的返回焦点。正文标 `data-dialog-body`，通用样式限制可视高度并管理独立滚动，页脚留在正文外且不收缩；不要叠加滚动区或用负边距伸出表单。

<!--#if (!IncludeLocalization)-->

## 3. 界面文案

- 组件模板里的界面文案写成 `t('模块.语义')`，解析到组件的 `protected readonly t = englishText(ENGLISH)`：按组件文件末尾的英文表 `ENGLISH` 取值，`{{name}}` 占位按参数替换（`frontend/src/app/shared/utils/english-text.ts`）。新增文案在同一组件的表里加一条键；表里没有的键会原样显示成键名。
- 组件 TS 里的提示文案（toast、确认框）直接写英文，或经同一个 `t` 取表里的键。
<!--#endif-->
