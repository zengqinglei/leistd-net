# 部署说明

项目根 `Dockerfile` 和 `deploy/` 是当前容器部署配置的事实来源。部署前先核对实际镜像、环境变量、数据库迁移、健康检查和回滚能力，不使用文档中的假定值替代配置。

## 配置与机密的分层

各环境共用同一套配置键，只换值的来源。默认同源部署可将同一镜像从测试环境晋升到生产。前后端分离时，当前 `Dockerfile` 的 `API_GATEWAY` 是构建参数；若各环境地址不同，镜像也会不同，必须分别记录、验证和发布对应产物。需要此部署形态也支持同镜像晋升时，应作为独立产品需求设计运行期配置。
<!--#if (LocalIdentity)-->

会话 Cookie 默认 `SameSite=Lax`，同源部署与同站的前后端分离（如 `app.example.com` 调 `api.example.com`）都可用；站点按公共后缀判定，托管公共后缀域（如 `*.azurewebsites.net`）下的两个子域属于跨站。需要在跨站请求上携带会话 Cookie 时才设 `SessionCookie__SameSite=None`（跨站部署，以及下文列出的跨站 POST 情形），此时须自行接入防伪令牌：模板未启用 antiforgery，Angular 内置的 XSRF 只对同源相对地址生效。`Lax` 下跨站的顶层 GET 导航仍会带上 Cookie，因此 GET 接口不得有副作用。另有两种情形同样需要 `None`：第三方站点以 POST 跳转到 `/connect/authorize` 或 `/connect/logout`，以及接入以 `form_post` 回调且回调地址直接落在 API 上的外部登录提供方。

**`SameSite=None` 只是服务端允许跨站携带，不等于浏览器一定会带上。** 跨站的 fetch/XHR 带的是第三方 Cookie，受浏览器策略约束：Firefox 默认按站点隔离 Cookie 存储（Total Cookie Protection），Safari 的跟踪防护默认拦截，Chrome 保留用户选择。所以 **跨站的 fetch/XHR 会话探测不能只靠 `SameSite=None` 保证可靠性**——它会在一部分浏览器上悄悄失效，表现为"有的人一处退出没有处处退出"，而且不报错、难排查。

上面那两种情形不同：它们是**顶层 POST 导航**，不属于第三方子资源请求，通常不受这类拦截影响（但也不宜承诺在所有浏览器策略下绝对可用）。

要做多系统"一处退出、处处退出"，用不依赖 Cookie 的方案：按访问令牌里的 `sid` 向身份服务查询会话状态（需要先验签令牌，再查服务端会话），或由身份服务按 OIDC back-channel logout 通知各客户端（身份服务与客户端两边都要实现该协议）。**两条都需要自己实现：本模板没有现成的会话查询端点，也没有接 back-channel logout。**
<!--#endif-->

部署时记录 API 和 DbMigrator 的固定版本或 digest，不用 `latest` 充当发布身份。模板 Compose 中的 `:latest` 是示例值，实际发布需要由项目流水线明确替换。

| 环境 | `ASPNETCORE_ENVIRONMENT` | 非机密配置 | 机密 |
| --- | --- | --- | --- |
| 本机开发 | `Development` | `appsettings.Development.json`（随仓库提交，全队共用） | `dotnet user-secrets`（每人一份，见根 README「本地运行」） |
| 集成测试 | `Testing` | 测试夹具显式给出 | 测试夹具给出确定性的假值 |
| 测试 / 预发 | `Staging` | 环境变量，需要时加 `appsettings.Staging.json` | CI/CD 的 secret 注入为环境变量 |
| 生产 | `Production` | 环境变量与 `appsettings.Production.json` | 密钥管理系统，经环境变量或挂载文件注入 |

开发环境以外，只在单机上成立的回落一律缺配即启动失败：

- Data Protection 密钥必须落在 Redis 或共享持久目录（`DataProtection:KeysPath`），并随数据一同备份。存储位置应只允许本服务访问：Redis 不对外发布端口，跨主机或使用托管 Redis 时设口令并开启 TLS；目录用文件系统权限限制到运行身份。显式指定存储位置后框架不再自动加密密钥，需要静态加密时按官方 `ProtectKeysWith*` 在 `AddMyProjectDataProtection` 里追加。
<!--#if (OpenIddictServer)-->
- 令牌签名与加密证书必须显式提供（`OAuth:SigningCertificatePath`、`OAuth:EncryptionCertificatePath`），与 HTTPS 证书分开；开发证书只用于本机开发。
- TLS 在网关或 ingress 终结时，配置 `ForwardedHeaders:KnownProxies` / `KnownNetworks` 让应用还原原始协议，不要打开 `OAuth:DisableHttpsRequirement`——OpenIddict 明确要求生产环境即使在反向代理后也不关闭传输安全检查。
<!--#endif-->

