# 多租户：一个 Identity 加多个 Resource 的验收与评估

核查与验收日期 2026-10-02。对象是从当前模板生成的四个服务：

- **idp**：Identity，含租户管理；
- **orders**、**billing**：两个 Resource，用户来自 idp，角色和权限各自管理；
- **solo**：Standalone，对照组。

自动化入口：`scripts/test-template-oidc-e2e.ps1 -IncludeMultiTenantScenarios`。本轮新增 `scripts/test-template-oidc-multitenant.ps1`。
选定改进方案后删除本文，长期结论回写 `framework/docs/components/multi-tenancy.md`、`template/docs/standards/` 与 `docs/template/`。

## 结论

1. **这是主流架构。** 单 issuer、令牌携带租户、各服务自行授权，与 Auth0 Organizations、Keycloak Organizations、Microsoft Entra（app roles 按应用划分）、AWS SaaS Lens 的推荐一致。和 ABP 微服务模板相比，差别只在一点：ABP 把权限授予集中在 Administration 服务；我们把授予分散到各服务，更接近 Entra app roles 的做法。
2. **核心机制可靠，并经过端到端验证。** 已验证的能力：
   - 租户在共享库内按行隔离、独立库按库隔离；
   - 伪造租户请求头无效；
   - 委托调用时租户随令牌交换传递；
   - 两个 Resource 之间单点登录（无感，不出现登录页）；
   - 后端静默续期，浏览器不持有任何令牌；
   - 租户停用后，在令牌窗口内收敛，且不会出现跳转循环。
3. **两项能力不足以支撑"开箱即用的多服务产品"，需要补齐。**
   - **Resource 没有授权起点。** 部署后没有任何人能通过接口授予第一条权限，只能直接写库。
   - **没有单点登出。** 从一个应用退出后，其他 Resource 的会话继续存在，还能一直静默续期（已实测）。
4. 另有若干口径不一致和文档缺口，见第五节。

## 一、业界常规验收场景与覆盖

来源与逐条依据见本轮调研：Auth0、Entra、Keycloak、Duende BFF、ABP、OWASP Multi-Tenant Security Cheat Sheet、RFC 8693、RFC 9700、RFC 10017。

| 组 | 验收要点 | 本轮自动化 | 已有自动化 |
|---|---|---|---|
| a 租户生命周期 | 独立库租户：先迁移、再登记；停用后禁止新登录；已签发令牌在窗口内收敛；刷新被拒 | MT0（新建独立库并迁移三服务）、MT5 | S5、S7 |
| b 数据隔离 | 列表看不到其他租户；按 Id 越权返回 404；伪造租户头无效；后台作业与缓存按租户分键 | MT1（行级、库级、按 Id、`X-Tenant` 伪造） | S5（投影落库）；作业与缓存只有单元/集成测试 |
| c 上下文传播 | 委托调用保留租户和用户，`act` 指向调用方；不信任客户端传入的租户 | MT2（HTTP）、MT3（浏览器 Cookie 会话委托） | S4（交换正反例） |
| d SSO 与登出 | 第二个应用无感登录、租户一致；RP 退出结束本应用与 IdP 会话；其他应用的行为有文档说明 | MT3、MT4 | S11（单应用退出） |
| e 令牌生命周期 | 浏览器无令牌；过期后静默续期；并发刷新只刷一次；刷新失败返回 401 并整页重新认证 | MT4（续期轮换）、MT5（失败收敛） | S1、S9；并发刷新见集成测试 `ResourceBrowserSessionTests` |
| f 各服务授权 | 首访建立投影且默认无权限；同一用户在不同服务的权限互相独立 | MT1（投影存在、默认 403、orders 授予后 200、billing 仍为 403） | `ResourceUserProjectionTests` |

未覆盖：

