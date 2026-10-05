# CompanyName.ProjectName

基于 .NET 10 和 Leistd.* 组件构建的应用，后端采用 Domain、Application、Infrastructure、Api 四层结构。
<!--#if (SpaFrontend)-->
前端使用 Angular 22。
<!--#endif-->

## 项目结构

```text
CompanyName.ProjectName/
|-- backend/                 # .NET 后端
<!--#if (SpaFrontend)-->
|-- frontend/                # Angular 前端
<!--#endif-->
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
<!--#if (IncludeMultiTenancy)-->
- 多租户控制面、租户管理与登录页租户选择。
<!--#endif-->
<!--#if (OpenIddictServer)-->
- OpenIddict OAuth 2.0/OIDC Server。
<!--#endif-->
<!--#if (ExternalLogin)-->
- GitHub、Google 等外部身份提供方登录。
<!--#endif-->
<!--#endif-->
<!--#if (!LocalIdentity)-->
- 远端 Bearer 验证、用户投影与本服务角色/权限；首位投影用户不会自动获得管理员权限。
<!--#if (ResourceBrowserSession)-->
- 后端 OIDC 机密依赖方与服务端 Cookie 票据。
<!--#endif-->
<!--#endif-->
<!--#if (IncludeNotifications)-->
- 通知持久化、未读状态与站内推送。
<!--#if (LocalIdentity)-->
- 安全提醒（新设备登录、密码与两步验证变更、账号锁定）：站内通知始终送达；用户在个人设置「通知」面板按类别与渠道选择接收方式。
<!--#endif-->
<!--#endif-->

<!--#if (IncludeRealTime)-->
- 业务实时事件与受授权保护的资源订阅；角色新建、修改、删除后角色列表自动刷新，断线恢复后补查一次（用户角色分配引起的人数变化不推送）。
<!--#endif-->
<!--#if (IncludeMultiTenancy)-->
- 租户作用域的数据隔离与独立数据库路由。
<!--#else-->
- 仅宿主作用域：业务数据保留 nullable TenantId 与审计字段，认证入口拒绝租户身份。
<!--#endif-->
<!--#if (IncludeOperationRecords)-->
- 内置操作记录存储、查询、导出及归档；成功记录随业务事务持久化。
<!--#else-->
- 必需的结构化安全记录；成功在工作单元提交后输出，失败立即输出。提交与日志输出之间进程退出可能丢记录。
<!--#endif-->

## 本地运行

### 后端

配置分三层：`appsettings.json` 是与环境无关的基线；随仓库提交的 `appsettings.Development.json` 放所有开发者共用的开发配置（指向本机开发库的连接串、本机演示管理员口令等）；机密与只属于本机的覆盖放 `dotnet user-secrets`，开发环境自动加载、不进仓库。Api 与 `DbMigrator` 共用同一份 user-secrets，设置一次两边都能读。

本机依赖用 `deploy/docker-compose.dev.yml` 起（只绑定 127.0.0.1）。Api 与 `DbMigrator` 各自的 `appsettings.Development.json` 都指向这个库，克隆后起依赖、迁移一次即可启动；连接共享开发库时用 user-secrets 覆盖 `ConnectionStrings:Default`，不改配置文件。

```bash
docker compose -f deploy/docker-compose.dev.yml up -d
cd backend
dotnet run --project src/CompanyName.ProjectName.DbMigrator -- --apply   # 首次启动前、拉到新迁移后
dotnet run --project src/CompanyName.ProjectName.Api
```

<!--#if (LocalIdentity)-->
首次启动按 `appsettings.Development.json` 里的演示口令创建管理员。

<!--#endif-->
<!--#if (RemoteTokenAuth)-->
配置 `Authentication:Issuer` 为真实 Identity 签发方地址，并在 Identity 登记本服务 API 的受众。访问令牌权限以本服务授权事实为准。
<!--#if (ResourceBrowserSession)-->
签发令牌的 Identity 服务：开发环境默认指向本机 Identity 的前端开发服务器 `http://localhost:4200/`（`appsettings.Development.json` 的 `Authentication:Issuer`），联调步骤见 [前端说明](frontend/README.md)；
联调别处的 Identity 时用 user-secrets 覆盖。需先在 Identity 登记机密浏览器依赖方及 `/api/v1/auth/signin`、`/api/v1/auth/signout` 回调（含本服务前端源），再配置 ClientId/ClientSecret；缺失会按键名启动失败：

