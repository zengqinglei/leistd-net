# 外壳导航重组与系统切换

状态：进行中 · 风险档：高 · 关联：方案对比页 [design.html](2026-11-16-shell-navigation-system-switcher/design.html)

## 1. 需求

### 问题与背景

多租户工作区形态（[2026-11-16-multitenancy-layering.md](2026-11-16-multitenancy-layering.md)）在 worker-01 三服务演示站走查时，用户反馈外壳导航混乱：

- 侧栏品牌块的同一个下拉里放了三组内容：工作区列表、平台控制台、工作空间/管理平台。用户要先分清"区域"和"工作区"才能使用。
- 模板没有系统切换。Identity 与各 Resource 服务之间只能手动改地址，但这些系统已经在 Identity 的开放应用里登记过。
- 分布式事件页让宿主选择"宿主 / 默认库"，暴露的是部署时的分库方式。
- 工作空间（业务区，面向客户）的顶栏可以切换租户，和"租户由入口确定"相矛盾。

### 目标

从上到下按 系统 → 租户 → 菜单 分层，每个控件只管一件事：

- 品牌块只切换系统。
- 租户在侧栏顶部选择。
- 平台控制台从侧栏底部进入。
- 区域与个人入口放在右上角的头像菜单。

系统列表直接取自开放应用登记，不另建系统目录。

### 非目标

- 不引入业务 Identity 的独立系统入口目录、租户子域地址模板、按租户开通系统、平台人员按系统授权。
- 不在系统列表上预判用户在各系统的权限。进入系统后由各系统自己判断权限。
- 不改顶栏布局（工作空间）的导航结构，只去掉租户相关元素。

### 功能要求与验收标准

术语：平台人员 = 工作区形态下没有 tenantId、可进入多个租户工作区的账号；租户用户 = 带 tenantId 的账号。

**系统切换**

- AC1 Given 开放应用里有浏览器客户端，填了首页地址并勾选"显示在系统切换中"；When 已登录用户打开 Identity 或 Resource 的侧栏品牌块；Then 下拉列出 Identity（排第一）和这些客户端，当前系统打勾，名称用开放应用的多语言显示名。（前端 spec；OIDC e2e 脚本）
- AC2 Given 只有 Identity 一个系统，或是 Standalone 形态；When 点击品牌块；Then 回到当前范围的首页，没有下拉。（前端 spec）
- AC3 Given 平台人员在 `/w/tenant-a/...`；When 在系统切换里选另一个系统；Then 跳到该系统首页地址下的 `/w/tenant-a/platform`。其他情况跳到首页地址本身。（前端 spec；浏览器 e2e）
- AC4 Given 首页地址不是绝对的 https 地址（开发环境允许 http 回环地址）；When 保存开放应用；Then 保存被拒，提示字段错误。没有勾选显示时，首页地址可以为空。（后端测试）
- AC5 Given Resource 配了 `Leistd:ServiceClients:Identity`，且服务客户端授予了 `system-directory.read`；When 前端请求本服务的系统列表；Then 后端以服务身份从 Identity 取回列表，短时缓存后返回。漏授权限时返回空列表并记 Error，品牌块退化为链接。（后端集成测试；OIDC e2e 脚本）
- AC6 Given 系统数量多于一个；When 打开命令面板；Then 有"切换系统"分组，行为与品牌块一致。（前端 spec）

**租户选择与平台控制台**（仅工作区形态）

- AC7 Given 平台人员可进入的租户工作区多于一个；When 在管理平台；Then 侧栏顶部显示可搜索的租户选择，选中后进入该租户的同一页面（沿用现有 switchTo 规则）。租户用户、非多租户形态、只有一个工作区时不显示。（前端 spec）
- AC8 Given 平台人员能访问平台控制台；When 在某个租户的管理平台；Then 侧栏底部显示"平台控制台"。进入控制台后，底部变成"返回 tenant-a"（上次的租户，没有则取第一个），租户选择隐藏。（前端 spec；浏览器 e2e）
- AC9 Given 任何形态；When 查看外壳；Then 品牌块的下拉里不再有工作区列表和区域切换。（前端 spec）

**页头与头像**

- AC10 Given 侧栏布局；When 宽度足够；Then 搜索在页头居中（⌘K）；窄页头收成图标按钮；侧栏顶部不再有搜索框。（前端 spec）
- AC11 Given 任何布局；When 查看页头；Then 头像在最右侧，菜单依次为：本人信息、区域入口（"工作空间"或"管理平台"，只出现当前不在的那个，进管理平台需要权限）、个人设置、退出。侧栏底部不再有用户信息。（前端 spec）
- AC12 Given 平台人员在某租户的工作空间；When 打开头像菜单；Then 顶部写"<用户名> · 代表 <租户>"，"管理平台"回到该租户的管理平台。（前端 spec）

