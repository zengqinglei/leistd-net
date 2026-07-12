# CompanyName.ProjectName

基于 .NET 10、Angular 21 和 Leistd.* 组件构建的全栈项目，后端采用 Domain、Application、Infrastructure、Api 四层结构。

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
<!--#if (IncludeIdentity)-->
- 本地账号、登录、注册和 Cookie 认证。
<!--#if (IncludeRoles)-->
- 用户、角色、权限以及超级管理员授权模型。
<!--#endif-->
<!--#if (IncludeLocalization)-->
- 英文（默认）和简体中文本地化，覆盖 .NET 请求文化、异常响应、Angular 构建和 PrimeNG 组件文案。
<!--#endif-->
<!--#if (IncludeOpenIddict)-->
- OpenIddict OAuth 2.0/OIDC Server。
<!--#endif-->
<!--#if (IncludeExternalLogin)-->
- GitHub、Google 等外部身份提供方登录。
<!--#endif-->
<!--#if (IncludeNotifications)-->
- 通知持久化、未读状态、通知 Hub 和业务实时 Hub。
<!--#endif-->
<!--#endif-->

## 本地运行

### 后端

未配置 `ConnectionStrings:Default` 时使用内存数据库，可直接启动：

```bash
cd backend/src/CompanyName.ProjectName.Api
dotnet run
```

健康检查地址为 `http://localhost:5240/api/health`。

如需 PostgreSQL，在被 Git 忽略的 `backend/src/CompanyName.ProjectName.Api/appsettings.Development.json` 中配置：

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Database=companyname-projectname;Username=postgres;Password=postgres"
  }
}
```

模板不预置迁移。应用在所有环境启动时自动初始化关系型数据库：已有迁移文件则执行 `MigrateAsync`，没有迁移文件则执行 `EnsureCreatedAsync` 创建数据库。修改模型后只需生成并审查迁移文件，无需再执行 `dotnet ef database update`：

```bash
cd backend
dotnet ef migrations add InitialCreate \
  --project src/CompanyName.ProjectName.Infrastructure \
  --startup-project src/CompanyName.ProjectName.Api \
  --output-dir Persistence/Migrations
```

由 `EnsureCreated` 创建的数据库没有迁移历史，不能直接切换为迁移管理。计划持续演进结构的数据库应在首次启动前随应用包含初始迁移；否则后续启用迁移时需要重建数据库或制定基线方案。生产部署会随应用启动自动创建或迁移数据库，因此部署前必须审查模型或迁移，并准备备份和失败恢复方案。

<!--#if (IncludeIdentity)-->
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
<!--#if (IncludeLocalization)-->
`npm start` 默认运行 `en-US` 构建；生产构建同时输出 `en-US` 与 `zh-CN` 子目录。
<!--#endif-->

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