```bash
cd backend
dotnet user-secrets set "Authentication:Issuer" "<Identity 服务的地址>/" --project src/CompanyName.ProjectName.Api
dotnet user-secrets set "Authentication:ClientId" "<已登记的机密客户端>" --project src/CompanyName.ProjectName.Api
dotnet user-secrets set "Authentication:ClientSecret" "<机密客户端密钥>" --project src/CompanyName.ProjectName.Api
```

<!--#endif-->
<!--#if (IncludeMultiTenancy)-->
租户路由与迁移作业以机器身份回源同一个 Identity（开发配置的 `Leistd:ServiceAuth:Authority` 与 `Leistd:ServiceClients:Identity:BaseAddress` 同样指向 `http://localhost:4200/`，联调别处时一并覆盖）。在 Identity「开放应用」登记一个 client credentials 机器客户端，授予 `tenant-routing.read`（运行时回源）与 `tenant-migration.read`（`DbMigrator` 枚举独立库租户），登记方式见 [服务调用规范](docs/standards/service-invocation.md)；把凭据写进 user-secrets，Api 与 `DbMigrator` 共用。本机可用一个客户端同时持有两个 scope，部署环境按 `deploy/docker-compose.yml` 分开。迁移前先启动本机 Identity：

```bash
cd backend
dotnet user-secrets set "Leistd:ServiceAuth:ClientId" "<机器客户端>" --project src/CompanyName.ProjectName.Api
dotnet user-secrets set "Leistd:ServiceAuth:ClientSecret" "<机器客户端密钥>" --project src/CompanyName.ProjectName.Api
```

<!--#endif-->
<!--#endif-->
<!--#if (OpenIddictServer)-->

默认配置只监听 HTTP（`http://localhost:5240`）。本机的 OIDC 流程（开放应用的授权码流程、资源服务联调）经前端开发服务器 `http://localhost:4200` 访问授权端点，签发方地址即为它；`appsettings.Development.json` 因此关闭了授权端点的 HTTPS 要求，只作用于开发环境。
<!--#endif-->

存活与就绪检查地址分别为 `http://localhost:5240/api/health/live` 和 `http://localhost:5240/api/health/ready`。
<!--#if (Email)-->

### 邮件

`Leistd:Email:Smtp` 默认指向本机邮件捕获器，开发 compose 已经带上它，收件箱在 `http://localhost:8025`。

邮箱验证默认关闭（`UserRegistration:EnableEmailVerification`）。开启后没有可达的 SMTP 会**发信失败并向调用方报错**，不会静默跳过——注册流程据此撤回已占用的限流槽位。生产环境须覆盖 `Host`/`Port`/`EnableSsl`/`DefaultFromAddress`，`Username`/`Password` 属于凭据，用环境变量或 user-secrets 注入。配置文件是部署基线：宿主管理员可以在「系统设置 → 邮件发送」里在运行期覆盖这些参数（口令加密落库、界面只写不读），并在同一面板发送测试邮件；清除覆盖值即回到配置文件里的值。
<!--#endif-->

Redis 不配置时用进程内缓存与本机锁；多副本部署必须配置 `ConnectionStrings:Redis`，开发 compose 已起一个可供本机验证。
<!--#if (LocalIdentity && IncludeMultiTenancy)-->

API 与 `DbMigrator` 必须共用 Data Protection 密钥环（租户独立库连接串加密存放在控制库里）。本机两边的内容根不同，需要时把密钥目录指向同一处：`dotnet user-secrets set "DataProtection:KeysPath" "<本机目录>" --project src/CompanyName.ProjectName.Api`。
<!--#endif-->

模板预置基线迁移和独立 `DbMigrator`。API 启动时不自动修改 schema；本地和发布环境都先运行迁移入口，再启动 API。

`DbMigrator` **默认只读预演**——列出待执行的迁移与 SQL，不改库；确认后再加 `--apply` 施加：

```bash
cd backend
dotnet run --project src/CompanyName.ProjectName.DbMigrator             # 预演：只看清单与 SQL
dotnet run --project src/CompanyName.ProjectName.DbMigrator -- --apply  # 确认后施加
dotnet run --project src/CompanyName.ProjectName.Api
```

<!--#if (IncludeMultiTenancy)-->
**首次安装**时控制库尚未迁移，租户注册表还不存在，预演只列出此刻能确定的目标（控制面、OIDC 存储、默认业务库）——
此时表里本就不可能有独立库租户，所以这份计划是准确的。`--apply` 会先建好控制表，再枚举独立库租户目标并一并施加。

