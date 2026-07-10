# Fullstack App Template

> **面向 AI 协作开发的 .NET 10 + Angular 21 全栈模板**：不只生成 DDD 四层代码骨架，更生成一套让 AI 端到端交付需求、且全程可追溯的**人机协作工作流**。

`dotnet new` 生成的项目开箱即带：DDD 四层后端 + Angular 前端、通过 NuGet 引用的 [Leistd 框架](../framework/README.md)、以及一套 **AI 协作工作流骨架**（`.claude/skills/` + `docs/standards/`）。

## 有什么不一样：AI 协作开发

普通模板只给你代码起点；本模板还给 AI 一套「怎么在这个项目里正确干活」的规范与流程——AI 不再凭记忆猜测，而是读项目规范、按阶段推进、留下可追溯证据：

- **6 个阶段 Skill**（随项目一起生成到 `.claude/skills/`）：`requirement-plan`（需求→Plan）· `task-manager`（登记/推进/验收）· `coding`（按 Plan 开发）· `code-review`（分级审查）· `test-runner`（测试验证）· `deploy`（部署发布）。各司其职，由 AI 按你的意图自动选择。
- **工作流事实源** `docs/standards/agent-workflow.md`：定义 8 阶段状态机、阶段门禁、交接包（换会话可无损接续）、以及生产部署等高风险动作的人工确认红线。
- **证据闭环**：需求 Plan、任务上下文、开发/审查/测试/部署/验收报告统一沉淀到 `docs/`，一个需求可从 plan 完整追溯到 acceptance。
- **规范即护栏**：技术栈、编码规范、API 契约、文档分类既给人看，也是 AI 行动的依据；缺失时 Skill 用内置默认继续并提示补齐，不阻塞。

> 上手指引见项目内 [`docs/quick-start/ai-native-model.md`](docs/quick-start/ai-native-model.md)。生成项目后，对 AI 说「用 requirement-plan 帮我把这个需求理成方案」「按 Plan 开发」「帮我 review」即可进入闭环。

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
├── backend/                                # 后端
│   ├── src/
│   │   ├── MyProject.Api/                  # API 层（Controllers、中间件）
│   │   ├── MyProject.Application/          # 应用层（AppService、权限）
│   │   ├── MyProject.Domain/              # 领域层（实体、领域服务）
│   │   └── MyProject.Infrastructure/      # 基础设施层（EF Core、邮件）
│   ├── Directory.Build.props               # 框架版本 / 本地调试切换
│   ├── Directory.Packages.props            # 中央包管理（CPM）
│   └── MyProject.sln
├── frontend/                               # 前端 Angular
│   └── src/app/{core,features,layout,shared}/
├── docs/                                   # 📄 项目文档 + AI 协作规范
│   ├── standards/                          #   工作流事实源、编码/API/测试规范
│   ├── requirements/                       #   需求 Plan 与登记册
│   ├── reports/                            #   各阶段证据报告
│   └── modules/                            #   模块文档
├── .claude/skills/                         # 🤖 6 个 AI 阶段 Skill（随项目分发）
├── deploy/                                 # 部署配置（docker-compose）
└── Dockerfile                              # 多阶段构建
```

> 项目根 = 含 `backend/`、`frontend/`、`docs/` 的这一层。把本模板并入 monorepo（如 `apps/<name>/`）时，AI 的 Skill 会按 `docs/standards/agent-workflow.md` §2.0 相对这一层解析所有文档路径。

### 条件裁剪说明

| 参数 | 裁剪的后端内容 | 前端影响 |
|------|---------------|---------|
| `--include-identity false` | AuthController、Auth/、Email/、UserRegistrationOptions | 前端保留但登录/注册功能不可用 |
| `--include-roles false` | Permissions/（PermissionConstant 等） | 前端保留但角色权限功能不可用 |

## 本地开发

### 后端

```bash
cd backend/src/MyProject.Api

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
# 在 backend/ 目录执行
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
cd frontend

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

### 联调本地框架（开发者）

生成的项目默认通过 NuGet `PackageReference` 引用 [Leistd 框架](../framework/README.md) 包。模板不会把本仓库的本地 NuGet 源写入生成项目；生成项目默认使用用户环境中的 NuGet 源（通常是 `nuget.org`）。

若你**同时在改框架源码**，想在本仓库内验证"改过的框架 + 模板项目"，临时指定本地 `local-feed`（保持模板纯净，等同最终用户的真实消费路径）。仓库根 `NuGet.Config` 只保留 `nuget.org`，避免 CI/发布环境因不存在的本地源失败：

```bash
# 1. 在仓库根目录执行，打本地包到 local-feed
dotnet pack framework/Leistd.Framework.slnx -c Debug -o ./local-feed

# 2. 在仓库内验证模板后端还原，显式指定本地 feed + nuget.org
dotnet restore template/backend/CompanyName.ProjectName.sln \
  --source ./local-feed \
  --source https://api.nuget.org/v3/index.json

# 3. 生成项目后若要继续消费本地包，可在生成项目根目录自行添加本地源
dotnet new fullstack-app -n Acme.Shop
dotnet nuget add source "<repo>/local-feed" --name leistd-local
```

> 若只想验证框架功能本身（不经过模板），更简单的方式是在仓库内的 demo / 测试项目里用 `ProjectReference` 直接指向 `framework/` 源码，可断点步进。

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
docker build -t companyname-projectname .

# 运行
docker run -d \
  -p 8080:8080 \
  -e ConnectionStrings__Default="Host=your-db;Database=MyProject;Username=postgres;Password=xxx" \
  -e DefaultAdmin__Password="YourPassword" \
  -e Jwt__SecretKey="YourSecretKey-MinimumLength32Characters!" \
  companyname-projectname
```

## 卸载模板

```bash
dotnet new uninstall FullstackApp.Template
```

## 许可证

MIT License
