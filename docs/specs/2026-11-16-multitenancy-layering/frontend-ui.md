# 工作区形态前端实现要点（方案 A）

本文把[设计稿](./design.html)第 3 节的方案 A 落到模板现有组件与令牌上，供实施方逐项对照。路径相对 `template/frontend/src/app/`。

优先级：`template/docs/standards/frontend-*.md` > 本文 > 设计稿示意。设计稿是近似绘制，尺寸、颜色、间距一律以本文列出的现有组件写法为准；本文没写到的，照"同类现有页面"的写法，不自创样式。

## 1. 令牌与通用约束

| 类别 | 规则 | 依据 |
| --- | --- | --- |
| 颜色 | 只用语义令牌类（`bg-primary`、`text-muted-foreground`、`bg-muted`、`border-border` 等），不写色值、不写 `text-[#...]` | `frontend-ui.md` §2 |
| 徽章 | 「平台」是分类：`hlmBadge variant="secondary"`，不加 `appStatusDot`。角色徽章沿用 `outline`（`user-table.ts` 的 `getRoleVariant()`）。状态：启用 `variant="success"`、停用 `variant="secondary"`，都加 `appStatusDot` | `frontend-ui.md` §2.3；`user-table.html` 的 `status` 列 |
| 字号 | 正文与表格 `text-sm`；次要信息、用户名、时间 `text-xs text-muted-foreground`；弹窗与抽屉标题用 `hlmDialogTitle` / `hlmSheetTitle` 自带样式；不用档外字号 | `frontend-ui.md` §2.6 |
| 间距 | 页面滚动列 `gap-6 p-4 sm:p-6 *:shrink-0`；工具栏外层 `flex flex-wrap items-center justify-between gap-x-4 gap-y-3`，左组 `flex min-w-0 flex-auto flex-wrap items-center gap-3`，右组 `ml-auto flex w-full flex-wrap items-center justify-end gap-3 sm:w-auto`，窄屏按钮 `flex-1 sm:flex-none` | `features/platform/components/tenants/tenants.html` |
| 表格 | TanStack + `app-table-loading`（传真实行高）+ `[appTableFit]`；表头 `bg-muted`，单元格 Spartan 默认密度，首列 `first:ps-4`；主列 `TITLE_COLUMN_META` / `TITLE_CONTENT_CLASS`；被裁列走行展开 | `frontend-lists.md` §2；`tenant-table.html` |
| 时间 | `appDate` 的 `short`，完整时刻放可聚焦元素的 Tooltip | `frontend-lists.md` §2 |
| 图标 | `<ng-icon name="lucideXxx">` + 组件级 `provideIcons`；行内图标按钮 `hlmBtn variant="ghost" size="icon-sm"` + `hlmTooltip` + `aria-label` | `frontend-ui.md` §2.5 |
| 空、错、载 | 区分"暂无数据""无匹配""读取失败（可重试）"，用 `hlmEmpty` 系列 | `frontend-ui.md` §1；`dashboard.html` 的 `hlmEmpty` |
| 词条 | 中英文同时加；英文句首大写；键按区命名：`workspace.*`（切换器、零权限）、`tenants.staff.*`（抽屉、弹窗）、`users.platformBadge` 等 | `frontend-i18n.md` |
| 响应式 | 复核 390、768、1024、1440 宽与长译文 | `frontend-ui.md` §1 |

## 2. 各界面的组件对照

### 2.1 侧栏头部切换器（扩展现有区域切换器）

- 位置：`layout/components/default-sidebar/default-sidebar.html` 的 `hlmSidebarHeader` 区域切换器，不另建组件、不放页头。
- 触发按钮不变：`hlmSidebarMenuButton size="lg"`、`app-logo`、两行文字（`text-sm font-medium` 应用名 + `text-xs text-muted-foreground` 副标题）、`lucideChevronsUpDown`。副标题在工作区内为「acme · 管理平台」，在控制面为「平台控制台 · 管理平台」。
- 菜单：同一个 `hlm-dropdown-menu class="min-w-56"`，分两组：
  1. `hlm-dropdown-menu-label`「工作区」：首项「平台控制台」（`lucideShieldCheck`），其后是可进入的租户；
  2. 分隔线后 `hlm-dropdown-menu-label`「区域」：现有的管理平台 / 工作空间两项。