- 一个用户属于多个租户时的切换。模板的用户只属于一个租户，换租户只能退出后重新登录。
- 跨服务事件的租户传播。框架只提供进程内事件总线。
- Resource 的 DbMigrator 经远端枚举全部独立库。端到端测试使用显式的 `MigrationTarget`。

## 二、测试拓扑与执行

- **服务**：idp（`identity-external-login`）、orders 与 billing（`resource`，各自带前端，由所属 API 同源托管）、solo（`standalone`）。
- **运行方式**：PostgreSQL 15 容器；全程 HTTPS（自签证书）；以 Production 环境运行；浏览器场景中访问令牌为 90 秒。
- **租户**：
  - mt-alpha、mt-gamma：共享库 `oidc_shared`，按行隔离；
  - mt-beta：独立库 `oidc_mt_beta`，先对 idp、orders、billing 跑迁移，再在 idp 登记。
- **命令**：`pwsh scripts/test-template-oidc-e2e.ps1 -IncludeMultiTenantScenarios -DockerContext orbstack -SkipPack -LocalFeedPath <feed>`

## 三、测试结果

最终一轮（运行目录 `.tmp/oidc-e2e/71524-20261002143949339`，证据另存于 `.tmp/oidc-review/evidence/final-mt/`）：
22 个场景全部通过，共 1185 条断言，0 失败。

| 场景 | 结果 | 关键断言 |
|---|---|---|
| S2–S8、S10、exchange-expiry（HTTP） | 通过 | 原有协议、令牌与租户用例 |
| S1、S9、S11、Google/GitHub（浏览器） | 通过 | BFF 登录、静默续期、刷新失败收敛、退出、外部登录 |
| MT0 准备 | 通过 | 新建独立库，对 idp、orders、billing 跑迁移后再登记租户；alpha、gamma 用共享库 |
| MT1 授权与隔离 | 通过 | 首访投影后默认 403（没有授权起点）；直接写入授予后 orders 返回 200，billing 仍为 403；alpha 看不到 gamma（行级）和 beta（库级）；按 Id 越权返回 404；`X-Tenant` 伪造无效 |
| MT2 委托传播 | 通过 | orders → billing 的委托调用中，用户、租户与 `act=orders-api` 均正确；独立库租户同样成立 |
| MT3 两个 Resource 的 SSO | 通过 | 登录 orders 后打开 billing：idp 只收到 `/connect/authorize`，没有登录页请求；两端租户与用户一致；浏览器 Cookie 会话委托调用时租户正确 |
| MT4 退出 | 通过（**断言的是现状缺口**） | orders 退出后，orders 需要重新交互登录；billing 会话仍然有效，且访问令牌到期后**仍能续期并轮换** |
| MT5 租户停用 | 通过 | 停用后新登录被拒；令牌窗口内 billing 仍为 200；到期后刷新被拒、票据被删，回到 idp 登录页，5 秒内无循环 |

MT4 按"现状行为"断言。补齐单点登出后，应把 `mt-slo-billing-refresh-after-idp-logout` 改为期望失败。
本轮有 1 次点击重试（`tenant-confirm attempt=1`），说明页面启动期确实存在点击被吞的时序。

## 四、支撑能力评估

