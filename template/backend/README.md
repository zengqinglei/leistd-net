# 后端项目

基于 .NET 10.0 与自研 Leistd 框架构建的应用程序，采用整洁架构（DDD）原则，内置基于 OpenIddict 的认证授权、EF Core 数据访问与本地开发 SPA 代理。

---

## 架构

项目遵循整洁架构，职责清晰分离：

```
backend/
├── src/
│   ├── CompanyName.ProjectName.Api/              # 表现层（REST API）
│   ├── CompanyName.ProjectName.Application/      # 应用层（服务、初始化）
│   ├── CompanyName.ProjectName.Domain/           # 领域层（实体、领域服务）
│   └── CompanyName.ProjectName.Infrastructure/   # 基础设施层（EF Core、OAuth、Redis）
├── Directory.Build.props          # 框架包版本（LeistdFrameworkVersion）
└── Directory.Packages.props       # 中央包管理（CPM）：Leistd.* 与第三方包版本
```

> **Leistd 框架引用**：共享组件（`components`）与 DDD 基础层（`ddd-struct`）已抽取为独立的
> [Leistd 框架](../../framework/README.md)，以 **NuGet 包**形式通过 `PackageReference` 引用，不随项目源码分发。
> 生成项目不会内置本仓库的本地 NuGet 源，默认使用用户环境中的 NuGet 源（通常是 `nuget.org`）。
> 需要本地联调"改过的框架"时临时指定 `local-feed`，见[模板文档 · 联调本地框架](../README.md#联调本地框架开发者)。

---

## 环境准备

开始之前，请确保已安装：

- **.NET SDK**: 10.0+
- **PostgreSQL**: 15+（可选，未配置时使用内存存储）
- **Redis**: 7+（可选，未配置时使用内存缓存）

验证安装：
```bash
dotnet --version
```

---

## 快速开始

### 1. 克隆并导航

```bash
cd backend/src/CompanyName.ProjectName.Api
```

### 2. 配置应用

本地开发的环境名为 `debug`（见 `Properties/launchSettings.json`），创建 `appsettings.debug.json` 覆盖本地配置（该文件已被 `.gitignore` 忽略，不会入库）：

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Port={pg-port};Database=CompanyName.ProjectName;Username=postgres;Password=YourPassword",
    "Redis": "127.0.0.1:6379"
  },
  "SpaProxy": {
    "Enabled": true,
    "Target": "http://localhost:4200"
  },
  "Cors": {
    "AllowAnyLocalhost": false,
    "AllowedOrigins": []
  }
}
```

> **注意**：
> - `ConnectionStrings.Default` 为可选配置。未配置（留空）时应用使用内存存储，重启即丢。`{pg-port}` 按本机 PostgreSQL 实际端口填写（默认 5432）。
> - `Redis` 可选，未配置时自动回退内存缓存。
> - `SpaProxy` / `Cors` 的取舍见下方「前后端联调」。
> - 默认管理员账号等开发默认值在 `appsettings.json` 中，生产环境务必用环境变量覆盖。认证基于 OpenIddict + Cookie，无需配置 JWT 密钥。

### 3. 前后端联调（两种模式，二选一）

前端是独立的 Angular dev server（4200），后端是 API（5240）。两者联调有两种方式：

#### 模式一：SPA 同源访问（推荐）

后端内置 SPA 反向代理：`/api/**` 由后端处理，其余请求代理到前端 dev server。**浏览器只访问后端地址，前后端同源，无跨域、cookie 正常。**

- 后端：`SpaProxy.Enabled=true`、`SpaProxy.Target=http://localhost:4200`（如上配置）。
- 前端：`environment.debug.ts` 里 `api.gateway=''`（同源相对路径）。
- 访问：浏览器打开 **`http://localhost:5240/`**（不是 4200）。

```text
浏览器 → http://localhost:5240
           ├─ /api/**  → 后端 Controller
           └─ 其他     → SPA 代理转发到 Angular(4200)
```

#### 模式二：CORS 分离访问

浏览器直接访问前端（4200），API 跨域打到后端。

- 后端：`SpaProxy.Enabled=false`、`Cors.AllowAnyLocalhost=true`（开发放行本地端口）。
- 前端：`environment.debug.ts` 里 `api.gateway='http://localhost:5240'`。
- 访问：浏览器打开 `http://localhost:4200/`。
- 提示：跨域携带 cookie 受 SameSite/Secure 限制更严，登录态异常时优先改用模式一。

> 常见错误：浏览器访问 4200、API 打 5240，但后端 `Cors.AllowAnyLocalhost=false` 且 `AllowedOrigins` 为空 → CORS 预检无 `Access-Control-Allow-Origin`，请求被浏览器拦截（curl 不受影响，故命令行能通、浏览器不通）。改用模式一或开启 CORS 即可。

### 4. 运行应用

```bash
dotnet run
```

应用将：
- 初始化数据库（未生成迁移时用 EnsureCreated 按当前模型建表；已生成迁移则自动应用，详见“数据库迁移”）
- 创建默认管理员用户
- 开始监听 `http://localhost:5240`

### 5. 访问

- **前端控制台**（模式一）: `http://localhost:5240/`
- **健康检查**: `http://localhost:5240/api/health`
- **业务 API**: `http://localhost:5240/api/v1/*`

---

## 环境配置

### 配置文件

- `appsettings.json` - 基础配置（含开发默认值，入库）
- `appsettings.debug.json` - 本地开发覆盖（环境名 `debug`，已 gitignore）
- `appsettings.Development.json` - 开发环境（已 gitignore）
- `appsettings.Production.json` - 生产环境

### 主要配置项

#### 数据库（PostgreSQL）

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Database=CompanyName.ProjectName;Username=postgres;Password=YourPassword"
  }
}
```

或使用环境变量：
```bash
ConnectionStrings__Default="Host=localhost;Port=5432;Database=CompanyName.ProjectName;Username=postgres;Password=YourPassword"
```

> 留空则使用内存数据库。

#### Redis 缓存

```json
{
  "ConnectionStrings": {
    "Redis": "localhost:6379,password=your-password,ssl=true"
  }
}
```

或使用环境变量：
```bash
ConnectionStrings__Redis="localhost:6379,password=your-password,ssl=true"
```

#### SPA 代理（本地联调，模式一）

```json
{
  "SpaProxy": {
    "Enabled": true,
    "Target": "http://localhost:4200"
  }
}
```

> `Enabled=true` 时，后端把非 `/api` 请求代理到 `Target`（前端 dev server），实现同源访问；生产/部署一般关闭，由后端直接托管前端构建产物。详见上方「前后端联调」。

#### CORS（本地联调，模式二）

```json
{
  "Cors": {
    "AllowAnyLocalhost": true,
    "AllowedOrigins": ["https://example.com"]
  }
}
```

> 开发用 `AllowAnyLocalhost=true` 放行任意 localhost 端口；生产用 `AllowedOrigins` 精确白名单。仅在采用「模式二：CORS 分离访问」时需要。

#### 默认管理员用户

```json
{
  "DefaultAdmin": {
    "Username": "admin",
    "Password": "Admin@123456",
    "Email": "admin@example.com",
    "Nickname": "系统管理员"
  }
}
```

> 认证基于 OpenIddict（OAuth2/OIDC）+ Cookie，无需配置 JWT 密钥。

#### 外部 OAuth（可选）

```json
{
  "ExternalAuth": {
    "GitHub": {
      "ClientId": "your-github-client-id",
      "ClientSecret": "your-github-client-secret",
      "RedirectUri": "http://localhost:5240/api/v1/external-auth/github/callback"
    },
    "Google": {
      "ClientId": "your-google-client-id",
      "ClientSecret": "your-google-client-secret",
      "RedirectUri": "http://localhost:5240/api/v1/external-auth/google/callback"
    }
  }
}
```

---

## 数据库迁移

> **模板默认不内置任何 EF Core 迁移。** 这是刻意的最佳实践（与 ABP、微软官方模板一致）：
> 迁移应由你的项目按**实际启用的功能组合**自行生成，而不是在模板里预置再按开关裁剪——后者
> 极易产生模型与迁移不一致、孤儿迁移类等问题。

### 首次运行：EnsureCreated（开箱即用）

未生成任何迁移时，应用启动会自动用 `EnsureCreated` 按当前模型直接建表，**无需先生成迁移**即可跑起来，
适合本地快速试跑。`EnsureCreated` 不走迁移历史，不适合需要持续演进表结构的生产环境。

### 生产环境：生成并使用迁移（推荐）

一旦你确定了功能组合、准备长期演进表结构，生成首个迁移：

```bash
dotnet ef migrations add InitialCreate \
  --project src/CompanyName.ProjectName.Infrastructure \
  --startup-project src/CompanyName.ProjectName.Api \
  --output-dir Persistence/Migrations
```

生成的迁移会精确匹配你当前启用的功能（Identity / Roles / OpenIddict / ExternalLogin / Notifications），
不含任何条件编译。此后应用启动会**自动应用待执行迁移**（见下）。

> 注意：同一数据库不要在 `EnsureCreated` 与迁移两种方式间来回切换。决定用迁移后，建议从一个干净的库开始。

### 自动应用迁移

存在迁移时，应用启动会自动应用待执行的迁移，无需手动 `database update`。

### 后续新增/修改表结构

```bash
dotnet ef migrations add MigrationName \
  --project src/CompanyName.ProjectName.Infrastructure \
  --startup-project src/CompanyName.ProjectName.Api \
  --output-dir Persistence/Migrations
```

**应用迁移：**

**方式一（推荐）：** 启动应用自动完成迁移

**方式二：** 手工完成迁移（默认使用appsettings.debug.json配置）
```bash
dotnet ef database update \
  --project src/CompanyName.ProjectName.Infrastructure \
  --startup-project src/CompanyName.ProjectName.Api
```

**使用自定义连接字符串应用迁移：**
```bash
dotnet ef database update \
  --project src/CompanyName.ProjectName.Infrastructure \
  --startup-project src/CompanyName.ProjectName.Api \
  --connection "Host=localhost;Database=CompanyName.ProjectName;Username=postgres;Password=postgres"
```

**移除最后一次迁移：**
```bash
dotnet ef migrations remove \
  --project src/CompanyName.ProjectName.Infrastructure \
  --startup-project src/CompanyName.ProjectName.Api
```

---

## 开发

### 构建

```bash
dotnet build
```

### 运行测试

```bash
dotnet test
```

### 使用特定环境运行

```bash
# Debug
dotnet run --environment Debug

# Development
dotnet run --environment Development

# Production
dotnet run --environment Production
```

### 监视模式（自动重载）

```bash
dotnet watch run
```

---

## Docker 部署

### 构建 Docker 镜像

从项目根目录：
```bash
docker build -t company-name-project-name:latest .
```

### 运行容器

```bash
docker run -d \
  --name company-name-project-name \
  -p 5240:8080 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e ConnectionStrings__Default="Host=your-db;Port=5432;Database=CompanyName.ProjectName;Username=postgres;Password=YourPassword" \
  -e ConnectionStrings__Redis="your-redis:6379" \
  -e DefaultAdmin__Password="YourAdminPassword" \
  company-name-project-name:latest
```

---

## 核心功能

- **认证授权**：OpenIddict（OAuth2/OIDC）+ Cookie，基于角色/权限
- **用户与权限管理**：内置用户、角色、权限体系
- **外部登录**：可选 GitHub / Google OAuth 接入
- **整洁架构**：基于 Leistd 框架的 DDD 分层
- **本地 SPA 代理**：开发期前后端同源联调
- **自动数据库迁移**：启动时无缝更新架构

---

## 故障排查

### 数据库连接问题

如果遇到数据库连接错误：
1. 验证 PostgreSQL 是否运行
2. 检查 `appsettings.json` 中的连接字符串
3. 确保数据库存在或让应用自动创建

### Redis 连接问题

如果 Redis 不可用：
- 应用会自动回退到内存缓存
- 本地开发无需额外操作

### 端口已被占用

如果端口 5240 被占用：
```bash
# 在 launchSettings.json 中更改端口或使用环境变量
dotnet run --urls "http://localhost:5241"
```

---

## 其他资源

- **.NET 文档**: [官方文档](https://learn.microsoft.com/zh-cn/dotnet/)
- **Entity Framework Core**: [官方文档](https://learn.microsoft.com/zh-cn/ef/core/)
