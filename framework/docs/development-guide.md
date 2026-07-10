# Leistd 框架开发规范

本文档面向**人与 AI**：在 `framework/` 内新增或修改组件时，必须遵循以下约定，以保证框架的一致性、可打包性与可调试性。

---

## 1. 命名与分组

- 程序集 / 包名：`Leistd.<领域>[.<实现>]`
  - 领域抽象核心：`Leistd.<领域>.Core`（如 `Leistd.Lock.Core`）
  - 具体实现：`Leistd.<领域>.<技术>`（如 `Leistd.Lock.Redis`、`Leistd.ObjectMapping.Mapster`）
  - ASP.NET Core 集成：`Leistd.<领域>.AspNetCore`
- 目录归属：
  - 共享组件放 `components/<kebab-分组>/Leistd.Xxx/`，分组与现有保持一致（aop、core、dependency-injection、event-bus、exception、lock、object-mapping、response、security、tracing、unit-of-work）。新分组用 kebab-case。
  - DDD 基础类型放 `ddd-struct/Leistd.Ddd.Xxx/`。
- `PackageId` 默认等于项目名（= 程序集名），**无需**在 csproj 显式设置。
- `RootNamespace` 仅在与程序集名不一致时显式设置（例：`Leistd.Security.Core` 的 `RootNamespace` 为 `Leistd.Security`）。

---

## 2. csproj 模板

新组件的 csproj 应尽量精简——共享属性由 `Directory.Build.props → common.props` 自动注入，**不要**重复声明 `LangVersion`、`Nullable`、`ImplicitUsings`、`GenerateDocumentationFile`、版本、打包元数据、Source Link。

最小示例：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <!-- 仅在与程序集名不同时设置：<RootNamespace>Leistd.Xxx</RootNamespace> -->
  </PropertyGroup>

  <ItemGroup>
    <!-- 第三方包：version-less，由 CPM 统一版本 -->
    <PackageReference Include="SomeThirdParty" />
  </ItemGroup>

  <ItemGroup>
    <!-- 框架内部引用：相对路径 ProjectReference -->
    <ProjectReference Include="..\..\core\Leistd.Core\Leistd.Core.csproj" />
  </ItemGroup>

</Project>
```

要点：
- **`PackageReference` 一律不写 `Version`**——版本在 `framework/Directory.Packages.props` 用 `<PackageVersion>` 声明。新引入的第三方包必须先在该文件登记版本，否则还原报错（CPM 已启用）。
- ASP.NET Core 能力用 `<FrameworkReference Include="Microsoft.AspNetCore.App" />`，不要直接引 Microsoft.AspNetCore.* 包。
- 纯内部、不应发布的项目，在其 csproj 设 `<IsPackable>false</IsPackable>`（默认全部可打包）。

---

## 3. 加入解决方案

新项目创建后，加入框架解决方案：

```bash
dotnet sln framework/Leistd.Framework.slnx add framework/components/<分组>/Leistd.Xxx/Leistd.Xxx.csproj
```

`.slnx` 会按目录自动归入对应解决方案文件夹。

---

## 4. 文档注释

- `GenerateDocumentationFile` 已全局开启，**公共 API 应写 XML 文档注释**（`///`）。
- `cref` 必须可解析（避免 CS1574）。缺注释的告警（CS1591）已被 `NoWarn` 容忍，但鼓励补全。

---

## 5. 依赖方向（不可违反）

- `Leistd.<...>.Core` / `Leistd.Ddd.Domain` 是底层，**不得**反向依赖上层或具体实现。
- `components` 可被 `ddd-struct` 依赖；**`components` 不得依赖 `ddd-struct`**（单向）。`ddd-struct` 内部 `Domain ← Application(.Contracts) ← Infrastructure` 单向。
- **`*.Core` 必须平台无关，不得依赖 Web（ASP.NET Core）或重型 AOP 实现（Castle 等）。** Web/AOP/EF 等具体技术只能出现在对应实现层（`*.AspNetCore*`、`*.EntityFrameworkCore`）。身份等概念在 `*.Core` 抽象里用中立类型（`string userId` / `ClaimsPrincipal`），不要把富身份模型（如 `ICurrentUser`）焊进核心接口签名；带技术细节的默认值（如 claim 类型）由宿主层注入而非写死在 Core。
- 一个组件**不得替另一个组件做端点映射 / 基础设施注册**（如通知组件不代映射实时 Hub）；跨组件复用通过显式调用各自的 `Add*/Map*` 完成。
- 新增跨域依赖前先评估是否会引入环，框架解决方案编译会暴露环依赖。