控制库**已迁移**之后若仍读不到租户注册表（表被误删、schema 配错、迁移与模型不一致），
那是损坏而不是首装：预演与 `--apply` 都以非零退出码结束，不会静默当作"没有独立目标"。

**单个租户出问题不挡住其他租户**：某个租户取不出本服务的连接（只登记了别的服务的连接名、连接串解不开），
或它的独立库迁移出错（连不上、迁移失败），只记在它名下，其余独立库照常预演或施加；
跑完后逐个报出这些租户与库（租户标识、库的指纹与原因，不含连接串），并以非零退出码结束。
控制库、OIDC 存储与默认业务库出错仍立即结束本次运行。
因此非零退出可能意味着**部分库已经迁移**：迁移不会回滚，发布流水线据退出码拦住新版本 API，
新旧 schema 并存的兼容性按[部署说明](docs/deploy/README.md)里 Expand 阶段的要求保证；修好问题后重跑 `--apply`，已迁移的库不会重复执行。

API 和 `DbMigrator` 使用不同的 Runtime/Migration Secret；API 运行身份只持有 DML 权限。SharedDatabase 中各服务共用数据库实例、使用固定独立 schema 并以 `TenantId` 隔离；DedicatedDatabase 由租户配置覆盖连接，各服务仍共用该租户连接并写入自己的 schema。
首次建立 DedicatedDatabase 租户前，先以 `ConnectionStrings__MigrationTarget` 运行各服务 DbMigrator 预建该服务 schema，再创建租户；常规发布仍使用全目标枚举模式。

<!--#endif-->

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

库里还没有超级管理员时，启动会按 `DefaultAdmin` 创建一个：

- 用户名：`admin`
- 密码：开发环境取 `appsettings.Development.json` 里的演示口令 `Admin!Local2026`。它随仓库公开，
  用它建出的账号会一直保留这个口令直到被修改；**连接共享的真实开发库之前**，先用 user-secrets 覆盖：

  ```bash
  cd backend
  dotnet user-secrets set "DefaultAdmin:Password" "<至少 12 个字符的口令>" --project src/CompanyName.ProjectName.Api
  ```

- 部署环境注入 `DefaultAdmin__Password`。基础配置里没有可用的默认口令：只在创建管理员那一刻校验，
  缺失或不满足密码策略（至少 12 个字符）即启动失败并报出键名；已有管理员的部署不必再提供。
<!--#endif-->

<!--#if (SpaFrontend)-->
### 前端

```bash
cd frontend
npm ci
npm start
```

浏览器打开 `http://localhost:4200`：开发服务器把 API 与 Hub 请求转发给本机后端，前后端同源。Mock 与后端端口的调整见 [前端说明](frontend/README.md)。

<!--#endif-->

## 验证

```bash
dotnet test backend/CompanyName.ProjectName.sln
<!--#if (SpaFrontend)-->
npm --prefix frontend run lint
npm --prefix frontend run build
<!--#endif-->
```

## AI 协作

项目级 [协作 Skill](.agents/skills/leistd-project-workflow/SKILL.md) 按任务加载开发、验证或环境交付 reference。[项目文档入口](docs/README.md)、源码、配置和测试提供工程事实。

[Codex](https://developers.openai.com/codex/skills) 等原生发现 `.agents/skills/` 的 AI CLI 直接使用。[Claude Code](https://code.claude.com/docs/en/skills#where-skills-live) 读取 `.claude/skills/`：在项目根建一次目录链接并提交到项目仓库，此后克隆、新建工作树即可用，修改或新增 Skill 无需同步：

```bash
mkdir -p .claude && ln -s ../.agents/skills .claude/skills
```

Windows 需开启开发者模式并设置 `git config --global core.symlinks true`，在项目根执行 `mklink /D .claude\skills ..\.agents\skills`；未开启时检出的 `.claude/skills` 是一个文本文件，按此命令重建即可。不要用 `npx skills add ./.agents/skills --agent claude-code` 生成适配：源目录就是 `.agents/skills` 时它复制而不链接，副本不随项目 Skill 更新。AI CLI 完全不支持 Skill 时，在当前会话中明确要求它先读取 `.agents/skills/leistd-project-workflow/SKILL.md`。

使用 `Leistd.*` 组件时，按 [后端说明](backend/README.md#leistd-框架-api) 定位当前 NuGet 包版本的文档和 XML，不根据模型记忆猜测 API。

## 部署

```bash
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.override.yml up --build
```

生产部署前应确认数据库迁移、密钥、管理员密码、HTTPS、健康检查和回滚方案。