**工作空间**

- AC13 Given 工作空间（顶栏布局）；When 客户或平台人员查看；Then 顶栏不出现租户切换，也不出现租户标识。（前端 spec）

**分布式事件**

- AC14 Given 租户范围（租户用户，或工作区里的平台人员）；When 打开分布式事件页；Then 没有库或租户筛选，后端按当前租户自动定位库。（前端 spec；框架测试）
- AC15 Given 平台控制台；When 打开分布式事件页；Then 显示可搜索的"租户"筛选，默认"全部"。选中租户时，后端定位该租户的库（共享库或独立库）并按租户过滤；"全部"时按时间合并各库的记录，统计按各库求和。仅宿主可用的操作（发送测试事件、拒收记录）保持不变。（框架测试：共享库与独立库各一；前端 spec）

**通用**

- AC16 Given 1440 与 390 两种宽度；When 走查上述场景；Then 版式符合方案页，手机端侧栏抽屉里的下拉不会关闭抽屉。（人工验收：实测对比页）

### 非功能要求

- 新词条中英文都要补；关闭本地化形态的英文表同步。
- 首页地址存在 OpenIddict 应用的扩展属性 `Properties` 里，不新增表，不需要迁移。
- 分布式事件管理接口的参数变化属于框架公共 API 变更，按版本规范随 develop 发 beta。

### 已确认的决策

- 2026-11-16 用户确认全部六条反馈及终局方案（方案对比页第 3 版）。系统切换与外壳调整一起做。
- 2026-11-16 用户确认工作空间对客户不出现租户。平台人员的租户上下文放在头像菜单里。

## 2. 方案

### 现状事实

- 品牌块下拉：`template/frontend/src/app/layout/components/default-sidebar/default-sidebar.html`，区域列表来自 `layout/services/navigation-service.ts` 的 `areaOptions`。工作区组由 `//#if (TenantWorkspace)` 包裹。
- 侧栏顶部是搜索按钮（`data-testid="sidebar-search"`），底部是 `app-user-menu`（`layout/components/user-menu/user-menu.ts`）。头像菜单里已有"工作空间 / 管理平台"一项，与品牌块重复。
- 顶栏布局的租户切换：`layout/components/workspace-nav/workspace-nav.html`（`workspace.switchTo`）。
- 开放应用 DTO：`template/backend/src/CompanyName.ProjectName.Application/OpenApplications/Dtos/*OpenApplication*Dto.cs`，只有显示名和回调地址，没有首页地址。OpenIddict 7.8 的应用描述没有 `client_uri`，但有 `Properties`（扩展属性）和 `DisplayNames`（多语言显示名）。
- Resource 已经用 client credentials 从 Identity 读账户偏好（`account-preferences.read`，见 `template/docs/standards/service-invocation.md`）。系统列表沿用同一种接法。
- 分布式事件：`framework/components/event-bus/Leistd.EventBus.EntityFrameworkCore/Stores/DistributedEventDatabaseScope.cs`。`database` 为空或为 `host` 时是共享业务库，并且能看到所有租户（AllTenants）；其他值是独立租户库的代表租户 Id。租户调用方固定为本租户。前端：`template/frontend/src/app/features/platform/components/distributed-events/distributed-events.ts`。
- 业务参照：下游业务 Identity 的系统入口目录与启动台接口（独立目录 + 子域模板 + 按租户开通 + 回调派生）。品牌块组件是服务切换器。

### 设计

见 [design.html](2026-11-16-shell-navigation-system-switcher/design.html)，其中有逐条判断、"谁看到什么"矩阵、系统切换流程，以及与业务做法的对照。要点：

1. **系统目录 = 开放应用登记**
   - 浏览器客户端新增"首页地址"（`Properties["home_uri"]`）和"显示在系统切换中"（`Properties["show_in_switcher"]`）。表单填首页地址时，按 `<首页>/api/v1/auth/signin` 与 `<首页>/api/v1/auth/signout-callback` 预填建议回调（以模板实际路径为准），回调仍显式保存。
   - Identity 提供 `GET /api/v1/systems`：登录用户或带 `system-directory.read` 的服务调用方可读，返回 `[{ key, displayName, homeUrl, isCurrent }]`，Identity 自身以签发方地址排第一。
   - Resource 提供同名接口，经 `Leistd:ServiceClients:Identity` 以 client credentials 转取，短时缓存。
