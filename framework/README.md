# Leistd Framework

Leistd 是面向 **.NET 10** 的 DDD 应用框架基座，从 `leistd-net` 模板中抽取，独立版本化、以 NuGet 包形式发布，供模板生成的业务项目（及其它项目）复用。

参考 [Volo.ABP](https://abp.io) 的工程化模式：单点版本、中央包管理（CPM）、PDB 内嵌 + Source Link 源码调试。

---

## 目录结构

```
framework/
├── common.props                # 共享构建属性 + NuGet 元数据 + Source Link（单点 <Version>）
├── Directory.Build.props       # 自动导入 common.props 到所有框架项目
├── Directory.Packages.props    # CPM：第三方包版本集中声明
├── Leistd.Framework.slnx       # 框架解决方案（26 个项目）
├── NuGet.md                    # 各包的 NuGet 自述
├── build/
│   ├── pack.ps1                # 打包为 nupkg（PDB 内嵌）
│   └── push.ps1                # 推送到 NuGet 源
├── docs/                       # 文档体系（详见 docs/README.md）
│   ├── README.md               # 文档总索引
│   ├── development-guide.md    # 新增/修改组件的规范（AI 与人均按此开发）
│   ├── versioning.md           # 版本与发布规范
│   ├── _doc-template.md        # 组件文档模板规范（人+AI 协作）
│   ├── components/             # 各组件分组文档（README 总览 + 11 个分组，由源码生成）
│   └── ddd-struct/             # DDD 四层基础类型文档
├── components/                 # 共享组件（按领域分组）
│   ├── aop/                    # Leistd.DynamicProxy（动态代理/拦截器）
│   ├── core/                   # Leistd.Core（基础原语、时钟、异常）
│   ├── dependency-injection/   # Leistd.DependencyInjection（约定式注册）
│   ├── event-bus/              # Leistd.EventBus.Core / .Local（事件总线）
│   ├── exception/              # Leistd.Exception.Core / .AspNetCore（异常处理）
│   ├── lock/                   # Leistd.Lock.Core / .Memory / .Redis（分布式锁）
│   ├── object-mapping/         # Leistd.ObjectMapping.Core / .AutoMapper / .Mapster
│   ├── response/               # Leistd.Response.Core / .AspNetCore（统一响应包装）
│   ├── security/               # Leistd.Security.Core / .AspNetCore（当前用户/声明）
│   ├── tracing/                # Leistd.Tracing.Core / .AspNetCore / .HttpClient（链路追踪）
│   └── unit-of-work/           # Leistd.UnitOfWork.Core / .EfCore（工作单元）
└── ddd-struct/                 # DDD 四层基础类型
    ├── Leistd.Ddd.Domain               # 实体、聚合、仓储接口、规约、领域事件
    ├── Leistd.Ddd.Application.Contracts # 应用契约、DTO、扩展
    ├── Leistd.Ddd.Application           # 应用服务基类、权限
    └── Leistd.Ddd.Infrastructure        # 仓储实现、持久化、审计、事件总线集成
```

---

## 本地编译与打包

```bash
# 编译整个框架
dotnet build framework/Leistd.Framework.slnx -c Release

# 打包（输出 framework/artifacts/，每个项目产出 .nupkg，PDB 已内嵌）
pwsh framework/build/pack.ps1

# 推送到 NuGet（API Key 从环境变量 NUGET_API_KEY 读取）
$env:NUGET_API_KEY = "<key>"
pwsh framework/build/push.ps1                 # 默认 nuget.org
pwsh framework/build/push.ps1 -Source <私有源URL>
```

---

## 版本机制

- **单点维护**：框架版本只在 `framework/common.props` 的 `<Version>` 一处声明，所有 26 个包同步该版本。
- 发布新版本时，修改该 `<Version>`，重新 `pack` + `push` 即可。
- 第三方依赖版本统一在 `framework/Directory.Packages.props` 声明（CPM），各 csproj 仅写 version-less 的 `PackageReference`。
- 详见 [docs/versioning.md](docs/versioning.md)。

---

## 模板如何引用框架（消费方式）

模板生成的后端项目（`template/backend/src`）通过 MSBuild 属性 `LeistdUseLocalFramework` 在两种模式间切换：

| 模式 | 取值 | 引用方式 | 适用 |
|------|------|----------|------|
| NuGet（默认） | `false` | `PackageReference Include="Leistd.*"` | 发布、CI、日常开发 |
| 本地源码 | `true`  | `ProjectReference` 指向 `framework/` 源码 | 联调、断点步进框架 |

切换（仅设置环境变量，不改文件，git 保持干净）：

```bash
pwsh scripts/switch-framework.ps1 local      # 切到本地源码模式
pwsh scripts/switch-framework.ps1 nuget      # 切回 NuGet 模式
pwsh scripts/switch-framework.ps1 status     # 查看当前模式
pwsh scripts/switch-framework.ps1 local -Persist   # 对新进程/IDE 持久生效
```

框架版本与包列表见 `template/backend/Directory.Packages.props`；其版本占位符 `$(LeistdFrameworkVersion)` 在 `template/backend/Directory.Build.props` 定义，需与框架 `<Version>` 对应。

---

## 源码调试（Source Link）

所有包将 `.pdb` **内嵌进主 `.nupkg`**（不产独立 `.snupkg`），并随附 `Microsoft.SourceLink.GitHub`。开发者装包即有符号，无需符号服务器；调试时由 Source Link 按 commit 从 GitHub 远程**自动拉取**对应源码，无需手动 clone 框架仓（VS/Rider 需开启「启用源链接支持」）。

> ⚠️ **前提**：框架代码须已推送到 `RepositoryUrl` 指向的 GitHub 远程，且调试的包版本对应的 commit 可达，调试器才能拉到源码。本地尚未配置 git 远程时，构建会出现 “源链接为空” 警告，且 Source Link 暂时无法拉源——配置并推送远程后即生效。