### 5.1 文档示例也受依赖方向约束（组件示例自包含原则）

依赖方向不仅约束代码，**也约束文档示例**——组件文档（`docs/components/<家族>.md`）的示例代码，只能使用**该组件自身的公共 API + 原生 .NET / EF Core 类型**，**不得**引用它并不依赖的其它组件或 `ddd-struct` 的类型。

- 具体地，组件示例里**禁止出现** `ddd-struct` 专属类型：`IRepository<>`、`BaseAppService`、`IAppService`、`Entity<>`、`FullAuditedEntity<>`、`PagedRequestDto`/`PagedResultDto`、`GetQueryableAsync()` 等。因为 `components` 不依赖 `ddd-struct`（见上），示例若用了这些类型，就等于让上游组件的文档倒挂到下游，破坏组件独立闭环。
- **EF Core 集成组件**（`auditing`、`unit-of-work` 等）示例中出现原生 `DbContext` / `DbSet<T>` / `SaveChangesAsync` 是**合理且必要**的——它们本就围绕 EF Core 工作，这是自包含用法，不是违规。
- **与持久化/分层无关的组件**（`event-bus`、`object-mapping`、`exception`、`authorization` 等）示例用**中性普通类**演示（如 `OrderNotifier`、`OrderMapping`、`OrderService`），**不要**取名 `OrderAppService` 或强套 `: BaseAppService, IAppService`——那是在“蹭”DDD 概念却又不真正遵守其分层，反而误导读者。
- 需要指引消费者“在 DDD 项目里的正确做法”时，用**一句叙述性 cross-link** 指向 `ddd-struct.md`（例：“在采用 DDD 四层基座的项目里，实体通常继承 `FullAuditedEntity<TKey>`、经仓储读写”），**只作文字说明、不在示例代码里引入该类型**。
- “经仓储 + AppService/DTO 分层”的完整示范，归位到 **`docs/ddd-struct/ddd-struct.md`**（它才引用这些类型）——那里是分层最佳实践的唯一权威出处，组件文档不重复、不承担这一职责。

> 一句话判据：**看这个组件的 csproj 引用了什么，示例就只能用什么**（外加原生 .NET）。示例引入了 csproj 里没有的组件类型 = 违规。

---

## 6. 提交前自检

```bash
dotnet build framework/Leistd.Framework.slnx -c Release                         # 0 错误
dotnet pack  framework/Leistd.Framework.slnx -c Release -o framework/artifacts   # 产出 nupkg（PDB 内嵌）
```

新增第三方包时确认已在 `Directory.Packages.props` 登记；新增包发布前确认 `PackageId` 唯一。

## 7. 脚本与命令的跨平台约定

框架面向 Mac / Linux / Windows 三平台开发者，构建与工具命令必须可移植：

- **能用 `dotnet` CLI 直接表达的，不要包一层脚本**。`dotnet build/pack/nuget push` 三平台命令完全一致、零额外依赖——文档与 CI 直接写 `dotnet …`，不写 `pwsh xxx.ps1` 去包装它。
- **确需脚本的复合逻辑**（如文档校验 `build/check-docs-*.ps1`）用 PowerShell 7（`pwsh`，本身跨平台），并遵守：
  - 首行 `#!/usr/bin/env pwsh`；
  - 路径分隔符用 `[\\/]` 正则或 `Join-Path`/`[System.IO.Path]`，不硬编码 `\`；
  - 不用 Windows 专属 cmdlet（`Get-WmiObject` 等）或调用 `cmd`/`*.exe`。
- **文档命令示例**优先给三平台通用形态；引用的脚本必须真实存在（不要写引用尚未创建的脚本的命令）。
