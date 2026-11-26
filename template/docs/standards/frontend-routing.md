# 前端路由规范

路由守卫、跳转与地址栏的规范。错误归一化与反馈见[错误处理](./coding-frontend.md#6-错误处理)。

## 1. 路由守卫与地址栏

- 启动阶段并行读取互不依赖的数据，守卫复用会话已有的结果。范围变化时读取新范围，授权写入、设置保存和显式恢复后照常刷新。启动失败（非 401）时守卫取消导航并记下目标，重试成功后进入。
- 权限不足时，`permissionGuard` 跳到 `/forbidden?target=<被拒的目标>`，用 `browserUrl` 保留目标。刷新、前进或后退会还原 403 页。`forbiddenGuard` 转回目标重新判定并替换当前历史条目，仍无权限则回到 403 页。
- 403 内部地址携带目标以区分相邻历史条目。用 `runGuardsAndResolvers: 'paramsOrQueryParamsChange'` 让复用的 403 页重跑守卫，保持 `onSameUrlNavigation` 默认值。
- 原地显示 404 页的通配路由（模板中为根通配）不用 `browserUrl`：地址栏就是用户输入的地址，刷新时重新匹配。
- 新增带 `browserUrl` 或 `skipLocationChange` 的跳转，要写明刷新与前进/后退时的预期，并补测试。
- 整页离开（去登录、回授权端点、退出、模拟登录进出等）一律经 `frontend/src/app/core/routing/full-page-navigation-service.ts`，不直接写 `location`、不手建表单：当前页只是中转就替换历史条目，后退不回到它。离开中的页面从往返缓存（bfcache）恢复时守卫不重跑，由该服务整页重载；组件不各自监听 `pageshow`。
- Router 保持默认 `canceledNavigationResolution: 'replace'`：模板没有会取消后退/前进的守卫，启动失败扣下目标时 `EntryRouteService` 也依赖它在取消后还原地址。新增 `canDeactivate` 等会拒绝后退/前进的守卫时改用 `'computed'`，否则被拒的后退会覆盖上一条历史、再后退跳过本页，并补"拒绝后退 → 再后退"与"刷新后首次导航被拒"的测试。
- 判断"这次导航从哪来"只用公开的导航信息（`Router.currentNavigation()` 的 `extras`，含只属于本次导航的 `info`），不读写 Angular 的私有状态键（`ɵ` 前缀，如 `history.state` 里的 `ɵrouterUrl`）：它们随版本变化，没有兼容承诺。
- 权限守卫的测试覆盖放行、拒绝，"拒绝 → 授权 → 刷新后进入目标"与仍被拒的刷新，以及在两条相邻 403 历史之间前进/后退（见 `frontend/src/app/core/guards/permission-guard.spec.ts`）。

<!--#if (TenantWorkspace)-->
## 工作区路由

`/platform/**` 只表示宿主控制面；所有租户内页面（包括租户用户自己的页面）统一在 `/w/:workspace/**`，复用管理平台与工作空间子路由。租户用户打开 `/platform/**` 时转到自己租户的对应路径。

入口地址确定目标工作区，权限守卫判定前加载该范围的权限与设置。切换完成后提交，旧范围的迟到响应按代次丢弃。父组件按工作区键重建子出口并复用首屏骨架。冷启动、历史导航和多标签页都以地址为准，localStorage 只记上次落点。本人授权变化后更新目录，其他会话的变更在显式刷新、重载或重新登录后可见。

公共根地址保留着陆页，不执行平台的认证与工作区守卫；平台父路由匹配 `platform`，子路由匹配空路径，再执行权限检查。不要用带平台守卫的空路径父路由包裹它，否则根地址也会被匹配为没有页面组件的受保护分支。

冷启动或深链的 `Workspace:Forbidden`、`Workspace:Unavailable` 显示独立的「无法访问该工作区」空态说明页，区分账号越界与工作区不可用，地址栏保留原目标。「前往我的工作区」共用登录后落点：上次仍可用的工作区 → 第一个 → `/platform`。刷新与历史还原重新判定原目标。应用内切换被拒恢复原范围并提示；Unavailable 刷新目录后退到下一个可用工作区。

HTTP 拦截器用 `HttpContextToken` 区分本人请求与工作区请求，后者默认取目标路由参数。`AuthService.currentUser` 只表示本人，业务订阅、本人行判断与租户资源键使用工作区主体。平台人员在工作区内保持本人常驻连接与随切换重建的工作区连接；通知中心按当前范围显示。租户用户只保留一条连接。
<!--#endif-->
