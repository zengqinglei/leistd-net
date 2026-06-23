# Fullstack App Template

.NET 10 + Angular 21 全栈项目模板，基于 DDD 四层架构，开箱即用。

## 技术栈

| 层 | 技术 |
|---|------|
| 前端 | Angular 21 + PrimeNG 21 + Tailwind CSS 4 |
| 后端 | .NET 10 + ASP.NET Core + EF Core |
| 数据库 | PostgreSQL 15+ / 内存数据库（开发） |
| 缓存 | Redis 7+（可选） |
| 部署 | Docker + Docker Compose |

## 安装模板

```bash
# 从本地目录安装
dotnet new install ./template

# 从 NuGet 安装（发布后）
dotnet new install FullstackApp.Template
```

## 创建项目

### 基本用法

```bash
# 创建项目（命名空间为 MyApp.Api, MyApp.Domain 等）
dotnet new fullstack-app -n MyApp

# 带公司前缀（命名空间为 Acme.MyApp.Api, Acme.MyApp.Domain 等）
dotnet new fullstack-app -n Acme.MyApp
```

### 可选参数

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `-n` / `--name` | string | 必填 | 项目名称/命名空间前缀，支持点分隔（如 `Acme.MyApp`） |
| `--include-identity` | bool | `true` | 是否包含认证模块（登录/注册/JWT） |
| `--include-roles` | bool | `true` | 是否包含角色权限系统 |

### 示例

```bash
# 完整项目（认证 + 权限）
dotnet new fullstack-app -n MyProject

# 带公司前缀
dotnet new fullstack-app -n Acme.MyProject

# 不需要认证模块
dotnet new fullstack-app -n MyProject --include-identity false

# 最小化项目（无认证、无权限）
dotnet new fullstack-app -n MyProject --include-identity false --include-roles false
```

## 生成的项目结构

```
MyProject/
├── src/                                    # 后端
│   ├── MyProject.Api/                      # API 层（Controllers、中间件）
│   ├── MyProject.Application/              # 应用层（AppService、权限）
│   ├── MyProject.Domain/                   # 领域层（实体、领域服务）
│   └── MyProject.Infrastructure/           # 基础设施层（EF Core、邮件）
├── web/                                    # 前端 Angular
│   ├── src/app/
│   │   ├── core/                           # 守卫、拦截器、服务
│   │   ├── features/                       # 功能模块（account、platform、workspace）
│   │   ├── layout/                         # 布局组件
│   │   └── shared/                         # 共享组件、管道、模型
│   ├── angular.json
│   └── package.json
├── deploy/                                 # 部署配置
│   ├── docker-compose.yml
│   └── docker-compose.override.yml
├── Dockerfile                              # 多阶段构建
└── MyProject.sln
```

### 条件裁剪说明

| 参数 | 裁剪的后端内容 | 前端影响 |
|------|---------------|---------|
| `--include-identity false` | AuthController、Auth/、Email/、UserRegistrationOptions | 前端保留但登录/注册功能不可用 |
| `--include-roles false` | Permissions/（PermissionConstant 等） | 前端保留但角色权限功能不可用 |

## 本地开发

### 后端

```bash
cd src/MyProject.Api

# 使用内存数据库（无需配置，开箱即用）
dotnet run

# 使用 PostgreSQL
# 1. 在 appsettings.json 中配置 ConnectionStrings:Default
# 2. 生成初始迁移
# 3. 启动应用（自动执行迁移）
dotnet run
```

启动后访问 `http://localhost:5240`。

### 数据库迁移

模板不包含 EF Core 迁移文件（不同用户可能使用不同数据库）。使用 PostgreSQL 等关系型数据库时，需要先生成迁移：

```bash
# 在项目根目录执行
dotnet ef migrations add InitialCreate \
  --project src/MyProject.Infrastructure \
  --startup-project src/MyProject.Api \
  --output-dir Persistence/Migrations

# 应用迁移（或启动应用时自动执行）
dotnet ef database update \
  --project src/MyProject.Infrastructure \
  --startup-project src/MyProject.Api
```

> **提示**：不配置连接字符串时，应用自动使用内存数据库，无需执行迁移。

### 前端

```bash
cd web

# 安装依赖
npm install

# 启动开发服务器（自动代理到后端 5240 端口）
npm start
```

启动后访问 `http://localhost:4200`。

### 默认管理员

首次启动时自动创建：
- 用户名：`admin`
- 密码：`Admin@123456`（请在生产环境修改）

## Docker 部署

### Docker Compose（推荐）

```bash
# 编辑配置
nano deploy/docker-compose.yml

# 启动
cd deploy && docker-compose up -d
```

### 单独构建

```bash
# 构建镜像
docker build -t myproject .

# 运行
docker run -d \
  -p 8080:8080 \
  -e ConnectionStrings__Default="Host=your-db;Database=MyProject;Username=postgres;Password=xxx" \
  -e DefaultAdmin__Password="YourPassword" \
  -e Jwt__SecretKey="YourSecretKey-MinimumLength32Characters!" \
  myproject
```

## 卸载模板

```bash
dotnet new uninstall FullstackApp.Template
```

## 许可证

MIT License
