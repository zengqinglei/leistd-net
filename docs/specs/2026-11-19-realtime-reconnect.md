# 实时连接持续重连与悬浮提示

状态：进行中 · 风险档：中 · 关联：设计稿 [design.html](2026-11-19-realtime-reconnect/design.html)

## 1. 需求

### 问题与背景

模板前端的实时连接（`template/frontend/src/app/core/services/signalr-service.ts`）用 `withAutomaticReconnect([0, 2000, 5000, 10000, 30000])`。SDK 按数组重试 5 次后关闭连接，之后不再尝试；前端也没有其他兜底，只有点提示条上的"重新连接"、进入角色页或重新登录时才会再建连。

用最小 SignalR 服务加 `@microsoft/signalr` 10.0.11、相同重试配置实测：

- 停机 8 秒：第 4 次重试连回。
- 停机 60 秒：断线约 47 秒后客户端报"重试次数已用尽"并关闭；服务恢复后观察到 130 秒，连接一直是 `Disconnected`。

重新部署时停机时长在 47 秒上下波动，所以表现为"偶尔不恢复，提示条一直挂着"。

提示条（`layout/components/realtime-status`）还有两个问题：它是一整行 Alert，插入页面顶部后把内容往下推；点"重新连接"后服务先把状态置为空闲，提示条消失，失败后又出现，看不出是否在重试。

### 业界做法（按源码与官方文档核对，2026-11-19）

- SignalR 官方默认只重试 4 次就放弃，自动重连不覆盖首次 `start()` 失败；官方文档没有给出"一直重连"的写法。
- Rocket.Chat、Mattermost、Phoenix、centrifuge、Meteor、socket.io 都不放弃：间隔逐渐加长并设上限（5 秒到 5 分钟不等），大多带随机抖动，会话或令牌失效才停止。多数在网络恢复（`online`）时立即重试，Phoenix 在切回页面时立即重试。
- 断线提示：Rocket.Chat 断线即在顶部显示横条（倒计时加"Connect"按钮），Mattermost 失败 7 次后在顶部公告栏提示，Phoenix LiveView 断开 0.5 秒后在右上角提示。

对比表见设计稿。

### 目标

- 登录期间实时连接不放弃，服务恢复后自动连回，不需要用户操作。
- 断线提示改为顶部居中的悬浮胶囊，不推开页面；重试时有进行中的反馈。

### 非目标

- 不补拉断线期间漏掉的推送：现有机制是连回后发出 `resourceSubscribed$`，由页面重查一次。
- 不改后端 Hub、会话与 Data Protection 配置。

### 已确认的决策（用户，2026-11-19）

- 重试间隔：前四次 0、1、2、5 秒；之后从 10 秒起翻倍，封顶 30 秒，再乘以 0.5 到 1 的随机系数。
- 登录期间一直重试。疑似会话失效（negotiate 返回 401）时，交给现有的会话重查：会话确已失效，就走登录过期流程并停止重连；会话仍有效，就继续重试。403 继续重试。
- 网络恢复、页面切回前台或点"立即重试"时，跳过等待马上重试。
- 胶囊在断线 3 秒后仍未连回时才出现，位置在页头下边沿正中（设计稿方案 A）。共四种状态：重连中、等待重试（倒计时加"立即重试"）、重试中、已恢复（只在胶囊出现过时显示，2 秒后淡出）。
- 规格按中风险档写；计划阶段不送审，代码完成后只把重连逻辑送 Codex 审查一轮。

### 验收标准

| # | Given / When / Then | 覆盖 |
| --- | --- | --- |
| AC1 | Given 已连接；When 服务停机超过 47 秒后恢复；Then 连接按退避自动连回，状态回到 `connected`，已持有的资源订阅重新确认 | `signalr-service.spec.ts`（重试策略不返回空、onclose 后重建） |
| AC2 | Given 首次建连或重建时 `start()` 失败；When 等待到期；Then 按同一退避再次建连，状态在此期间保持 `reconnecting`，不回到 `idle` | `signalr-service.spec.ts` |
| AC3 | Given 处于等待重试；When 收到 `online`、页面切回可见或调用立即重试；Then 取消等待并马上建连，退避计数从头开始；正在建连时不重复发起 | `signalr-service.spec.ts` |
| AC4 | Given 处于等待重试；When 登出或主体切换（`reset()`）；Then 等待被取消，不再为旧主体或新主体自动建连 | `signalr-service.spec.ts` |
| AC5 | Given 重试失败原因含 `Status code '401'`；When 发生；Then 发出会话重查信号，`SessionContextService` 调用 `revalidate()`；重试不因此停止 | `signalr-service.spec.ts`、`session-context-service.spec.ts` |
| AC6 | Given 退避第 n 次（n ≥ 4）；When 计算间隔；Then 落在 [0.5, 1] × min(30 秒, 10 秒 × 2^(n−4)) 之间；前四次依次为 0、1、2、5 秒 | `signalr-service.spec.ts` |
| AC7 | Given 断线；When 3 秒内连回；Then 不显示胶囊；超过 3 秒才显示"重连中" | `realtime-status.spec.ts` |
| AC8 | Given 等待重试；Then 胶囊显示秒级倒计时与"立即重试"；读屏只播报一次稳定文案，不逐秒播报 | `realtime-status.spec.ts` |
| AC9 | Given 点了"立即重试"；Then 胶囊保留，按钮转圈、不禁用，再点不重复发起 | `realtime-status.spec.ts` |
| AC10 | Given 胶囊已显示；When 连回；Then 显示"已恢复" 2 秒后消失；胶囊没出现过时不显示"已恢复" | `realtime-status.spec.ts` |
| AC11 | 胶囊悬浮，不改变页面布局；在 390 / 768 / 1440 宽度、亮暗两种主题、中英文下不折行、不压住页头搜索 | 人工验收（浏览器截图） |

### 方案

服务沿用 SDK 自动重连，传入自定义 `IRetryPolicy`：除 Mock 外永不返回 `null`，并记录下次重试时刻供界面倒计时。SDK 不覆盖的两种情况，由服务自己用同一退避处理：首次或重建时 `start()` 失败，以及当前连接收到 `onclose`（服务端发来不允许重连的关闭消息等）。"立即重试"的做法是：静默丢弃当前连接，状态保持 `reconnecting`，再新建连接。状态类型去掉 `disconnected`，只剩 `idle`、`connected`、`reconnecting`，另外暴露下次重试时刻。重试失败原因匹配到 401 时，服务发出会话重查信号；`SessionContextService` 收到后调用已有的 `revalidate()`，401 由拦截器清会话并调用 `reset()`，重连就此停止。

提示组件改为零高度宿主，胶囊绝对定位，骑在页头下边沿。倒计时每秒刷新，放在 `aria-hidden` 里；宿主里另放一句稳定的读屏文案。`frontend-navigation.md` 的挂载说明同步修改。