- 菜单项沿用现有区域项的写法：`hlmDropdownMenuItem class="gap-2 p-2"`、带边框的图标方块（租户用名称首字母）、当前项打勾。
- 租户超过 7 个时，在"工作区"组顶部放一个 `hlmInput` 过滤框（`h-8`），列表限高滚动。只有一个工作区时不列租户，只显示当前项。
- 弹出方向、窄屏不关抽屉沿用现有写法（`areaMenuSide()`、`[closeMobileSidebarOnClick]="false"`）。
- 选择工作区只做路由跳转：`/w/<旧>/X` → `/w/<新>/X`，目标路由不存在时退到 `/w/<新>/platform`。
- 顶栏布局（`WorkspaceLayout`）的切换器放在导航条起始处，窄屏随导航收进抽屉（`frontend-navigation.md` §2）。

### 2.2 租户列表行菜单「平台人员」

- `features/platform/components/tenants/widgets/tenant-table/tenant-table.html` 的 `rowMenu` 里加一项「平台人员」（`lucideUsers`），放在「停用」之前、分隔线之前；按 `App.Tenants.StaffAccess` 显示。
- 点击写 query `?staff=<tenantId>` 打开抽屉；抽屉开关以 query 为唯一来源，写法照 `webhooks.ts` 的 `delivery` 参数。

### 2.3 平台人员抽屉

- 骨架照 `features/platform/components/webhooks/widgets/webhook-delivery-sheet/webhook-delivery-sheet.html`：`hlm-sheet side="right"`，`hlm-sheet-content class="data-[side=right]:w-full data-[side=right]:sm:max-w-2xl"`，`[closeLabel]`。
- 头部：`hlmSheetTitle`「acme 的平台人员」；`hlmSheetDescription` 一句说明（显示名 · "这些宿主用户可以进入该租户，以所列角色操作"）。
- 主体：`flex flex-col gap-4`。
  - 工具栏：左侧搜索 `hlm-input-group`（照 `tenants.html`），右侧主按钮「授予访问」（`hlmBtn variant="default"` + `lucidePlus`）。
  - 表格列：人员（主列，两行：显示名 `text-sm font-medium` + 用户名 `text-xs text-muted-foreground`）、角色（`app-readonly-list-popover` + `outline` 角色徽章，照 `user-table.html` 的 `roles` 列）、状态（启用/停用徽章）、授予时间（`appDate short`）、操作。
  - 操作列：行内「修改角色」（`lucidePencil`）；`…` 溢出菜单里分隔线后是「移除访问」（`variant="destructive"`）；窄屏全部收进 `…`。
- 列表数量少，不分页；搜索在前端过滤。

### 2.4 授予访问弹窗

- 骨架照 `features/platform/components/users/widgets/user-roles-dialog/`：`hlm-dialog` + `hlm-dialog-content`（宽度与该弹窗一致）、`hlm-dialog-header`（`hlmDialogTitle`「授予进入 acme」、`hlmDialogDescription`）、`hlm-dialog-footer`（取消 `outline`、提交 `default`，提交中显示 `hlm-spinner`）。
- 字段一「平台人员」：内嵌 `hlm-command` 列表（写法参照 `layout/components/command-palette/`），顶部是搜索输入，选项为头像（`hlm-avatar` 首字母）+ 显示名 + `text-xs text-muted-foreground font-mono` 用户名；已授予的人置灰并在右侧写「已授予」（`text-xs text-muted-foreground`）。字段描述说明关联账号的用户名为 `platform.<用户名>`。
- 字段二「acme 的角色」：勾选列表沿用 `user-roles-dialog` 的行写法（`flex items-center gap-3 rounded-md px-2 py-2 text-sm hover:bg-muted/50 pointer-coarse:ps-3.5` + `hlm-checkbox`），至少选一个；内置角色右侧 `text-xs text-muted-foreground`「内置」。
- 字段标签与描述沿用 `frontend-forms.md` 的字段写法；用户名冲突等服务端字段错误显示在对应字段下。
- 成功后关闭弹窗、toast 提示并刷新抽屉列表；失败沿用现有错误处理。

