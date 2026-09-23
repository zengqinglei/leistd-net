# 部署说明

项目根 `Dockerfile` 和 `deploy/` 是当前容器部署配置的事实来源。部署前先核对实际镜像、环境变量、数据库迁移、健康检查和回滚能力，不使用文档中的假定值替代配置。

## 配置与机密的分层

各环境共用同一套键，只换值的来源；同一个镜像从测试环境晋升到生产，不按环境重新构建。

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
  令牌证书以 compose `secrets` 挂载到 `/run/secrets`，只有口令走环境变量。
<!--#endif-->
- **Kubernetes**：非机密配置放 ConfigMap，机密放 Secret，以环境变量（`Section__Key`）注入，证书类文件（如身份服务的令牌证书）以卷挂载。`DbMigrator` 作为发布前的一次性 Job（带 `--apply`），成功后再滚动发布 API；就绪与存活探针分别指向 `/api/health/ready` 与 `/api/health/live`。多副本必须配置 Redis，Data Protection 密钥与分布式锁都依赖它。
- **云平台（容器服务、应用服务）**：配置写应用设置，机密放托管密钥库并以托管身份读取（如 Key Vault 引用）。这类接入与平台绑定，确定平台后再加，不预置在模板里。

## 生产边界

- 密钥和生产凭据由环境变量或密钥管理系统提供，不写入仓库。
- 操作记录默认**只增不减**。需要保留期时打开 `Leistd:OperationRecords:Retention:Enabled`（或由宿主管理员在系统设置的「审计」面板打开），
  到期记录会被搬进 `OperationRecordArchives` 表而不是删除；归档表不参与日常查询，但数据仍在库里，容量规划要把它算进去。
  配置里的开关与保留天数是基线，界面上的设置优先，归档任务每轮读取；执行时刻与批大小只在配置里。
  归档逐个进入宿主库与每个独立库，某个库失败只记错误、最迟在下一个调度时段重做（按截止时间扫描，积压会一并搬走）；
  多副本部署经分布式锁每轮只跑一份，要求配置 Redis，否则每个副本各跑一遍。
- **多个服务共用一个 Redis 时，每个服务的 `Leistd:Lock:Redis:KeyPrefix` 必须互不相同**（配置文件里给的是应用名，环境部分由部署侧追加）。
  周期任务的锁键是固定的作业名（`operation-records.archive`、`notifications.retention`），前缀相同的两个服务会互相抢同一把锁——
  抢不到的那个当轮直接跳过自己的库，而两边日志各自都正常。
- 「只增不减」由应用契约保证。需要数据库层也保证时，把 Runtime Secret 对 `OperationRecords` 表的权限收敛到 INSERT／SELECT；
  代价是归档要从原表删除，**收敛权限与启用保留期不能同时成立**，二选一并在部署记录里写明。
- 操作记录表增长到按时间范围查询变慢时，再按时间分区（数据库层 DDL，由 `DbMigrator` 的 DDL 身份执行），不预先做。
- 模板不内置限流。对外提供登录的服务应在网关或 ASP.NET Core 限流中间件上按来源 IP 限流：
  应用内的失败登录计数按账号聚合，同一 IP 轮换账号的撞库不会触发它。
- API 启动时不执行 DDL。部署流水线先以 Migration Secret 运行一次性 `DbMigrator`，成功后再发布 API，详见 [后端迁移策略](../../backend/README.md#数据库迁移)。
- API 使用 Runtime Secret 且只持有 DML 权限；`DbMigrator` 使用独立 DDL 身份。部署前必须审查迁移，并明确备份、超时、失败恢复及新旧版本并存时的兼容性。
- **破坏性 schema 变更按 Expand → Backfill/Switch → Contract 三个有序阶段推进**，不在一次发布里完成。约束的是阶段顺序与下面两道闸门，不是发布次数——Backfill 可能是一次独立运维任务，也可能分多个批次：
  - `DbMigrator` 先于新 API 执行，此刻旧 Pod 仍在运行，因此 **Expand 阶段的迁移必须同时兼容当前生产版本和新版本**（只加不减：新增可空列、新增表、新增索引，不改语义、不删不改名）。
  - 全部目标迁移完成后，再发布新代码并完成 Backfill 或读写切换。
  - **确认旧版本实例全部退出后**，才在之后的受控发布里执行 Contract（删除旧列/旧表/兼容代码）。过渡结构只允许存在于这段窗口，不留长期历史兼容层。
  - CI 用真实数据库验证「当前生产版本 → 新版本」的升级路径，以及迁移重跑幂等。
- DedicatedDatabase 首次建库时，先对目标连接以 `ConnectionStrings__MigrationTarget` 运行每个服务的 DbMigrator，再在 Identity 创建租户。该模式只迁移当前服务的业务 schema，不会把 Identity Control schema 写入租户目标。
- 数据迁移、备份、回滚、健康检查和核心路径验证必须在执行前明确。
- 生产部署、回滚、重启、流量切换及真实数据操作必须由用户明确确认。

只有项目已经确定真实环境和运维流程时，才在本目录新增或更新文档。先参考最新同类记录，没有可参考内容时与负责人确认最小必要信息，不创建服务器配置或部署报告模板。
