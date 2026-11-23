# 模板首屏性能：静态资源压缩、外壳懒加载与首屏规范

状态：已完成 · 风险档：中（宿主静态资源响应与前端路由加载时序，不改认证、权限、数据与发布流程） · 关联：下游业务项目的同主题规格（业务项目按 nginx 部署单独验收，本规格只核对共用策略）

## 1. 需求

### 问题

一个下游业务项目首屏不达标，其员工端由本模板生成。在该员工端试验过的三类根因（T1–T3）模板里同样存在，T4 是预算与规范的缺口（develop `a0aca4d8`）：

- **T1 静态资源未压缩。** `Dockerfile` 在 `dotnet publish` 之后才把 Angular 产物拷进 `wwwroot`。宿主用 `UseStaticFiles(SpaExtensions.CreateSpaStaticFileOptions())` 托管，没有压缩，JS 和 CSS 按原始大小传输。
- **T2 布局外壳直接引用。** `app.routes.ts` 里 `EmptyLayout`、`WorkspaceLayout`、`DefaultLayout`、`TenantLayout` 都写成 `component:`，外壳及其依赖都进了首包。该员工端做了同类改动：首包 1.37 → 1.07 MB。它现有的 Lighthouse 样本最终落到的是 Identity 登录页，不能拿来作模板的速度基线。
- **T3 SignalR 在首包。** `core/services/signalr-service.ts` 静态导入 `@microsoft/signalr`。该业务项目实测约 66 kB 未压缩。
- **T4 没有首屏规范。** `template/docs/standards/` 里只零散提到懒加载。`initial` 预算是 1.5 MB 警告、2 MB 报错，与实际体积脱节。

不在范围内：

- Portal 特有的问题（Logo 首访预加载、人机验证、入口接口）；
- CDK 浮层那 49 kB：走上游 issue；
- 真实用户监控：跟随业务项目的后续计划；
- 框架（Leistd.* 包）：不改。

### 验收标准

| # | Given / When / Then | 验收入口 |
| --- | --- | --- |
| AC1 | Given 生成的含前端发布镜像已启动，并经现有 TLS 与转发头配置的 HTTPS 入口访问<br>When 带 `Accept-Encoding: br, gzip` GET 根级带哈希的 JS/CSS<br>Then 返回 200，`Content-Encoding` 为 br 或 gzip，有 `Vary: Accept-Encoding`，缓存仍是一年不可变 | 经 HTTPS 入口做一轮真实 GET 检查，结果记入 §4 |
| AC2 | Given 同一镜像<br>When 不带编码协商请求同一文件，以及请求根路径、深链接、非哈希资源、缺失的 JS、未命中的 `/api`<br>Then 依次得到：原文可读；可重验证的 index；`no-cache`；404；404 | 同上 |
| AC3 | Given 生产构建（本地化场景保留 Transloco `postbuild`）<br>When 对比基线与最终候选的 `--stats-json`<br>Then 记录 `initial` 原始字节，不要求下降；布局外壳保持静态引用，不懒加载；SignalR 保持静态导入 | 构建统计 |
| AC4 | Given 同一已登录落地路由，保留会话，单独清理或禁用资源缓存，相同的压缩与节流<br>When 对比基线与候选<br>Then 两项都不出现可重复的退化：浏览器 trace 中页面内容就绪点之前的传输字节；串行冷访 3 次的 LCP 中位数。瀑布图只用于定位新增的等待。记录最终 URL 与 LCP 元素，避免把登录跳转或启动骨架当成目标页面；压缩带来的下降单独记录。出现可重复退化的布局保留原来的加载方式 | 浏览器 trace 与 Lighthouse，记入 §4 |
| AC5（仅在采用动态导入时） | Given 在启用实时能力的生产构建上，SDK 块的第一次下载临时失败，之后同一 URL 恢复<br>Then 外壳仍可用，同一页面经现有退避连上 Hub。单测覆盖导入失败后的应用状态，以及代次已变时不建连 | 现有浏览器入口做一次检查，加前端单测 |
| AC6 | Given 最终路由（布局外壳保持静态引用）<br>Then 现有路由与导航用例通过 | 前端测试 |
| AC7 | 规范写入首屏条款，同步 `template/docs/README.md` 的任务读取表 | 文档审查与阅读成本闸门 |

### 方案

**压缩（T1）：** 采用 ASP.NET Core 官方的响应压缩中间件。

