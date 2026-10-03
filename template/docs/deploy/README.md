# 部署说明

项目根 `Dockerfile` 和 `deploy/` 是当前容器部署配置的事实来源。部署前先核对实际镜像、环境变量、数据库迁移、健康检查和回滚能力，不使用文档中的假定值替代配置。

## 配置与机密的分层

各环境共用同一套配置键，只换值的来源。浏览器页面与所属 API 必须同源，支持同镜像托管，也支持分进程经部署代理统一外部源。认证导航、`/api/**` 协议回调与前端回跳都以该外部源为准；独立跨源 API 地址不属于模板浏览器认证的部署契约。`API_GATEWAY` 构建参数与 `environment.api.gateway` 保持空值，以相对路径访问同源 API；其他服务通过同源微服务路由前缀访问。

会话 Cookie 默认 `SameSite=Lax`。外部 OAuth correlation 与 OIDC nonce Cookie 保持官方 `SameSite=None`、`Secure=Always`，协议回调须 HTTPS。放宽会话 Cookie 到 `None` 不会补齐跨源浏览器认证导航。
<!--#if (OpenIddictServer)-->

第三方站点以顶层 POST 进入授权或退出端点时不需要放宽 SameSite：请求先缓存，再以顶层 GET 重入，Lax 会话 Cookie 即可送达（见 [依赖方的登录与退出](../standards/api.md#依赖方的登录与退出)）。
<!--#endif-->

口令哈希默认 PBKDF2-HMAC-SHA256 600,000 次迭代（OWASP 现行建议）。硬件基准表明可以承受更高成本时用 `PasswordHash__IterationCount` 调高；新值只作用于此后设置或修改的口令，存量密文按自身记录的迭代数校验，照常可用。
<!--#if (OpenIddictServer)-->

多系统退出不会即时通知其他依赖方：模板没有 OIDC back-channel logout。登记为会话绑定的依赖方在 Identity 会话结束后，于访问令牌到期时续期失败而收敛；未绑定的依赖方持有的刷新令牌不受影响。

授权与退出请求缓存在控制库的令牌表里（request token），重新认证证明、退出确认凭据与 Cookie 由 Data Protection 保护；重新认证证明比较签发时刻与会话开始时间，各副本的时钟须同步。多实例 Identity 之间一次登录或退出可能落在不同副本，因此除共享缓存外，还要共享同一个控制库、同一套令牌证书、同一个 Data Protection 密钥环与应用名，以及分布式锁。修改开放应用登记后滚动重启 Identity：OpenIddict 应用缓存只在本进程失效。
<!--#endif-->

部署时记录 API 和 DbMigrator 的固定版本或 digest，不用 `latest` 充当发布身份。模板 Compose 中的 `:latest` 是示例值，实际发布需要由项目流水线明确替换。

| 环境 | `ASPNETCORE_ENVIRONMENT` | 非机密配置 | 机密 |
| --- | --- | --- | --- |
| 本机开发 | `Development` | `appsettings.Development.json`（随仓库提交，全队共用） | `dotnet user-secrets`（每人一份，见根 README「本地运行」） |
| 集成测试 | `Testing` | 测试夹具显式给出 | 测试夹具给出确定性的假值 |
| 测试 / 预发 | `Staging` | 环境变量，需要时加 `appsettings.Staging.json` | CI/CD 的 secret 注入为环境变量 |
| 生产 | `Production` | 环境变量与 `appsettings.Production.json` | 密钥管理系统，经环境变量或挂载文件注入 |

开发环境以外，下列只在单机上成立的回落缺配即启动失败：

- Data Protection 密钥必须落在 Redis 或共享持久目录（`DataProtection:KeysPath`），并随数据一同备份。`KeysPath` 只替代密钥的存储位置，不替代 Redis 承载的分布式缓存与锁。存储位置应只允许本服务访问：Redis 不对外发布端口，跨主机或使用托管 Redis 时设口令并开启 TLS；目录用文件系统权限限制到运行身份。显式指定存储位置后框架不再自动加密密钥，需要静态加密时按官方 `ProtectKeysWith*` 在 `AddMyProjectDataProtection` 里追加。
- 未配置 `ConnectionStrings:Redis` 不阻止启动：单实例配 `KeysPath` 是合法部署。此时分布式缓存与分布式锁回落到进程内存，只适合单实例，启动日志各有一条 Warning 说明降级内容；多副本必须配置 Redis。
- TLS 在网关或 ingress 终结时，所有形态（含 Standalone）都须配置 `ForwardedHeaders:KnownProxies` / `KnownNetworks` 还原原始协议与主机。浏览器写请求的 Origin 校验同样依赖这些转发头；来源拒绝返回 Problem Details，Warning 日志给出收到的 Origin 与计算出的本源。
<!--#if (OpenIddictServer)-->
- 令牌签名与加密证书必须显式提供（`OAuth:SigningCertificates`、`OAuth:EncryptionCertificates` 各至少一项，每项 `Path`、`Password`），与 HTTPS 证书分开；开发证书只用于本机开发。任何一项缺路径、无法加载或没有 RSA 私钥，启动失败并指出带下标的键名。
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
- **Kubernetes**：非机密配置放 ConfigMap，机密放 Secret，以环境变量（`Section__Key`）注入，证书类文件（如身份服务的令牌证书）以卷挂载。`DbMigrator` 作为发布前的一次性 Job（带 `--apply`），成功后再滚动发布 API；就绪与存活探针分别指向 `/api/health/ready` 与 `/api/health/live`。多副本必须配置 Redis：分布式缓存（会话票据等）与分布式锁依赖它，Data Protection 密钥也默认存在那里。
- **云平台（容器服务、应用服务）**：配置写应用设置，机密放托管密钥库并以托管身份读取（如 Key Vault 引用）。这类接入与平台绑定，确定平台后再加，不预置在模板里。

<!--#if (OpenIddictServer)-->
## 令牌证书轮换

签名与加密证书都按集合登记，轮换期间新旧同时在册（重叠轮换），不要直接替换文件硬切。OpenIddict 把全部签名证书发布进 JWKS；
签名时优先用已生效且到期最晚的一张，尚未生效（`NotBefore` 在未来）的排在后面（顺序在启动时排定）；加密用优先的一张，解密时全部可用。
集合里至少要有一张当前有效的签名证书与加密证书，否则 OpenIddict 拒绝启动。

1. **发布新公钥**：新签名证书的 `NotBefore` 设在未来（留出第 2 步的时间）、`NotAfter` 晚于旧证书、使用新的 kid，作为新的一项加入
   `OAuth:SigningCertificates` 后滚动重启 Identity。此时 JWKS 已含新 kid，签名仍用旧证书。
2. **确认各资源服务已取得新 kid**：资源服务有两条各自缓存的配置链路——OpenIddict 验证（访问令牌：Bearer、登录回调、服务端续期）
   与 ASP.NET Core OIDC 处理器（id_token）。缓存过期（默认 12 小时）后要等下一个请求才会重新抓取，单靠时间过去不能确认，
   所以滚动重启各资源服务副本，并经真实链路（一次 Bearer 请求、一次登录）预热，确认每个副本、两条链路都已取得新 kid。
3. **切换签发**：新证书过了 `NotBefore` 后滚动重启 Identity。证书的优先顺序在启动时排定，不重启就一直用旧证书签名。
4. **撤掉旧证书**：授权码、刷新令牌（默认 14 天）与授权、退出请求的 request token 由 Identity 自己签名并加密，
   兑现时既要验签也要解密，所以旧的签名证书和加密证书都要保留到它们全部过期。旧签名证书还要覆盖访问令牌（10 分钟）
   与依赖方保存的 id_token：后者在退出时作为 `id_token_hint` 回到 Identity，验不了签会让退出失败，所以至少保留到依赖方会话的最长寿命。
   撤掉时从集合中删除该项并滚动重启。

第 2 步漏做或资源服务在窗口内才启动时，资源服务遇到未知 kid 会先向配置的签发方刷新一次再验证：访问令牌链路合并并发、
抓取超时 10 秒、请求刷新每分钟至多一次，id_token 链路由 IdentityModel 自身刷新重试一次，因此通常只多一次网络往返；
但签发方不可达、或刚刷新过（IdentityModel 的 5 分钟间隔、本服务的 1 分钟限频）时，这些请求仍会以 401 失败，
不能把它当作省略第 2 步的理由。

签发方证书泄露时不走重叠流程：从集合中移除并重启 Identity，用它签发、加密的授权码、刷新令牌与 request token 在 Identity 上随即失效。
资源服务已经缓存了这张证书的公钥，认得的 kid 不会触发刷新，只重启 Identity 不能让它们拒绝旧签名的访问令牌：
要接着滚动重启各资源服务副本（或等缓存过期后的首次抓取），并用旧证书签的令牌确认每个副本都已拒绝。

<!--#endif-->
## 生产边界

- 密钥和生产凭据由环境变量或密钥管理系统提供，不写入仓库。
<!--#if (OpenIddictServer)-->
- 浏览器整页进入授权端点，发现文档、JWKS、令牌与 UserInfo 由 Resource 后端访问，不要求为这些服务端协议通信开放浏览器 CORS。本机 Angular 开发代理将 `/api/**` 转到 API。
  生产 API 的 `Cors:AllowedOrigins` 默认为空；仅供显式需要的其他 API 集成使用，不负责浏览器认证导航。
- 下游资源服务的 API 标识以对象登记在 `OAuth:ApiResources` 的 Name，与该服务的 `Authentication:Audience` 相同；Scope 与 OwnerClientId 可独立配置，默认资源名。
- 各类撤销在下游资源服务上生效的时间不同。资源服务只验签，不回本服务查状态；Access Token 有效期在 `Program.cs` 里设为 10 分钟。
  - **停用或删除账号**：同时撤销该用户已签发的令牌，本服务立即拒绝；资源服务要等 Access Token 过期，之后也刷新不到新令牌。
  - **停用或删除租户**：本服务每个请求都查注册表，立即拒绝，刷新令牌也换不到新令牌；资源服务同样要等 Access Token 过期。
  - **撤销会话（含"退出其他设备"、退出登录与空闲到期）**：作废本服务的登录会话，不撤销已签发的访问令牌。之后是否还能刷新取决于开放应用的"会话绑定"：绑定的客户端（授权时记下了会话）刷新随即被拒，下游访问在访问令牌到期时结束；未绑定的客户端不受影响，刷新令牌默认 14 天有效且滑动续期，持续刷新可以一直访问下去，调短 Access Token 有效期也改变不了这一点。

  停用或删除账号、租户之后无法再刷新，所以这两类的窗口可以靠调短 Access Token 有效期来缩短。资源服务改用令牌内省（`UseIntrospection()`）能让**已撤销的令牌**即时失效，所以对停用或删除账号有效；租户停用还要先在停用时撤销该租户的令牌才行。
<!--#endif-->
<!--#if (RemoteTokenAuth)-->
- 跨服务客户端、Issuer、Audience 与机器身份配置见 [服务间调用](../standards/service-invocation.md#identity-与资源服务对接)。
- readiness 是启动门禁：首次成功解析发现文档、精确比对 issuer 并取得非空 JWKS 后锁存成功。
  冷实例在 Identity 不可达时 `/api/health/ready` 返回 503，`/api/health/live` 仍为 200；
  日志指出发现文档或签名密钥依赖，Identity 恢复后自动重试并进入就绪。
  热实例保持就绪；只有 OpenIddict 验证器已缓存所需密钥时，才能在 Identity 停机期间继续本地验签。
  该探针不表示 Identity 持续可达，也不检查租户路由。
  采用 [ASP.NET Core 分离 readiness/liveness 的启动任务示例](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0#separate-readiness-and-liveness-probes)的门禁语义，
  不周期探测共享 Identity，避免其短暂故障使所有资源实例同时摘流。
  代价是就绪不保证新密钥获取、新令牌获取或租户回源成功，这些依赖失败在对应请求上暴露。
- 浏览器使用后端 OIDC 机密客户端与 Cookie 会话，服务端保存并刷新令牌；配置、回调、缓存/密钥与 CSRF 边界见 [浏览器认证](../standards/api.md#浏览器认证)。
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
