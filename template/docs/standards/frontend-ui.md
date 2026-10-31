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
- 可访问性：可键盘操作，颜色不是唯一信息；字段关联错误；系统减少动效时禁位移/缩放，保留颜色与焦点反馈（见[Spartan 维护](./frontend-spartan.md)）。触屏目标至少 44px：组件与 styles.css 已覆盖常规组件，页面不重复补；自写目标加 pointer-coarse:min-h-11，必要时补宽。复选框/开关/单选保留视觉尺寸，用粗指针伪元素扩展命中区；使用方在滚动容器内部留出各轴伸出量（16px 复选框每侧 14px，开关按实际尺寸），避免 overflow 裁切或邻项重叠，外层内边距不能代替。表格选择列由 styles.css 补末端和高度，行首内边距与行高由页面保证；背景空白不计入目标。用 coarse-pointer.testing.ts 的 expectTouchTarget 量尺寸、tapAt 在外沿真实点按验证。
- 响应式同时复核短高度（390×667）、长内容、滚动边界与软键盘；宽度合格仍须验证操作可达。

## 2. 组件与样式

### 2.1 组件库优先

- 先查本地 `frontend/libs/ui` 的现成组件与锁定版本；新增组件或核对 API 时再查项目内 `spartan` Skill 与匹配版本的 [Spartan UI 官方文档](https://spartan.ng/)
- 尽量沿用 Spartan 的默认组件与风格（`hlm*` 指令 / `hlm-*` 组件）
- 仅在无法满足需求时才可创建自定义组件，不在 `shared` 重建组件库

### 2.2 样式方案

- **必须** 优先使用 Tailwind CSS v4 的原子类进行布局和微调
- 任何自定义样式都**必须**与 Spartan UI 的主题风格保持一致（基于 Spartan 主题的 CSS 变量与 `.dark` class）
- 所有页面一律使用语义化主题变量，不写具体色值（`blue-*`、hex 等）。换品牌改 `frontend/src/styles.css` 里的令牌：品牌色亮暗成对改（同一色相，亮色 L≈0.52、暗色 L≈0.72），中性色带一点跟随品牌色相的彩度。`--input`（输入框、复选框、描边按钮的边界）与 `--border`（装饰分隔线）分开调，前者深一档、带品牌色相，看得出轮廓又不压过内容，聚焦由 `--ring` 强指示；分段控件（ToggleGroup 默认变体）用灰色轨道与浮起的选中项（底色 + 轻阴影 + 文字色），与 Spartan Tabs 同一写法，不加深色描边，规则集中在 `styles.css` 按 `data-slot` 书写
  - 例外只有三类：图像遮罩及其上文字（`bg-black/25 text-white`）、二维码底色（`bg-white`，扫码需浅底）、第三方品牌标识 SVG 的官方色值
- 居中或带 `text-balance` 的标题与导语加 `break-keep wrap-anywhere`：中文优先在标点和空格处换行，超长连续文本才在内部断开以免溢出；正文段落不加
<!--#if (LocalIdentity)-->
- 登录、注册、强制启用两步验证共用 `frontend/src/app/features/account/components/auth-shell`：五五分栏，左栏是独立贴边的品牌面板（固定深色的 `hero-*` 表面，亮暗主题同色；标识，居中的两行展示标题、导语与三条不随参数变化的亮点，底部一行信任语），右栏是主题/语言切换与居中的表单（不再套卡片）；lg 以下品牌面板隐藏，标识移到表单栏顶部。这几页在一次登录里连续经过，外观必须一致
<!--#endif-->

### 2.3 有限语义色板

- 状态/分类标签**只用项目约定的有限几个语义色**，不随意扩色。
- **分类标签用中性色，不用告警色表达"非告警"的分类**（不要用 warn 橙表达一个普通分类）。
- **状态徽章前带圆点，分类标签不带**：启用 / 停用、已锁定、邮箱未验证、成功 / 被拒绝、当前设备这类状态在 `hlmBadge` 上加 `appStatusDot`（`shared/directives/status-dot`），圆点取徽章文字色；角色名、客户端类型、"内置"这类分类不加。

> `例`：用 Spartan badge 的有限变体 `default` / `secondary` / `destructive` / `success` / `warning` / `outline`（`hlmBadge`）；分类标签一律用中性变体（`secondary`）；`success` 只用于启用、已验证、成功这类正向状态。

### 2.4 组件设计

- **单一职责**：组件不混合业务流程、请求与展示
- **数据流**: 遵循"单向数据流"原则（`[input]` 向下, `(output)` 向上）
- **变更检测**：共享与展示型组件使用 `ChangeDetectionStrategy.OnPush`

### 弹窗生命周期

业务弹窗静态关闭 Brain 自动退出并隐藏内置关闭按钮，外点/遮罩无操作；core/components/dialog-close 的 requestClose 统一处理 Esc/关闭/取消。脏表单或集合变化由 ConfirmService 确认继续编辑/放弃，否则直接关。保存中锁定用户退出；成功/销毁走框架关闭。凭据不确认，关即清空。

同步聚焦主输入；异步先聚焦标题，就绪且焦点仍在标题才移入输入。关闭返回触发入口，验证菜单销毁/弹窗接力。限制可视高度，正文 min-h-0 独立滚动、不穿透背景，操作区不收缩。

### 2.5 图标 / 文案跨页一致

- 跨页的图标尺寸、文案措辞要统一；**对齐用实测（浏览器/像素）确认，不靠目测**。
- 图标使用 `@ng-icons` + lucide：模板写 `<ng-icon name="lucideXxx">`，并在组件级用 `provideIcons({ lucideXxx })` 按需注册（不全局注册全部图标）。
<!--#if (IncludeNotifications)-->
- 通知图标从 `frontend/src/app/core/services/notification-service.ts` 的映射派生 `NOTIFICATION_ICONS`，使用方整组注册。
<!--#endif-->

### 2.6 视觉规范

字号按**层级**用，每一档只有一个职责，不出现档外值（`text-[10px]` 之类）。正文基准是 14px——按钮、输入框、表格、卡片描述都是这个字号，这是组件已经定下的。

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
<!--#if (!IncludeLocalization)-->

## 3. 界面文案

- 组件模板里的界面文案写成 `t('模块.语义')`，解析到组件的 `protected readonly t = englishText(ENGLISH)`：按组件文件末尾的英文表 `ENGLISH` 取值，`{{name}}` 占位按参数替换（`frontend/src/app/shared/utils/english-text.ts`）。新增文案在同一组件的表里加一条键；表里没有的键会原样显示成键名。
- 组件 TS 里的提示文案（toast、确认框）直接写英文，或经同一个 `t` 取表里的键。
<!--#endif-->