## 本地验证

本机开发时，依赖服务用 `deploy/docker-compose.dev.yml` 起在本机（只绑定 127.0.0.1），应用用 `dotnet run` / `npm start` 跑。完整容器形态用生产 compose 验证：

```bash
cp deploy/.env.example deploy/.env    # 逐项填写，deploy/.env 已被 Git 忽略
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.override.yml up --build
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.override.yml down
```

命令和文件存在性应以当前项目为准。

## 部署形态

- **Docker Compose 单机**：机密写在 `deploy/.env` 或由流水线导出为环境变量，清单见 `deploy/.env.example`；必填项在 compose 里以 `${VAR:?}` 引用，漏填时带着变量名失败。
<!--#if (OpenIddictServer)-->
  令牌证书以 compose `secrets` 挂载到 `/run/secrets`，只有口令走环境变量。单机 compose 的文件型 secret 是按宿主机文件权限的绑定挂载，`mode`、`uid` 不生效：证书文件必须对容器用户（UID 1654）可读，例如 `chown 1654 certs/*.pfx && chmod 400 certs/*.pfx`，或 `chmod 640` 并把属组设为 1654。宿主机上 `600` 且属主 root 的证书会让 API 启动失败。
<!--#endif-->
- **容器用户**：API 与迁移镜像以镜像自带的非 root 用户（UID 1654）运行。挂进容器的文件要对它可读；Data Protection 密钥改用文件目录（`DataProtection:KeysPath`）而不是 Redis 时，挂载的目录要对它可写。
- **Kubernetes**：非机密配置放 ConfigMap，机密放 Secret，以环境变量（`Section__Key`）注入，证书类文件（如身份服务的令牌证书）以卷挂载。`DbMigrator` 作为发布前的一次性 Job（带 `--apply`），成功后再滚动发布 API；就绪与存活探针分别指向 `/api/health/ready` 与 `/api/health/live`。多副本必须配置 Redis，Data Protection 密钥与分布式锁都依赖它。
- **云平台（容器服务、应用服务）**：配置写应用设置，机密放托管密钥库并以托管身份读取（如 Key Vault 引用）。这类接入与平台绑定，确定平台后再加，不预置在模板里。

## 生产边界

- 密钥和生产凭据由环境变量或密钥管理系统提供，不写入仓库。
<!--#if (OpenIddictServer)-->
- 前端不与本服务同源时（如独立部署的资源服务前端经本服务登录），把它的源加入本服务的 `Cors:AllowedOrigins`：发现文档、JWKS、令牌与 userinfo 都是跨源请求。本机开发不需要，前端开发服务器已为 localhost 来源放行。
- 下游资源服务的 API 标识登记在 `OAuth:ApiResources`，与该服务的 `Authentication:Audience` 取同一个值。
- 各类撤销在下游资源服务上生效的时间不同。资源服务只验签，不回本服务查状态；Access Token 有效期在 `Program.cs` 里设为 10 分钟。
  - **停用或删除账号**：同时撤销该用户已签发的令牌，本服务立即拒绝；资源服务要等 Access Token 过期，之后也刷新不到新令牌。
  - **停用或删除租户**：本服务每个请求都查注册表，立即拒绝，刷新令牌也换不到新令牌；资源服务同样要等 Access Token 过期。
  - **撤销会话（含"退出其他设备"）**：只作废本服务的登录会话，不撤销该设备经授权拿到的令牌。客户端持有刷新令牌时，它对下游资源服务的访问不受影响：刷新令牌默认 14 天有效且滑动续期，持续刷新的客户端可以一直访问下去，调短 Access Token 有效期也改变不了这一点。需要一并撤销时，要把令牌与会话关联起来，模板未内置。

  停用或删除账号、租户之后无法再刷新，所以这两类的窗口可以靠调短 Access Token 有效期来缩短。资源服务改用令牌内省（`UseIntrospection()`）能让**已撤销的令牌**即时失效，所以对停用或删除账号有效；租户停用还要先在停用时撤销该租户的令牌才行。
<!--#endif-->
<!--#if (RemoteTokenAuth)-->
- 本服务只验证身份服务签发的令牌，不回去查账号与租户状态。身份服务那边停用账号、撤销会话、停用或删除租户，已签发的 Access Token 在本服务仍然有效，直到过期（有效期由身份服务决定）。各类撤销的完整边界见身份服务的部署文档。改用令牌内省（OpenIddict 验证端的 `UseIntrospection()`）只能让身份服务**已撤销的令牌**即时失效，代价是每个请求多一次往返。
<!--#endif-->
<!--#if (IncludeNotifications && LocalIdentity)-->
- 通知邮件要附可点开的站内链接时配置 `Leistd:Notifications:Email:PublicBaseUrl`（站点对外地址；哈希路由以 `/#` 结尾），不配则不附。
<!--#endif-->
- 操作记录默认**只增不减**。需要保留期时打开 `Leistd:OperationRecords:Retention:Enabled`（或由宿主管理员在系统设置的「审计」面板打开），
  到期记录会被搬进 `OperationRecordArchives` 表而不是删除；归档表不参与日常查询，但数据仍在库里，容量规划要把它算进去。
  配置里的开关与保留天数是基线，界面上的设置优先，归档任务每轮读取；执行时刻与批大小只在配置里。
  归档逐个进入宿主库与每个独立库，某个库失败只记错误、最迟在下一个调度时段重做（按截止时间扫描，积压会一并搬走）；
  多副本部署经分布式锁每轮只跑一份，要求配置 Redis，否则每个副本各跑一遍。
