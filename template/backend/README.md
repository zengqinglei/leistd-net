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
| `appsettings.Development.json` | 非机密的开发配置：内存库名、开发证书开关 | 是 |
| `dotnet user-secrets` | 机密与只属于本机的覆盖：管理员口令、本机连接串、SPA 代理开关 | 否，只在开发环境加载 |
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
dotnet run --project src/CompanyName.ProjectName.DbMigrator
dotnet run --project src/CompanyName.ProjectName.Api
```

`DbMigrator` 先迁移服务默认目标，再从 Identity 获取 DedicatedDatabase 覆盖并按物理连接去重。每个服务使用自己的固定 schema 和迁移历史表。Identity 还会先迁移固定宿主库的 Control DbContext（租户、连接配置、OpenIddict），再迁移可按租户路由的业务 DbContext。

新建 DedicatedDatabase 租户前，运维流程必须先对新目标执行一次业务 schema 迁移，再在 Identity 中建立租户与 Secret Reference：

```bash
ConnectionStrings__MigrationTarget='<migration connection string>' \
  dotnet run --project src/CompanyName.ProjectName.DbMigrator
```

`MigrationTarget` 模式只迁移该服务的业务 schema，不迁移 Identity Control schema，也不枚举已登记租户。它用于打破“先登记租户才能枚举目标、但租户初始化前又必须先有表”的首次建库循环；日常发布仍使用不带该配置的全目标模式。

修改 EF Core 模型后生成并审查迁移文件：

```bash
dotnet ef migrations add <MigrationName> \
  --context MyProjectDbContext \
  --project src/CompanyName.ProjectName.Infrastructure \
  --startup-project src/CompanyName.ProjectName.Api \
  --output-dir Persistence/Migrations/Identity
```

生产数据库身份必须分离：API 使用 Runtime Secret 且不得执行 DDL，`DbMigrator` 使用 Migration Secret。任一目标迁移失败时进程以非零码退出并阻断发布，具体边界见 [部署说明](../docs/deploy/README.md)。

<!--#if (LocalIdentity)-->
## 认证配置

首次启动会按 `DefaultAdmin` 创建管理员。**`DefaultAdmin:Password` 没有默认值**，必须由部署注入（环境变量或 `dotnet user-secrets`）——缺失、空值或不满足密码策略（至少 12 个字符）都会在启动期被拒绝。另需持久化 Data Protection 密钥。

角色、权限和超级管理员属于不同授权维度；业务接口应同时覆盖允许、拒绝和超级管理员旁路场景。
<!--#if (OpenIddictServer)-->

OpenIddict 的 issuer、证书和 HTTPS 要求通过 `OAuth` 配置；开发证书不得用于生产。
<!--#endif-->
<!--#if (ExternalLogin)-->

外部登录凭据通过 `ExternalAuth` 配置或密钥系统提供，不写入仓库。每个提供商（`Github` / `Google`）保持
`ClientId`、`ClientSecret`、`RedirectUri` 三个原有配置键：三项全空表示不启用，启用时必须全部填写，且回调地址必须是绝对 HTTP(S) URI。
配置不完整会在启动期被拒绝；这些适配器细节由 Infrastructure 绑定与校验，Application 只通过 `IOAuthProvider` 使用已配置的提供商。
<!--#endif-->
<!--#if (IncludeNotifications)-->

通知 Hub 与业务实时 Hub 分别注册和映射。修改通知、订阅或资源鉴权时，应验证通知持久化、未读状态、通用订阅和越权拒绝。
<!--#endif-->
<!--#endif-->

## 前后端联调

同源开发时启用 SPA 代理（本机偏好，放 user-secrets）：

```bash
dotnet user-secrets set "SpaProxy:Enabled" "true" --project src/CompanyName.ProjectName.Api
dotnet user-secrets set "SpaProxy:Target" "http://localhost:4200" --project src/CompanyName.ProjectName.Api
```

也可以让前端直接访问后端，并同样在 user-secrets 里设置 `Cors:AllowAnyLocalhost=true`。