### 2.5 修改角色与移除访问

- 修改角色：与 2.4 同一弹窗的"只有角色字段"形态，或直接复用 `user-roles-dialog` 的展示结构；不另做第三种写法。
- 移除访问：`ConfirmService.open({ owner, action, header, message, confirmText, variant: 'destructive' })`，写法照 `tenants.ts` 的启停确认。确认文案写清"该人员将无法再进入 acme；其在 acme 的操作记录保留"。

### 2.6 租户侧用户列表的「平台」标记

- `user-table.html` 的 `username` 主列：显示名一行改为 `flex min-w-0 items-center gap-1.5`，显示名 `truncate`，其后 `<span hlmBadge variant="secondary" class="shrink-0">平台</span>`；第二行仍是用户名（`platform.xxx`）。
- 关联账号行：最近登录显示「-」；行内不显示编辑；溢出菜单只保留「修改角色」「启用/停用」；重置口令、重置两步验证、删除不显示。
- 溢出菜单底部加一行原因说明（`p class="px-1.5 pb-1 text-xs text-muted-foreground"`，照本人行的原因写法）：「平台人员的口令与登录方式由本人账号管理；移除访问在宿主的租户管理中操作。」
- 行展开详情同样显示该标记。

### 2.7 零权限工作区

- 放在工作区布局的内容区（保留侧栏、页头与切换器），用 `hlmEmpty`：`hlmEmptyMedia variant="icon"` + `lucideLock`、`hlmEmptyTitle`「你在 globex 还没有可用的功能」、`hlmEmptyDescription` 说明原因、`hlmEmptyContent` 两个按钮（「切换工作区」`outline`，打开切换器；「回到平台控制台」`secondary`）。
- 不跳 403 页。

## 3. 状态与交互约定

- 路由：工作区形态下 `/platform/**` 只是宿主控制面；租户用户与平台人员的租户内页面都在 `/w/:workspace/**`。租户用户的切换器只显示自己的工作区，不展开列表；打开 `/platform/**` 时转到 `/w/<自己的租户>/...`。
- 实时连接：平台人员在工作区内两条（本人常驻 + 工作区随切换重建），通知中心按当前范围显示；其他情况一条。遇到 403 `Workspace:Unavailable` 时刷新工作区列表并退到下一个可用工作区，没有时回 `/platform`。

- 请求范围：拦截器读 `HttpContextToken`；默认取当前路由的 `:workspace` 参数；`/platform` 与本人账户请求显式标为本人。
- 守卫顺序：导航开始时确立目标工作区；路由守卫判定前加载该工作区的权限与设置；旧工作区的迟到响应丢弃（复用 `SessionContextService` 与 SignalR 的代次）。
- 切换时父组件按工作区键重建子路由出口，各页沿用自己的首屏骨架，不额外加全屏加载层。
- `AuthService.currentUser` 只表示本人；头像菜单、个人设置照旧显示本人。

## 4. Mock 与测试

- Mock：补齐工作区列表、平台人员列表、授予、改角色、移除、关联账号字段的数据与处理器（`check-template-mock-coverage.py` 会查）。
- 组件测试：`default-sidebar.spec.ts` 更新切换器构成（按完整序列断言）；平台人员抽屉、授予弹窗、用户表格「平台」标记、零权限外壳各有 spec；有排序列的表格调用 `expectAriaSortOnSortedHeaderOnly`。
- 视觉自检：提交前在 Mock 站点按 1440 与 390 宽，截切换器展开、平台人员抽屉、授予弹窗、租户用户列表、零权限外壳五张图，放 `.tmp/multitenancy-layering/screens/`，供 Claude 与设计稿逐项对照。