- **多个服务共用一个 Redis 时，每个服务的 `Leistd:Lock:Redis:KeyPrefix` 必须互不相同**（配置文件里给的是应用名，环境部分由部署侧追加）。
  周期任务的锁键是固定的作业名（如 `operation-records.archive`），前缀相同的两个服务会互相抢同一把锁——
  抢不到的那个当轮直接跳过自己的库，而两边日志各自都正常。
- 「只增不减」由应用契约保证。需要数据库层也保证时，把 Runtime Secret 对 `OperationRecords` 表的权限收敛到 INSERT／SELECT；
  代价是归档要从原表删除，**收敛权限与启用保留期不能同时成立**，二选一并在部署记录里写明。
- 操作记录表增长到按时间范围查询变慢时，再按时间分区（数据库层 DDL，由 `DbMigrator` 的 DDL 身份执行），不预先做。
<!--#if (LocalIdentity)-->
- 原始 IP 保存在三处，各有各的期限，合规评估按这三处分别写明：
  - **会话表**：会话过期后不再使用，由登录时的本人清理或每天的 `auth.sessions.cleanup` 作业删除（逐库执行，含停用租户的库），作业正常运行时最长比有效期多留约一天；
  - **操作记录**（会话撤销等记录的目标名里带 IP）：跟随操作记录的保留期，见上；
  - **用户的最近登录 IP**：是当前状态字段而非历史，随用户记录保留、不单设保留期。删除用户是软删除，**不会**清掉它；合规要求删除时须物理删除用户记录或显式清空该字段。
<!--#endif-->
- 模板不内置限流。对外提供登录的服务应在网关或 ASP.NET Core 限流中间件上按来源 IP 限流：
  应用内的失败登录计数按账号聚合，同一 IP 轮换账号的撞库不会触发它。
- API 启动时不执行 DDL。部署流水线先以 Migration Secret 运行一次性 `DbMigrator`，成功后再发布 API，详见 [后端迁移策略](../../backend/README.md#数据库迁移)。
- API 使用 Runtime Secret 且只持有 DML 权限；`DbMigrator` 使用独立 DDL 身份。部署前必须审查迁移，并明确备份、超时、失败恢复及新旧版本并存时的兼容性。
- **破坏性 schema 变更按 Expand → Backfill/Switch → Contract 三个有序阶段推进**，不在一次发布里完成。约束的是阶段顺序与下面两道闸门，不是发布次数——Backfill 可能是一次独立运维任务，也可能分多个批次：
  - `DbMigrator` 先于新 API 执行，此刻旧 Pod 仍在运行，因此 **Expand 阶段的迁移必须同时兼容当前生产版本和新版本**（只加不减：新增可空列、新增表、新增索引，不改语义、不删不改名）。
  - 全部目标迁移完成后，再发布新代码并完成 Backfill 或读写切换。
  - **确认旧版本实例全部退出后**，才在之后的受控发布里执行 Contract（删除旧列/旧表/兼容代码）。过渡结构只允许存在于这段窗口，不留长期历史兼容层。
  - 模板仓库 CI 用真实 PostgreSQL 验证从当前模板建库、迁移及租户隔离。具体项目发布前，需另用当前生产版本的数据副本验证「当前生产版本 → 新版本」的升级路径与迁移重跑；模板 CI 没有该项目的生产基线，不能替代这项验收。
- DedicatedDatabase 首次建库时，先对目标连接以 `ConnectionStrings__MigrationTarget` 运行每个服务的 DbMigrator，再在 Identity 创建租户。该模式只迁移当前服务的业务 schema，不会把 Identity Control schema 写入租户目标。
- 数据迁移、备份、回滚、健康检查和核心路径验证必须在执行前明确。
- 生产部署、回滚、重启、流量切换及真实数据操作前核对用户已有授权是否覆盖目标、环境和动作；未授权或范围变化时再确认。

只有项目已经确定真实环境和运维流程时，才在本目录新增或更新文档。先参考最新同类记录，没有可参考内容时与负责人确认最小必要信息，不创建服务器配置或部署报告模板。
