# CompanyName.ProjectName

基于 .NET 10、Angular 22 和 Leistd.* 组件构建的全栈项目，后端采用 Domain、Application、Infrastructure、Api 四层结构。

## 项目结构

```text
CompanyName.ProjectName/
|-- backend/                 # .NET 后端
|-- frontend/                # Angular 前端
|-- deploy/                  # Docker Compose 配置
|-- docs/                    # 项目规范与按需沉淀文档
|-- .agents/skills/          # 跨工具项目 Skill
`-- Dockerfile
```

## 已启用能力

- EF Core 数据访问、审计字段、软删除、仓储与应用服务基座。
<!--#if (IdentityService)-->
- 本地账号、登录、注册和 Cookie 认证。
<!--#if (LocalAuthorization)-->
- 用户、角色、权限以及超级管理员授权模型。
<!--#if (MultiTenancy)-->
- 多租户：租户解析与数据硬隔离、租户管理（宿主侧）、登录页租户选择；每个租户拥有独立的用户、角色与权限授予。
<!--#endif-->
<!--#endif-->
- OpenIddict OAuth 2.0/OIDC Server。
<!--#if (IncludeExternalLogin)-->
- GitHub、Google 等外部身份提供方登录。
<!--#endif-->
<!--#endif-->
<!--#if (ResourceService)-->
- 远程 OIDC 令牌验证、本服务 Membership/角色/权限与租户数据隔离。
<!--#endif-->
<!--#if (IncludeNotifications)-->
- 通知持久化、未读状态、通知 Hub 和业务实时 Hub。
<!--#endif-->

## 本地运行

### 后端

未配置 `ConnectionStrings:Default` 时使用内存数据库，可直接启动：

```bash
cd backend/src/CompanyName.ProjectName.Api
dotnet run
```

存活与就绪检查地址分别为 `http://localhost:5240/api/health/live` 和 `http://localhost:5240/api/health/ready`。

如需 PostgreSQL，在被 Git 忽略的 `backend/src/CompanyName.ProjectName.Api/appsettings.Development.json` 中配置：

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Database=companyname-projectname;Username=postgres;Password=postgres"
  }
}
```

模板预置基线迁移和独立 `DbMigrator`。API 启动时不自动修改 schema；本地和发布环境都先运行迁移入口，再启动 API：

```bash
cd backend
dotnet run --project src/CompanyName.ProjectName.DbMigrator
dotnet run --project src/CompanyName.ProjectName.Api
```

API 和 `DbMigrator` 使用不同的 Runtime/Migration Secret；API 运行身份只持有 DML 权限。SharedDatabase 中各服务共用数据库实例、使用固定独立 schema 并以 `TenantId` 隔离；DedicatedDatabase 由租户配置覆盖连接，各服务仍共用该租户连接并写入自己的 schema。
首次建立 DedicatedDatabase 租户前，先以 `ConnectionStrings__MigrationTarget` 运行各服务 DbMigrator 预建该服务 schema，再创建租户；常规发布仍使用全目标枚举模式。

<!--#if (IdentityService)-->
开发环境首次启动会创建管理员账号：

- 用户名：`admin`
- 密码：`Admin@123456`

部署前必须通过安全配置覆盖默认密码。
<!--#endif-->

### 前端

```bash
cd frontend
npm ci
npm start
```

默认开发服务器地址为 `http://localhost:4200`。Mock、同源代理和跨域联调方式见 [前端说明](frontend/README.md)。

## 验证

```bash
dotnet test backend/CompanyName.ProjectName.sln
npm --prefix frontend run lint
npm --prefix frontend run build
```

## AI 协作

项目级 Skill 位于 [`.agents/skills/leistd-project-workflow/`](.agents/skills/leistd-project-workflow/SKILL.md)，统一覆盖规划、实现、审查、测试、协调和部署，并按场景加载必要 reference。它以 [项目文档入口](docs/README.md)、源码、配置和测试为事实，不依赖特定 AI 工具的入口文件。

[Codex](https://developers.openai.com/codex/skills) 等原生发现 `.agents/skills/` 的 AI CLI 无需安装。使用只识别其他项目目录的 CLI 时，按需生成本地适配；例如项目 Skill 位于 `.claude/skills/` 的 [Claude Code](https://code.claude.com/docs/en/skills#where-skills-live) 执行：

```bash
npx skills add ./.agents/skills/leistd-project-workflow --agent claude-code --skill leistd-project-workflow -y
```

`npx skills` 会根据平台能力选择链接或复制，并在结果中标明实际方式。需要强制复制时执行：

```bash
npx skills add ./.agents/skills/leistd-project-workflow --agent claude-code --skill leistd-project-workflow --copy -y
```

复制的适配不会自动跟随权威源更新，修改项目 Skill 后应重新执行命令。生成的 `.claude/skills/` 和 `skills-lock.json` 是本地适配产物，不提交到仓库。AI CLI 完全不支持 Skill 时，在当前会话中明确要求它先读取 `.agents/skills/leistd-project-workflow/SKILL.md`。

使用 `Leistd.*` 组件时，按 [后端说明](backend/README.md#leistd-框架-api) 定位当前 NuGet 包版本的文档和 XML，不根据模型记忆猜测 API。

## 部署

```bash
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.override.yml up --build
```

生产部署前应确认数据库迁移、密钥、管理员密码、HTTPS、健康检查和回滚方案。