2. **外壳**
   - 品牌块 `app-system-switcher`：多于一个系统时是下拉，否则是首页链接。
   - 侧栏头部：品牌块下面是租户选择（`hlm-combobox`，工作区形态）。
   - 侧栏底部：平台控制台入口 / "返回 <租户>"（工作区形态 + 能访问控制台）。
   - 页头：居中搜索、最右侧头像（`app-user-menu` 改为页头变体）。
   - 工作空间顶栏：去掉 `workspace-nav` 的租户切换与租户标识。
   - 命令面板：增加"切换系统"分组。
3. **分布式事件**：管理接口把 `database` 换成 `tenantId`（可空）。宿主传租户时，后端按租户定位库；不传时，"全部"把共享库（所有租户）与各独立库按时间合并，现有的 `Before` 游标加偏移，独立库数量少，代价可接受。前端把选库换成租户筛选（宿主范围），租户范围不显示筛选。

### 备选与取舍

- **独立系统目录（业务做法）**：能支持子域模板与按租户开通，但模板没有这两个需求。系统已经是开放应用里的客户端，再维护一份目录要两处同步。不采用。
- **回调由首页地址派生**：开放应用也登记第三方客户端，回调必须能显式控制，所以改为表单预填建议值。
- **租户选择放页头右侧（业务做法）**：用户选择放在侧栏顶部，层级是 系统 → 租户 → 菜单，更直观。差异写进下游对齐清单。
- **分布式事件去掉宿主的跨租户视图**：运维要逐个进入租户才能看失败事件，代价太大。改为按租户筛选，保留"全部"。

### 兼容与回滚

- 开放应用新字段可空，已有登记不受影响。不勾选显示时，行为与现在相同（品牌块是链接）。
- 分布式事件管理接口是破坏性变更（参数名与语义）。模板前端同步修改。使用该接口的下游业务服务在同步框架 beta 时按对齐清单调整。
- 回滚：撤销 PR。扩展属性里的多余键会被忽略，不需要清理数据。

## 3. 计划

| # | 任务 | 产物 | 验证 |
| --- | --- | --- | --- |
| 1 | 框架分布式事件管理接口改为按租户筛选，"全部"按时间合并 | `Leistd.EventBus.*` 的 DTO、Store、Scope 与测试，随包文档 | 框架单元与集成测试：共享库租户、独立库租户、"全部"合并分页、租户调用方 |
| 2 | Identity 开放应用的首页地址与显示开关，系统列表接口，`system-directory.read` scope | Application/Api 代码、权限与 scope 登记、操作记录 | 后端集成测试：增改查与校验（AC4）、列表顺序与过滤（AC1）、服务调用方授权 |
| 3 | Resource 转取系统列表 | Resource 客户端注册、接口、缓存 | 后端集成测试（AC5）；`scripts/test-template-oidc-e2e.ps1` 登记首页地址并断言两个 Resource 能读到列表 |
| 4 | 前端外壳：系统切换、租户选择、底部控制台入口、页头搜索与头像、头像菜单、命令面板 | layout/components 下的组件与 spec、i18n、ENGLISH 表、Mock | 组件 spec（AC1–AC3、AC6–AC13）；1440/390 版式自查 |
| 5 | 前端：开放应用表单新字段（预填回调），分布式事件租户筛选 | features 下的表单与页面、spec、Mock | spec（AC4 前端提示、AC14、AC15） |
| 6 | 文档 | `template/docs/standards/service-invocation.md`（登记首页地址与 `system-directory.read`）、前端布局规范、框架随包文档；本规格 §4 | 文档引用检查 |
| 7 | 交叉形态验证 | worker-01 上实际生成 Standalone / Identity / Resource × Impersonation / Workspace × 关闭本地化的代表形态 | 前端 lint、构建、受影响测试；浏览器 e2e：系统切换带工作区、租户选择、进入与返回控制台、刷新受保护页 |
| 8 | 交付 | PR；develop 的 Release 运行变绿；两套演示站更新并在开放应用登记首页地址 | 有头走查；1440/390 实测对比页（AC16） |

**下游对齐**（写入本规格，同步时由三仓自行对齐，不发通知）：

- 业务的独立系统入口可评估收敛到开放应用登记；子域模板与按租户开通是业务特有能力，保留。
- 租户选择的位置与"返回"按钮。
- 下游业务服务分布式事件页的选库改为租户筛选。

**不做的事**：按租户开通系统、子域地址模板、在系统列表上预判权限、顶栏布局导航结构调整。