- 在 `SpaFrontend` 条件下、`UseStaticFiles` 之前启用；用一次正向匹配，只对根级 JS/CSS 生效；使用框架默认的 Brotli 和 Gzip 提供程序，开启 `EnableForHttps`。
- Dockerfile 的发布顺序、现有缓存契约和 SPA 回退都不变。
- 不选 `MapStaticAssets` 的原因：它按 .NET 自己的指纹 URL 设置不可变缓存，Angular 已经带哈希的 URL 会被当成原始 URL；接入还要改发布清单和路由接线，收益只是把压缩开销移到构建期。

**懒加载（T2）：**

- 原计划把 `WorkspaceLayout`、`DefaultLayout`、`TenantLayout` 改为 `loadComponent`，`EmptyLayout` 保留静态引用。
- Angular Router 22.3 要等守卫和 resolver 完成后，才并发加载已匹配的 `loadComponent` 树，所以布局请求可能多出一段关键路径。这一点按 AC4 实测验收，不预先保证。
- **决定：四个布局外壳都保持静态引用**（AC4 出现可重复退化，数据与原因见 §4）。懒加载只用于功能路由。
- 路由用到的 provider 目前已由独立模块导入，没有需要搬迁的令牌。

**SignalR（T3）：**

- 同步分支改为类型导入，状态值在类型约束下比较。构造连接需要的值，放到动态导入之后再用。
- 主体代次的检查放在 `await import()` 之后、建立和登记连接之前；导入失败沿用现有的退避入口。
- 是否采用动态导入，以 AC5 的那次短暂失败检查为准。现行 HTML 标准规定，网络或 HTTP 获取失败后会移除对应的模块映射条目，Chrome 从 156 起支持重新获取；模板锁定的 Playwright 搭配 Chromium 159。如果检查不通过，就保留静态导入并记录原因，沿用既有的 Hub 建连与重试测试，不自建加载器。
- **决定：保留静态导入**（结果与原因见 §4）。

**预算与规范（T4）：**

- `initial` 预算按较重登记形态的实测值重新设定，数值记入 §4。不加体积脚本。
- 首屏条款放在前端编码规范，界面和导航篇用链接引到同一处。内容：
  - Core Web Vitals，按真实访问的 P75，移动和桌面分开：LCP ≤ 2.5 s、INP ≤ 200 ms、CLS ≤ 0.1；
  - 默认懒加载；
  - 首屏非必需的第三方脚本按需加载；
  - 只给真实的 LCP 图片提优先级：CSR 下用 `NgOptimizedImage` 的 `priority`；瀑布图显示发现得晚、且地址已知时，再用与实际图片同 URL 的 preload；
  - 预算上调须附构建统计差异。
- 压缩说明写进现有部署文档，随包只写最终验证过的行为。

**验证：** 按仓库现行配方。

- 本地：`-Sync` 后跑目标 spec、lint、定向构建，并在真实发布镜像上检查 AC1、AC2。
- 用选择器产出的场景跑 `-GenerateOnly`，确认 `SpaFrontend`、实时能力、Workspace 三个开关的组合都覆盖到。复用 `identity-workspace` 已有的直达与刷新验证。
- PR CI 承担 core 档增量；合入后和发布前由 full 档证明完整覆盖。
- 高负载步骤经 remote-project-runner 跑在 worker-01 上。

## 4. 结果

重活经 remote-project-runner 执行：worker-01 在线时在节点上跑，下线后改本机串行（AC1、AC2）。逐次数据与瀑布细节留给 PR 描述。

### 实际交付（工作区，未提交）

- **T1 压缩**：`WebHostExtensions` 在 `SpaFrontend` 下注册 `AddResponseCompression(EnableForHttps = true)`，用默认 Brotli、Gzip 提供程序。`SpaExtensions.UseMyProjectSpaCompression` 在 `UseDefaultFiles`/`UseStaticFiles` 之前只对根级 `.js`/`.css` 启用；路径判据与缓存契约共用 `IsRootLevelBundle`。这些响应不论是否压缩都声明 `Vary: Accept-Encoding`，编码仍由官方中间件协商。`SpaStaticFileCachingTests` 用一个宿主覆盖缓存、压缩、不协商编码的原文与嵌套目录反例。部署说明只写验证过的行为。
- **T2 布局外壳**：撤回，四个布局保持静态引用。
- **T3 SignalR**：保留静态导入。
- **T4**：
  - `initial` 预算由 1.5 MB / 2 MB 改为 1.44 MB 警告、1.5 MB 报错。最重登记形态 identity-workspace 的 Angular `Initial total` 为 1.38 MB（约 1,380,440 B），两档分别上浮约 4.3% 与 8.7%。
  - 前端开发规范新增 §10 首屏性能，界面与导航篇链接到该节，读取表已同步。

