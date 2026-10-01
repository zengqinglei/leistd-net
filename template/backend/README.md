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

`Directory.Build.props` 统一声明 `LeistdFrameworkVersion`。使用 `Leistd.*` API 前，优先使用 `leistd-net-framework` Skill 按实际还原版本定位包内文档；未安装该 Skill 时，先用 `dotnet nuget locals global-packages --list` 找到 NuGet 缓存，再读取对应包版本的 `docs/*.md` 和 `lib/{tfm}/*.xml`。不要根据模型记忆猜测类型、签名或注册方法。

## 开发配置

启动配置使用 `ASPNETCORE_ENVIRONMENT=Development`（`DbMigrator` 为 `DOTNET_ENVIRONMENT=Development`）。配置按下表分层，后者覆盖前者：

| 层 | 放什么 | 是否进仓库 |
| --- | --- | --- |
| `appsettings.json` | 与环境无关的基线；凭据位置留空 | 是 |
| `appsettings.Development.json` | 所有开发者共用的开发配置：内存库名、开发证书开关、公开的演示管理员口令 | 是 |
| `dotnet user-secrets` | 机密与只属于本机的覆盖：本机连接串、连接共享库时的管理员口令 | 否，只在开发环境加载 |
| 环境变量 / 密钥系统 | 部署环境的全部机密与差异 | 否 |

Api 与 `DbMigrator` 共用同一个 `UserSecretsId`。配了 `ConnectionStrings:Default` 就走真实数据库，否则用 `Database:InMemoryName` 指定的内存库：

```bash
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=companyname-projectname;Username=postgres;Password=postgres" --project src/CompanyName.ProjectName.Api
dotnet user-secrets list --project src/CompanyName.ProjectName.Api
```

不要把密码、证书或生产连接字符串写进任何 `appsettings*.json`。集成测试宿主跑在 `Testing` 环境，不加载 user-secrets 与 `appsettings.Development.json`，所需配置由测试夹具显式给出。

开发环境以外，下列只在单机上成立的回落不再生效，缺配即启动失败：

- Data Protection 密钥必须持久化到共享位置：`ConnectionStrings:Redis`，或 `DataProtection:KeysPath` 指向 API 与 `DbMigrator` 共用的持久目录。存储位置应只允许本服务访问；需要对密钥做静态加密时，在 `AddMyProjectDataProtection` 里按官方 `ProtectKeysWith*` 追加。
<!--#if (OpenIddictServer)-->
- 令牌签名与加密证书默认必须显式提供：`OAuth:SigningCertificatePath`、`OAuth:EncryptionCertificatePath`（两张独立的 RSA 证书，口令由部署注入）。`OAuth:UseDevelopmentCertificates` 只用于本机开发，由 `appsettings.Development.json` 打开。
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

`DbMigrator` 先迁移服务默认目标，再从 Identity 获取 DedicatedDatabase 覆盖并按物理连接去重。每个服务使用自己的固定 schema 和迁移历史表。Identity 还会先迁移固定宿主库的 Control DbContext（租户、连接配置、OpenIddict），再迁移可按租户路由的业务 DbContext。

新建 DedicatedDatabase 租户前，运维流程必须先对新目标执行一次业务 schema 迁移，再在 Identity 中建立租户与 Secret Reference：

```bash
ConnectionStrings__MigrationTarget='<migration connection string>' \
  dotnet run --project src/CompanyName.ProjectName.DbMigrator -- --apply
```

`MigrationTarget` 模式只迁移该服务的业务 schema，不迁移 Identity Control schema，也不枚举已登记租户。它用于打破“先登记租户才能枚举目标、但租户初始化前又必须先有表”的首次建库循环；日常发布仍使用不带该配置的全目标模式。

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
- `OAuth:ApiResources` 列出由本服务签发令牌的下游 API，各登记为同名 scope，下游服务把自己的 `Authentication:Audience` 设为同一个值；
- 租户路由的两个 scope 只授予机器客户端；用户调用下游使用官方 Token Exchange，调用方 client ID 与来源 API 受众一致，目标由 aud:/scp: 应用权限授予。SPA 仅申请自己的 API scope，详见服务间调用规范。
<!--#endif-->
<!--#if (ExternalLogin)-->

外部登录凭据通过 `ExternalAuth` 配置或密钥系统提供，不写入仓库。每个提供商（`Github` / `Google`）保持
`ClientId`、`ClientSecret`、`RedirectUri` 三个原有配置键：三项全空表示不启用，启用时必须全部填写，且回调地址必须是绝对 HTTP(S) URI。
回调地址指向前端页面 `/auth/external-callback/{provider}`（如本机 `http://localhost:4200/auth/external-callback/github`），由它把授权码提交给 API；提供商后台登记的回调要与 `RedirectUri` 逐字一致，GitHub 连查询串一起比对。
配置不完整会在启动期被拒绝；这些适配器细节由 Infrastructure 绑定与校验，Application 只通过 `IOAuthProvider` 使用已配置的提供商。
<!--#endif-->
<!--#if (IncludeNotifications)-->

通知与业务实时事件共用实时 Hub（`AddNotificationsSignalR<RealTimeHub>()`，只映射 `MapRealTimeHub()`），前端只建一条连接。修改通知、订阅或资源鉴权时，应验证通知持久化、未读状态、通用订阅和越权拒绝。
<!--#endif-->
<!--#endif-->

## 前后端联调

本机开发时浏览器访问前端开发服务器（`http://localhost:4200`），由它把 API 与 Hub 请求转发到本服务，见[前端说明](../frontend/README.md)。
本服务不托管开发中的前端；部署时前端构建产物放在 `wwwroot`，由本服务同源托管。
