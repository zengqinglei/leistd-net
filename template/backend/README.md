# CompanyName.ProjectName 后端

后端基于 .NET 10 和 Leistd.* NuGet 包，采用单向依赖的 DDD 四层结构。

## 分层

```text
src/
|-- CompanyName.ProjectName.Domain/          # 领域模型和内层抽象
|-- CompanyName.ProjectName.Application/     # 用例编排，只依赖 Domain
|-- CompanyName.ProjectName.Infrastructure/  # EF Core 和外部适配器
`-- CompanyName.ProjectName.Api/             # 组合根和 HTTP 入口
```

`Api` 显式引用并组合 Application 与 Infrastructure；Application 不依赖 Infrastructure。

<!--#if (IncludeLocalization)-->
## 多语言边界

请求文化和资源解析只位于 Api 层：默认 `en-US`，支持 `zh-CN`，使用 .NET 内置 `AddLocalization`、`UseRequestLocalization` 和 `.resx`。Domain/Application 只抛出带稳定错误码、英文回退消息以及可选资源键的业务异常，不依赖 `IStringLocalizer`。客户端通过 `Accept-Language` 或 ASP.NET Core 文化 Cookie 选择语言，资源缺失时回退英文消息。
<!--#endif-->

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
GET http://localhost:5240/api/health
```

测试项目存在于 `tests/` 时执行：

```bash
dotnet test CompanyName.ProjectName.sln
```

## 数据库迁移

模板不携带迁移文件。应用在所有环境启动时按以下规则自动初始化关系型数据库：

- 已有迁移文件时执行 `MigrateAsync`，应用待执行迁移。
- 没有迁移文件时执行 `EnsureCreatedAsync`，按当前模型创建数据库。

修改 EF Core 模型后生成并审查迁移文件：

```bash
dotnet ef migrations add InitialCreate \
  --project src/CompanyName.ProjectName.Infrastructure \
  --startup-project src/CompanyName.ProjectName.Api \
  --output-dir Persistence/Migrations
```

重新启动应用即可应用迁移，无需另行执行 `dotnet ef database update`。由 `EnsureCreated` 创建的数据库没有迁移历史，不能直接切换为迁移管理；计划持续演进结构的数据库应在首次启动前包含初始迁移，否则后续启用迁移时需要重建数据库或制定基线方案。

生产部署会随应用启动自动创建或迁移数据库。共享或生产数据库必须在明确目标、模型或迁移内容、备份和失败恢复方案后再启动新版本，具体边界见 [部署说明](../docs/deploy/README.md)。

<!--#if (IncludeIdentity)-->
## 认证配置

首次启动会按 `DefaultAdmin` 创建管理员。生产环境必须覆盖默认密码，并持久化 Data Protection 密钥。
<!--#if (IncludeRoles)-->

角色、权限和超级管理员属于不同授权维度；业务接口应同时覆盖允许、拒绝和超级管理员旁路场景。
<!--#endif-->
<!--#if (IncludeOpenIddict)-->

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
