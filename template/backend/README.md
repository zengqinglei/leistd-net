# CompanyName.ProjectName 后端

后端基于 .NET 10 和 Leistd.* NuGet 包，采用单向依赖的 DDD 四层结构。

## 分层

```text
src/
|-- CompanyName.ProjectName.Domain/          # 领域模型和内层抽象
|-- CompanyName.ProjectName.Application/     # 用例编排，只依赖 Domain
|-- CompanyName.ProjectName.Infrastructure/  # EF Core 和外部适配器
|-- CompanyName.ProjectName.Client/          # 供其他服务消费的强类型 SDK
|-- CompanyName.ProjectName.DbMigrator/      # 独立、一次性数据库迁移入口
`-- CompanyName.ProjectName.Api/             # 组合根和 HTTP 入口
```

`Api` 显式引用并组合 Application 与 Infrastructure；Application 不依赖 Infrastructure。

## Leistd 框架 API

`Directory.Build.props` 统一声明 `LeistdFrameworkVersion`。使用 `Leistd.*` API 前，优先使用 `leistd-net-framework` Skill 按实际还原版本定位包内文档。未安装该 Skill 时：

1. 从 `obj/project.assets.json` 或 `dotnet list package --include-transitive` 确认实际还原的包与版本；
2. 用 `dotnet nuget locals global-packages --list` 找到 NuGet 缓存根目录；
3. 读取 `{缓存根}/{小写包名}/{版本}/docs/*.md`，精确签名以匹配项目 `TargetFramework` 的 `lib/{tfm}/{包名}.xml` 为准。

不读缓存中其他版本的文档，也不根据模型记忆猜测类型、签名或注册方法。

## 开发配置

启动配置使用 `ASPNETCORE_ENVIRONMENT=Development`（`DbMigrator` 为 `DOTNET_ENVIRONMENT=Development`）。配置按下表分层，后者覆盖前者：

| 层 | 放什么 | 是否进仓库 |
| --- | --- | --- |
| `appsettings.json` | 与环境无关的基线；凭据位置留空 | 是 |
| `appsettings.Development.json` | 所有开发者共用的开发配置：指向本机开发 compose 的连接串、开发证书开关、公开的演示管理员口令 | 是 |
| `dotnet user-secrets` | 机密与只属于本机的覆盖：共享开发库的连接串、连接共享库时的管理员口令、机器客户端密钥 | 否，只在开发环境加载 |
| 环境变量 / 密钥系统 | 部署环境的全部机密与差异 | 否 |

Api 与 `DbMigrator` 共用同一个 `UserSecretsId`，两边各有一份指向 `deploy/docker-compose.dev.yml` 本机库的开发连接串。连接其他开发库时用 user-secrets 覆盖，两边都会读到：

```bash
dotnet user-secrets set "ConnectionStrings:Default" "<共享开发库的连接串>" --project src/CompanyName.ProjectName.Api
dotnet user-secrets list --project src/CompanyName.ProjectName.Api
```

除开发 compose 的本机口令与演示管理员口令这类公开的本机开发值外，不要把密码、证书或生产连接字符串写进任何 `appsettings*.json`。集成测试宿主跑在 `Testing` 环境，不加载 user-secrets 与 `appsettings.Development.json`，所需配置由测试夹具显式给出；数据库由夹具在 Docker 里起 PostgreSQL（Testcontainers），运行集成测试需要本机 Docker。

开发环境以外，下列只在单机上成立的回落不再生效，缺配即启动失败：

<!--#if (SpaFrontend || (LocalIdentity && IncludeMultiTenancy))-->
- Data Protection 密钥必须持久化到共享位置：`ConnectionStrings:Redis`，或 `DataProtection:KeysPath` 指向持久目录。存储位置应只允许本服务访问；需要对密钥做静态加密时，在 `AddMyProjectDataProtection` 里按官方 `ProtectKeysWith*` 追加。
<!--#endif-->
<!--#if (LocalIdentity && IncludeMultiTenancy)-->
- 控制库使用 Data Protection 加密租户连接串，API 与 `DbMigrator` 必须共用密钥环与应用名。
<!--#endif-->
<!--#if (OpenIddictServer)-->
- 令牌签名与加密证书默认必须显式提供：`OAuth:SigningCertificates` 与 `OAuth:EncryptionCertificates` 各至少一项（每项 `Path`、`Password`，RSA 证书，与 HTTPS 证书分开，口令由部署注入）；轮换时新旧同时登记，步骤见部署文档。`OAuth:UseDevelopmentCertificates` 只用于本机开发，由 `appsettings.Development.json` 打开。
<!--#endif-->

## 启动与验证

```bash
dotnet restore CompanyName.ProjectName.sln
dotnet build CompanyName.ProjectName.sln
dotnet run --project src/CompanyName.ProjectName.Api
```

启动后检查：

```text
GET http://localhost:5240/api/health/live
GET http://localhost:5240/api/health/ready
```

测试项目存在于 `tests/` 时执行：

```bash
dotnet test CompanyName.ProjectName.sln
```

## 数据库迁移

模板携带可审查的基线迁移。API 启动时不自动迁移，也不使用 `EnsureCreatedAsync`；发布流水线必须先以 DDL 身份运行独立 `DbMigrator`，成功后再启动仅持有 DML 权限的 API：

```bash
dotnet run --project src/CompanyName.ProjectName.DbMigrator -- --apply
dotnet run --project src/CompanyName.ProjectName.Api
```

`DbMigrator` 不带 `--apply` 时只读预演（列出待执行迁移与 SQL，不改库）。

默认目标始终执行业务迁移；OIDC 存储由 Identity 的独立 DbContext 管理。
<!--#if (IncludeMultiTenancy)-->
`DbMigrator` 先迁移服务默认目标，再从 Identity 获取 DedicatedDatabase 覆盖并按物理连接去重。每个服务使用自己的固定 schema 和迁移历史表。Identity 还会先迁移固定宿主库的 Control DbContext（租户、连接配置、OpenIddict），再迁移可按租户路由的业务 DbContext。
单个租户取不出连接或它的库迁移失败时，其余库照常迁移，作业最后逐个报出并以非零退出，详见[模板说明](../README.md)。

新建 DedicatedDatabase 租户前，运维流程必须先对新目标执行一次业务 schema 迁移，再在 Identity 中建立租户与 Secret Reference：

```bash
ConnectionStrings__MigrationTarget='<migration connection string>' \
  dotnet run --project src/CompanyName.ProjectName.DbMigrator -- --apply
```

`MigrationTarget` 模式只迁移该服务的业务 schema，不迁移 Identity Control schema，也不枚举已登记租户。它用于打破“先登记租户才能枚举目标、但租户初始化前又必须先有表”的首次建库循环；日常发布仍使用不带该配置的全目标模式。

<!--#endif-->

修改 EF Core 模型后生成并审查迁移文件：

```bash
dotnet ef migrations add <MigrationName> \
  --context MyProjectDbContext \
  --project src/CompanyName.ProjectName.Infrastructure \
  --startup-project src/CompanyName.ProjectName.Api \
<!--#if (LocalIdentity)-->
  --output-dir Persistence/Migrations/Identity
<!--#else-->
  --output-dir Persistence/Migrations/Resource
<!--#endif-->
```

漏生成迁移时，单元测试 `MigrationSnapshotTests` 会失败：它不连库，按关系型模型与已提交的迁移快照比对。新增 DbContext 时在其中补一条对应断言。

生产数据库身份必须分离：API 使用 Runtime Secret 且不得执行 DDL，`DbMigrator` 使用 Migration Secret。任一目标迁移失败时进程以非零码退出并阻断发布，具体边界见 [部署说明](../docs/deploy/README.md)。
<!--#if (LocalIdentity)-->

## 认证配置

库里还没有超级管理员时，启动会按 `DefaultAdmin` 创建一个。`DefaultAdmin:Password` 在基础配置里没有默认值，只在创建那一刻校验：缺失或不满足密码策略（至少 12 个字符）即启动失败并报出键名；已有管理员的部署不必再提供。开发环境由 `appsettings.Development.json` 提供公开的演示口令，连接共享库前用 user-secrets 覆盖；部署经环境变量注入。另需持久化 Data Protection 密钥。

角色、权限和超级管理员属于不同授权维度；业务接口应同时覆盖允许、拒绝和超级管理员旁路场景。
<!--#if (OpenIddictServer)-->

OpenIddict 的 issuer、证书和 HTTPS 要求通过 `OAuth` 配置；开发证书不得用于生产。

访问令牌的受众由授予的 scope 推出，能签发哪些 scope 只由 `Application/Auth/OAuth/OAuthScopes.cs` 定义（服务端登记、scope 表、开放应用的权限校验都读它）：

- `OAuth:Resource` 是本服务 API 的标识，同名登记为 scope。调用本服务 API 的客户端要被授予并申请它，本服务只接受受众是它的令牌；
- `OAuth:ApiResources` 列出由本服务签发令牌的下游 API，每项用 Name、Scope、OwnerClientId 表达资源与归属，Scope/OwnerClientId 默认 Name；下游服务把自己的 `Authentication:Audience` 设为资源 Name；
- 租户路由的两个 scope 只授予机器客户端；用户调用下游使用官方 Token Exchange，调用方必须拥有来源 API 受众，目标由 aud:/scp: 应用权限授予。浏览器的机密依赖方在服务端申请 API scope，详见服务间调用规范。
<!--#endif-->
<!--#if (ExternalLogin)-->

外部登录凭据通过 `ExternalAuth` 配置或密钥系统提供，不写入仓库。每个提供商（`Github` / `Google`）只配置
`ClientId`、`ClientSecret`：两项全空表示不启用，部分填写在启动期报出缺失键名。
提供商后台登记后端 HTTPS 回调 `/api/v1/external-auth/{github,google}/signin`，由官方处理器在后端验证 code/state。
组合根直接使用 AddGoogle、AddGitHub 与短时服务端外部票据；Application 接收规范化 ExternalUserInfo 与可用提供商名称，不读取适配器凭据。
完整流程、Cookie 与部署规则见 [浏览器认证](../docs/standards/auth.md#浏览器认证)。
<!--#endif-->
<!--#if (IncludeNotifications)-->

通知持久化、未读状态及实时送达由通知组件提供；业务实时开启时共享一个 Hub 和一条前端连接。修改订阅时验证实际资源权限与作用域隔离。
<!--#endif-->
<!--#endif-->

<!--#if (RemoteTokenAuth)-->

## 资源管理员首次授予

在完成迁移后，以部署授权执行正式引导命令，把远端用户加入 Admin 角色；`sub` 必须为远端自然人的非空 GUID，允许在其首次访问之前执行：

```bash
dotnet run --project src/CompanyName.ProjectName.DbMigrator -- --grant-admin <sub>
dotnet run --project src/CompanyName.ProjectName.DbMigrator -- --grant-admin <sub> --apply
<!--#if (IncludeMultiTenancy)-->
dotnet run --project src/CompanyName.ProjectName.DbMigrator -- --grant-admin <sub> --tenant <tenant-id> --apply
<!--#endif-->
```

默认 dry-run 不改变任何数据或操作记录，只说明将要做的事；`--apply` 在持有初始化锁的同一事务内完成：首次初始化角色（Admin 角色首次播种当前作用域全部权限，之后按普通角色管理），本地还没有该用户时建立只含主体标识的最小用户行（首次访问时按令牌补齐资料），再把该用户加入 Admin 角色。已有用户不改资料与启停，已删除的用户不恢复。

权限经角色取得：之后调整 Admin 角色的权限、或把此人移出 Admin，都按普通角色管理生效。此人已在 Admin 中时重复执行无操作；曾被移出时也不再加回，要恢复走角色管理。记录来源是部署身份，授权依据为 `DeploymentBootstrap`。
<!--#if (IncludeMultiTenancy)-->
租户必须已经登记，目标连接经正式路由解析。回源机器身份需要租户路由读取权限。
<!--#else-->
此应用只接受宿主目标，`--tenant` 参数会被拒绝。
<!--#endif-->
<!--#if (IncludeOperationRecords)-->
授权与成功操作记录在同一数据库事务内持久化。
<!--#else-->
成功安全记录在提交之后输出；提交后、输出前进程退出仍可能丢记录。
<!--#endif-->
<!--#endif-->

<!--#if (SpaFrontend)-->
## 前后端联调

本机开发时浏览器访问前端开发服务器（`http://localhost:4200`），由它把 API 与 Hub 请求转发到本服务，见[前端说明](../frontend/README.md)。
本服务不托管开发中的前端；部署时前端构建产物放在 `wwwroot`，由本服务同源托管。
<!--#endif-->