本次的首屏收益来自压缩。布局外壳懒加载只是把字节挪到守卫之后；SDK 动态导入在较旧的受支持浏览器上存在恢复缺口。两者均撤回。

### 验收记录

- **AC1、AC2**：通过。用生成项目（standalone-workspace-minimal）的 Dockerfile 构建含前端的镜像，候选框架包经临时包源注入，再经 HTTPS 入口检查（本机 job `20261123t141727-6788756c976d`，总状态 passed，清理完成）。
  - 根级 `main`、`chunk`、`styles` 均按协商返回 Brotli，`main` 另测 Gzip；`main` 与 `styles` 不带 `Accept-Encoding` 时返回原文，同样只带一个 `Vary: Accept-Encoding`，压缩结果解压后与原文逐字节一致；有效 bundle 均为一年 `immutable` 缓存。
  - `index.html` 与深链接返回同一份正文，`no-cache`，带 ETag 时可 304 重验证；非哈希资源 `no-cache`。
  - 缺失的 JS 与未命中的 `/api` 返回 404，转发头生效（HTTPS 与 HTTP 来源的登录请求分别得到 400 与 403）。
  - worker-01 上的首次尝试（job `20261123t110100-c4ff37278e30`）因拉取基础镜像过慢、随后节点下线而取消，其清理状态待节点恢复后核对。
- **AC3**：最终首包与 develop 相同，布局与 SDK 都在首包里。Angular `Initial total`：identity-workspace 1.38 MB、resource-workspace 1.36 MB、identity-all-features 1.32 MB、identity 1.18 MB，新预算下均无警告。把 `maximumError` 临时压到 1.3 MB，生产构建按预算报错失败（job `20261123t100211-42df8315936d`，构建与反证阶段通过；该作业总状态 failed，原因是最后的生成检查阶段在节点上缺 Python venv 模块，与构建无关）。
- **AC4**：布局懒加载的首屏出现可重复退化，按 AC4 保持静态引用。
  - 环境：identity-workspace，e2e 入口托管生产构建，两侧同样压缩。已登录、禁用缓存、CDP 节流，每路由串行冷访 3 次、两轮。
  - 结果：就绪前传输字节约 +20 kB，就绪前请求数约翻倍；LCP 在多数路由与轮次变慢；Lighthouse LCP 约 11.9 s → 14.6 s。
  - 机制：首包变小让启动提早约 0.5 s，但布局块要等守卫链结束才沿导入链分批下载，提早的部分被抵消。
  - 只差布局加载方式的对照中，懒加载布局在三条路由上都晚约 0.3 s（job `20261123t092302-bc6352f9a1a6` passed；对照 job `20261123t083550-9af5c53b7b91` 的测量阶段通过，总状态 failed，原因是其后的 HTTPS 脚本缺陷）。
  - 局限：该入口是 HTTP/1.1，HTTP/2 下未实测。
  - 压缩让就绪前 JS/CSS 的传输量减少约 66%。
- **AC5**：未采用动态导入，不适用。本项目的实测证据来自 Chromium：SDK 块首次下载失败后同一 URL 重新获取，同一文档经退避连上 Hub。较旧的受支持浏览器（Firefox 155 之前等）存在恢复缺口，页面要刷新才能恢复实时连接，因此保留静态导入，沿用既有的 Hub 建连与重试测试。
- **AC6**：最终候选的 identity-workspace 完整 `verify.ps1` 通过（job `20261123t104523-e184a5896577`，总状态 passed）：后端单元 339、集成 854（含 `SpaStaticFileCachingTests`），前端 190 个文件 2170 个用例，lint，e2e 16 个（含直达与刷新）。其中 `two-factor-required.e2e.ts` 前两次 30 s 超时、第 3 次通过，记为 flaky。同一用例在前两次带压缩的完整运行里首次即过，这次整轮耗时约为平时的两倍，判断为节点负载所致。
- **AC7**：
  - 前端规范 §10、界面与导航篇链接、读取表已同步；`measure-template-read-cost.py --check` 与 `check-retired-terms.ps1` 通过。
  - worker-01 上 `-GenerateOnly` 7 个场景通过（job `20261123t102957-215bbc202cb4` passed），覆盖 `SpaFrontend`、实时能力、Workspace 的开关组合。

### 遗留

- 布局懒加载在 HTTP/2 入口下的表现未实测。
