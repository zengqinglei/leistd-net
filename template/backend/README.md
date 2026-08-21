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

启动配置使用 `ASPNETCORE_ENVIRONMENT=Development`。本地私有配置写入被 Git 忽略的：

```text
src/CompanyName.ProjectName.Api/appsettings.Development.json
```

最小 PostgreSQL 配置：

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Database=companyname-projectname;Username=postgres;Password=postgres"
  },
  "Cors": {
    "AllowAnyLocalhost": true
  }
}
```

连接字符串为空时使用内存数据库。不要提交密码、证书或生产连接字符串。

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

<!--#if (IdentityService)-->
## 认证配置

首次启动会按 `DefaultAdmin` 创建管理员。生产环境必须覆盖默认密码，并持久化 Data Protection 密钥。
<!--#if (LocalAuthorization)-->

角色、权限和超级管理员属于不同授权维度；业务接口应同时覆盖允许、拒绝和超级管理员旁路场景。
<!--#endif-->
<!--#if (IdentityService)-->

OpenIddict 的 issuer、证书和 HTTPS 要求通过 `OAuth` 配置；开发证书不得用于生产。
<!--#endif-->
<!--#if (IncludeExternalLogin)-->

外部登录凭据通过 `ExternalAuth` 配置或密钥系统提供，不写入仓库。
<!--#endif-->
<!--#if (IncludeNotifications)-->

通知 Hub 与业务实时 Hub 分别注册和映射。修改通知、订阅或资源鉴权时，应验证通知持久化、未读状态、通用订阅和越权拒绝。
<!--#endif-->
<!--#endif-->

## 前后端联调

同源开发时，在 `appsettings.Development.json` 启用 SPA 代理：

```json
{
  "SpaProxy": {
    "Enabled": true,
    "Target": "http://localhost:4200"
  }
}
```

也可以让前端直接访问后端，并在仅限开发环境的配置中设置 `Cors:AllowAnyLocalhost=true`。