| 能力 | 是否足够 | 是否正确 | 是否最佳实践 | 说明 |
|---|---|---|---|---|
| 租户控制面集中在 Identity | 足够 | 正确 | 是 | 注册表与连接登记只在 Identity；Resource 通过机器令牌回源，连接串加密、只用进程内缓存 |
| 租户解析（Resource 只信令牌） | 足够 | 正确 | 是 | Resource 的解析链收窄到主体 claim，`X-Tenant` 伪造无效（MT1）。符合 OWASP"请求中的租户只是选择器"的要求 |
| 数据隔离 | 足够 | 正确 | 是 | EF 全局过滤加启动期闸门；独立库按 `(租户, 连接名)` 登记；行级与库级均实测 |
| 跨服务传播 | 同步调用足够；异步不提供 | 正确 | 是 | 单跳交换，保留租户与 `act`，不继承角色；机器调用要求显式传租户 |
| SSO | 足够 | 正确 | 是 | Identity 会话有效时，第二个 Resource 无感登录（MT3，idp 日志中没有登录页请求） |
| 静默续期与浏览器令牌 | 足够 | 正确 | 是（RFC 10017 BFF） | 由服务端临近过期时刷新，用锁串行化；浏览器只持有引用 Cookie。前端不需要也不应做 iframe 或令牌静默刷新，401 时整页重新认证，Identity 会话仍在时无感 |
| 停用收敛 | 足够 | 正确 | 可接受 | 本地验签的窗口上限等于访问令牌有效期（10 分钟）；之后刷新被拒，会话被删，回到登录页且无循环（MT5） |
| 单点登出 | **不足** | 行为与文档一致 | 否 | Identity 退出不撤销其他 Resource 持有的 refresh token，billing 在退出后仍能续期（MT4）。OpenIddict 7 没有后通道登出 |
| Resource 授权起点 | **不足** | — | 否 | 首访投影默认无权限（正确）；但没有任何接口、配置或种子能授予第一个管理员，只能直接写 `PermissionGrantRecords`（MT1） |
| 租户选择体验 | 一般 | 正确 | 部分 | Resource 跳转 Identity 时不带租户提示，子域名定租户在单 issuer 下无法贯通，租户用户需要在登录页手动输入租户 |

## 五、缺口与建议（按优先级）

1. **P1 Resource 授权起点。** 可选方案：
   - Identity 的租户管理员或宿主超管，在首访投影时映射为本地管理员（配置化的角色映射）；
   - 或者为 Resource 的 DbMigrator 或管理端增加"按 sub 授予首个管理员"的入口。

   无论哪种，都需要同时写清"谁能在各服务分配角色"的责任边界。
2. **P1 单点登出。** OpenIddict 8 计划提供后通道登出。在此之前的可行做法：Identity 退出时撤销该用户在本会话中的授权和 refresh token，这样其他 Resource 会在下一次刷新时（最长等于访问令牌有效期）收敛。需要先为授权关联会话标识。也可以在文档中明确列为已知边界，由业务决定是否接受。
3. **P2 `/me` 口径不一致。** Resource 的 `/me` 返回的 `roles` 与 `isSuperAdmin` 来自 Identity 令牌，而授权依据的是本地数据。前端若据此显示菜单会产生误导。建议 `/me` 改为返回本地授权视图，或删除这两个字段。
4. **P2 租户提示。** 考虑由 Resource 发起登录时带上租户提示（例如 `acr_values=tenant:xxx`，Duende 的做法），Identity 登录页据此预填。
5. **P3 文档与测试：**
   - Resource 接真实库时依赖 `Leistd:ServiceAuth`，README 没有写明；
   - S4 伪造租户头时用了错误的头名 `X-Tenant-Id`（框架默认是 `X-Tenant`），应改正。MT1 已用正确的头名覆盖。

## 六、本轮附带修改（未提交）

- **新增** `scripts/test-template-oidc-multitenant.ps1`，并在主入口新增 `-IncludeMultiTenantScenarios` 开关：构建 billing 前端，执行 MT0–MT5。
- **浏览器辅助**：新增 `Invoke-BrowserNavigationClick`，点击后确认地址确实改变，未改变才重试，重试次数记入 `click-retries.log`。用于应对页面启动期偶发吞掉点击，这也是此前 GitHub 场景偶发失败的同一原因。
- **`Get-CodeToken` 的令牌有效期断言**改为跟随当前配置：浏览器阶段为 90 秒，HTTP 阶段为 600 秒。
- **模板 `ResourceUserProvisioningMiddleware` 注释**：授予的提供方名由误导性的 `"U"` 更正为 `PermissionGrantProviderNames.User`（实际值为 `"User"`）。按旧注释写库的授予不会生效，本轮验收第一次就是因此失败的。
