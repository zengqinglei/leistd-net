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
- `components` 可被 `ddd-struct` 依赖；`ddd-struct` 内部 `Domain ← Application(.Contracts) ← Infrastructure` 单向。
- 已存在的跨域引用（如 `Leistd.Security.Core → Leistd.Ddd.Domain`）保持相对路径，迁移后不变。
- 新增跨域依赖前先评估是否会引入环，框架解决方案编译会暴露环依赖。

---

## 6. 提交前自检

```bash
dotnet build framework/Leistd.Framework.slnx -c Release   # 0 错误
pwsh framework/build/pack.ps1                              # 每个可打包项目产出 nupkg（PDB 内嵌）
```

新增第三方包时确认已在 `Directory.Packages.props` 登记；新增包发布前确认 `PackageId` 唯一。
