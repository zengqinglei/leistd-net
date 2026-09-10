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
<!--#if (LocalIdentity)-->
- 本地账号、登录、注册和 Cookie 认证。
- 用户、角色、权限以及超级管理员授权模型。
- 多租户：租户解析与数据硬隔离、租户管理（宿主侧）、登录页租户选择；每个租户拥有独立的用户、角色与权限授予。
- OpenIddict OAuth 2.0/OIDC Server。
<!--#if (ExternalLogin)-->
- GitHub、Google 等外部身份提供方登录。
<!--#endif-->
<!--#endif-->
<!--#if (!LocalIdentity)-->
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

<!--#if (LocalIdentity)-->
### 邮件

`Leistd:Email:Smtp` 默认指向本机邮件捕获器，本地起一个即可看到真实投出去的信：

```bash
docker run -d -p 1025:1025 -p 8025:8025 axllent/mailpit   # 收件箱在 http://localhost:8025
```

邮箱验证默认关闭（`UserRegistration:EnableEmailVerification`）。开启后没有可达的 SMTP 会**发信失败并向调用方报错**，不会静默跳过——注册流程据此撤回已占用的限流槽位。生产环境须覆盖 `Host`/`Port`/`EnableSsl`/`DefaultFromAddress`，`Username`/`Password` 属于凭据，用环境变量或 user-secrets 注入。
<!--#endif-->

如需 PostgreSQL，在被 Git 忽略的 `backend/src/CompanyName.ProjectName.Api/appsettings.Development.json` 中配置：

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Database=companyname-projectname;Username=postgres;Password=postgres"
  }
}
```

模板预置基线迁移和独立 `DbMigrator`。API 启动时不自动修改 schema；本地和发布环境都先运行迁移入口，再启动 API。

`DbMigrator` **默认只读预演**——列出待执行的迁移与 SQL，不改库；确认后再加 `--apply` 施加：

```bash
cd backend
dotnet run --project src/CompanyName.ProjectName.DbMigrator             # 预演：只看清单与 SQL
dotnet run --project src/CompanyName.ProjectName.DbMigrator -- --apply  # 确认后施加
dotnet run --project src/CompanyName.ProjectName.Api
```

**首次安装**时控制库尚未迁移，租户注册表还不存在，预演只列出此刻能确定的目标（控制面、OIDC 存储、默认业务库）——
此时表里本就不可能有独立库租户，所以这份计划是准确的。`--apply` 会先建好控制表，再枚举独立库租户目标并一并施加。

控制库**已迁移**之后若仍读不到租户注册表（表被误删、schema 配错、迁移与模型不一致），
那是损坏而不是首装：预演与 `--apply` 都以非零退出码结束，不会静默当作"没有独立目标"。

API 和 `DbMigrator` 使用不同的 Runtime/Migration Secret；API 运行身份只持有 DML 权限。SharedDatabase 中各服务共用数据库实例、使用固定独立 schema 并以 `TenantId` 隔离；DedicatedDatabase 由租户配置覆盖连接，各服务仍共用该租户连接并写入自己的 schema。
首次建立 DedicatedDatabase 租户前，先以 `ConnectionStrings__MigrationTarget` 运行各服务 DbMigrator 预建该服务 schema，再创建租户；常规发布仍使用全目标枚举模式。

### 多服务共用一个数据库时的两行配对调整

多个服务共用同一个数据库实例（SharedDatabase 的常见形态）时，**`HasDefaultSchema` 与
`MigrationsHistoryTable` 必须成对设置**：

```csharp
// OnModelCreating / ConfigureModel 里
modelBuilder.HasDefaultSchema("companyname-projectname");

// AddDbContext 的 UseNpgsql 回调里
npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "companyname-projectname");
```

**只做前者是最隐蔽的坑**：表被分到各自 schema，但迁移历史表默认落在 `public`，于是多个
服务争用同一张 `__EFMigrationsHistory`——A 服务施加迁移后，B 服务会认为自己的迁移"已执行过"
而跳过，或反过来重复执行。两者都不报错，直到某个表缺列才暴露。

同一个数据库里若有多个 DbContext（如 Identity 的控制库与业务库），**历史表名也要区分**，
模板已按 `__EFMigrationsHistory_Control` / `__EFMigrationsHistory` 分开。

<!--#if (LocalIdentity)-->
开发环境首次启动会创建管理员账号：

- 用户名：`admin`
- 密码：由部署注入 `DefaultAdmin__Password`（环境变量或 `dotnet user-secrets`）。
  **基础配置里没有可用的默认密码**——缺失、空值或不满足密码策略（至少 12 个字符）都会导致启动失败。
  这是刻意的：开源模板里的默认管理员密码等于公开凭据，而漏配的部署会照常启动、照常能登录。

部署前必须通过安全配置注入 `DefaultAdmin__Password`（不存在可覆盖的默认值，缺失即启动失败）。
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
