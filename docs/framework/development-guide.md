# Leistd 框架开发规范

本文档面向**人与 AI**：在 `framework/` 内新增或修改组件时，必须遵循以下约定，以保证框架的一致性、可打包性与可调试性。

---

## 1. 命名与分组

- 程序集 / 包名：`Leistd.<领域>[.<实现>]`
  - 领域抽象核心：`Leistd.<领域>.Core`（如 `Leistd.Lock.Core`）
  - 具体实现：`Leistd.<领域>.<技术>`（如 `Leistd.Lock.Redis`、`Leistd.ObjectMapping.Mapster`）
  - ASP.NET Core 集成：`Leistd.<领域>.AspNetCore`
- 目录归属：
  - 共享组件放 `framework/components/<kebab-分组>/Leistd.Xxx/`，分组与现有保持一致。新分组用 kebab-case。
  - DDD 基础类型放 `framework/ddd-struct/Leistd.Ddd.Xxx/`。
- `PackageId` 默认等于项目名（= 程序集名），**无需**在 csproj 显式设置。
- **包名回答「装哪个」，命名空间回答「这个概念叫什么」——两者刻意解耦。**
  打包边界的调整（把类型从抽象包挪到实现包）不应变成消费者的源码破坏；
  一个概念也不该让调用方记两个词。微软自己就是这么做的：
  `Microsoft.Extensions.Logging.Abstractions` 里的 `ILogger` 在 `Microsoft.Extensions.Logging`，
  `Microsoft.Extensions.Caching.Abstractions` 里**一个 `.Abstractions` 命名空间都没有**。
  结论一句话：**打包后缀不进命名空间。**

- `RootNamespace` 规则（由 `scripts/check-csproj-conventions.py` 强制）：
  - **包名以 `.Core` 结尾的一律显式声明为剥掉 `.Core` 的形态**（`Leistd.Security.Core` → `Leistd.Security`，
    根原语包 `Leistd.Core` → `Leistd`）。
  - 其余项目**一律不声明**——默认已等于程序集名，写出来只是噪声。
  - 类型的命名空间 = `RootNamespace` + 从项目根到该文件的目录路径，由 `framework/.editorconfig`
    的 IDE0130（severity=error）机械保证。

- **子命名空间的两条硬规则**（由同一道闸门强制，检查目录名）：
  - ❌ 与包名重复：`Leistd.Exception.Exceptions`、`Leistd.Lock.Memory.Locks`、`Leistd.EventBus.Local.EventBus`。
  - ❌ 缩写：`Uow`。FDG 明确要求避免缩写，且这类往往同时是自重复。
  - ✅ 描述内容的分段：`Leistd.Security.Users`、`Leistd.Timing`、`Leistd.MultiTenancy.ConnectionStrings`、
    `Leistd.Response.AspNetCore.Filters`。

- **包内组织有两种合法形态，按家族有没有并列的子话题来选：**

  | 家族形态 | 组织方式 | 例 |
  | --- | --- | --- |
  | 有并列子话题 | **按内容分**：契约与实现同处一个内容目录 | `Leistd.Security.Core` 的 `Users/`（`ICurrentUser` + `CurrentUser`）、`Claims/`、`Clients/`；`Leistd.Core` 的 `Timing/`；`Leistd.UnitOfWork.Core` 的包根 / `Database/` / `Options/` |
  | 只有单一中心概念 | **按层次分**：`Abstractions/` 放契约、`Services/` 放实现 | `lock`、`event-bus`、`object-mapping`、`notifications`、`multi-tenancy` 等 14 个家族 |

  按内容分对消费者更省事（`using Leistd.Security.Users;` 一次拿到接口与实现），
  按层次分则要为契约多写一个 `using`——这是接受的取舍，与微软把 `ILogger` 放在
  `Microsoft.Extensions.Logging` 根上的做法不同。选按层次分的理由是源码组织：
  契约与实现共存于一个包时（`Leistd.MultiTenancy.Core` 30 个文件中 18 个含实现），
  把契约归拢便于查找；在「目录即命名空间」（IDE0130）下这必然反映到 API 表面。

- **两种形态里 `Services/` 一律只放实现**（由闸门强制）。它在框架内恒定表示「实现」，
  契约混进去会让这个词失去含义。契约归 `Abstractions/` 或内容目录。

- **扩展类（`XxxExtensions`）按形态落位**：按层次分的包放 `Extensions/`，与 `Abstractions/`、
  `Services/` 并列——它既不是契约也不是实现，那两个目录都不该收；按内容分的包则与被扩展的类型
  同处内容目录（`Leistd.Core` 的 `Timing/ClockExtensions.cs`、`Leistd.ServiceClient.Core` 的
  `Http/HttpResponseMessageExtensions.cs`）。这条不设闸门：形态由「有没有并列子话题」判定，
  没有机械依据，做成闸门会误伤按内容分的正确放法。

- `DependencyInjection.cs` 始终留在包根。

- **不要让命名空间与其中的类型同名**（FDG 明确禁止）。家族名与核心类型同名时，
  给实现类加 `Default` 前缀（`Leistd.UnitOfWork.DefaultUnitOfWork`），与
  `DefaultPermissionChecker` / `DefaultDataScopeApplier` 一致。

- **家族名不要遮蔽 BCL 类型**。异常处理家族使用 `Leistd.ExceptionHandling`，避免 `Exception` 命名空间遮蔽 `System.Exception`。

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
- **不重复声明 `common.props` 已注入的属性**（`LangVersion` / `ImplicitUsings` / `Nullable` /
  `GenerateDocumentationFile`）。写成与继承值**不同**的值是合法覆盖（测试项目的
  `GenerateDocumentationFile=false` 就是），同值重复则由 `check-csproj-conventions.py` 拦下：
  重复声明会让改共享值只对没重复的项目生效，差异静默存在。
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

- `GenerateDocumentationFile` 已全局开启，**公共 API 必须写 XML 文档注释**（`///`）。缺注释（CS1591）是构建错误。
- **语言约定**：XML 与代码注释用**中文**；异常消息与日志消息面向运维，**统一用英文**。
- `cref` 必须可解析（避免 CS1574）。
- 组件用法文档位于 `framework/docs/components/` 与 `framework/docs/ddd-struct/`，骨架见 §4.3。

### 4.1 信息分层

遵循 Microsoft 的 [XML 文档建议](https://learn.microsoft.com/dotnet/csharp/language-reference/xmldoc/recommended-tags)：公开 API 至少有 `<summary>`，使用完整句子，并用 `<param>`、`<returns>`、`<exception>` 和 `cref` 表达可校验的契约。本仓库在此基础上采用以下精简规则：

| 内容 | 位置 | 约束 |
| --- | --- | --- |
| API 是什么 | `<summary>` | 一句话；全部公开成员 |
| 参数、返回值与异常 | `<param>`、`<returns>`、`<exception>` | 只补签名无法表达的信息 |
| 前置条件、失败形态、顺序、线程和生命周期 | `<remarks>` | 只写会改变正确用法的契约 |
| 主要注册入口和非显然主路径 | `<example>` + `<code>` | 使用真实、可编译的最小示例 |
| 组件安装、注册、默认行为和限制 | `framework/docs/` | 面向消费者，不复制 XML |
| 看似可删但必须保留的实现约束 | 行内 `//` | 独占一行，通常 1–3 行 |
| 实施过程和历史 | Git、PR、CI | 不写入源码和分发文档 |

- `<remarks>` 只保留会改变正确用法的契约；设计论证放组件文档，过程记录交给 Git。短是默认方向，完整性优先于行数。
- `<example>` 用于主要注册入口和容易误用的主路径；可从签名直接推出的调用不补示例。
- 接口或基类定义公共契约；实现没有新增语义时使用 `<inheritdoc/>`，不复制同一段说明。
- XML 不使用 Markdown `**…**`；行内代码用 `<c>`，引用 API 用 `<see cref="..."/>`。
- `<para>` 仅用于两个以上段落。
- 行内注释解释“为什么必须这样”，不复述代码正在做什么。

Microsoft 没有规定注释密度、`<remarks>` 行数或示例配额。本仓库也不为这些数字设硬闸门；统计只用于发现趋势，审查仍回到必要性、准确性与唯一性。

### 4.2 由闸门保证的部分

以下判据由构建或 `check-all.ps1` 保证：

| 判据 | 由谁保证 |
| --- | --- |
| 公共可见成员必须有 XML 注释 | 编译器 `CS1591`（`common.props` 未将其放入 `NoWarn`，且 `TreatWarningsAsErrors` 开启） |
| Framework 的 `///` 不挂 private / internal 成员 | `scripts/check-doc-comment-shape.py` 规则 1 |
| `///` 内不写 Markdown `**…**` | 同上，规则 2（`/api/health/**` 这类 URI 通配不误伤） |
| 组件文档必选段齐全、顺序正确、名称精确、无空壳段 | `scripts/check-docs-skeleton.py` |
| 分发面不出现路线图 / 升级动作表述 | `check-retired-terms.ps1` 的 `roadmap` 规则 |

`check-doc-comment-shape.py` 同时输出 `<summary>`、`<remarks>` 和 `<example>` 数量，仅供趋势观察。Template 只执行 XML 渲染形态检查。分发面不得出现路线图或升级过程；校验脚本必须提供自测，并在规则不再防止现实错误时删除。

### 4.3 组件文档骨架

组件文档是消费者与 AI 的入口，**标题固定才能被精确定位**，因此骨架是规范而非建议。

**必选段，固定顺序、固定名称：**

```
# <概念标题>            —— 不写包名，写它解决的问题
   导语 1 段，≤120 字：这个组件解决什么问题，怎么解决
## 何时使用             场景 → 用法 表格
## 安装                 dotnet add package
## 使用                 最小可运行示例
## 接口参考             关键类型与入口；精确签名交给随包 XML
## 注意事项             用错会出事的点
```

**可选段，有才出现、无则整段不写：**

| 段 | 出现条件 |
| --- | --- |
| `## 注册` | 需要注册服务、中间件、过滤器或端点 |
| `## 配置` | 需要在多个实现间选择或组合 Provider |
| `## 配置项（<配置节路径>）` | 有 Options 类（无绑定节路径时只写 `## 配置项`） |
| `## 实现行为` | 有非显然的运行时语义 |
| 家族特有段 | 该家族独有的话题 |
| `## 相关` | 有内容真正相关的兄弟文档 |

**顺序规则：必选段之间的相对顺序固定；可选段按读者需要它的时机插入；`## 注意事项` 与 `## 相关` 恒在最后两段。**
读者读到「注意事项」时应该已经读完全部用法——把它后面再挂一段家族话题，等于让最重要的告警不是最后一眼看到的东西。

- **没有配置项就不写 `## 配置项`**，不写「当前无配置项」。空壳段只消耗读者的目录，不提供信息。
- **`## 相关` 里不放恒定链接**。每篇都指向组件总览与依赖注入等于没有指向；无真正相关的兄弟文档时整段删除。
- **同一事实在一篇文档里只写一次**。放在读者最可能找它的那一段——通常是 `注意事项`。
- 示例代码受 §5.1 的依赖方向约束。

---

## 5. 依赖方向（不可违反）

- `Leistd.<...>.Core` / `Leistd.Ddd.Domain` 是底层，**不得**反向依赖上层或具体实现。
- `components` 可被 `ddd-struct` 依赖；**`components` 不得依赖 `ddd-struct`**（单向）。`ddd-struct` 内部 `Domain ← Application(.Contracts) ← Infrastructure` 单向。
- **`*.Core` 不得依赖"可替换的"具体技术**：Web 宿主（ASP.NET Core）、ORM（EF Core）、消息中间件等只能出现在对应实现层（`*.AspNetCore*`、`*.EntityFrameworkCore`）。判据是**能不能换掉而组件仍成立**——工作单元不接 EF 仍能提供边界与阶段，授权不接 ASP.NET 仍能判权，所以那些必须外移。
  - **例外：某项技术就是该组件主要 API 的实现机制本身时，它属于 Core。** 目前只有动态代理属于这一类，且只涉及两个包：`Leistd.UnitOfWork.Core`（`[UnitOfWork]`、`[UnitOfWorkEventHandler]`）与 `Leistd.Tracing.Core`（`[CorrelationId]`）。这些声明式特性的语义**就是**"由拦截器织入"，把代理拆出去会得到一个无法提供其主要能力的 Core——命名会更整齐，但包不再自洽。
  - 例外是**闭集**，不是逃生门：新增组件不得自行扩列。确有需要时先改本条规范并说明为什么该技术不可替换，再落代码。
  - 例外不放宽平台无关：这两个包依旧不引 Web 与 ORM。
  - 身份等概念在 `*.Core` 抽象里用中立类型（`string userId` / `ClaimsPrincipal`），不要把富身份模型（如 `ICurrentUser`）焊进核心接口签名；带技术细节的默认值（如 claim 类型）由宿主层注入而非写死在 Core。
- 一个组件**不得替另一个组件做端点映射 / 基础设施注册**（如通知组件不代映射实时 Hub）；跨组件复用通过显式调用各自的 `Add*/Map*` 完成。
- 新增跨域依赖前先评估是否会引入环，框架解决方案编译会暴露环依赖。

### 5.1 文档示例也受依赖方向约束（组件示例自包含原则）

依赖方向不仅约束代码，**也约束文档示例**——组件文档（`framework/docs/components/<家族>.md`）的示例代码，只能使用**该组件自身的公共 API + 原生 .NET / EF Core 类型**，**不得**引用它并不依赖的其它组件或 `ddd-struct` 的类型。

- 具体地，组件示例里**禁止出现** `ddd-struct` 专属类型：`IRepository<>`、`BaseAppService`、`IAppService`、`Entity<>`、`FullAuditedEntity<>`、`PagedRequestDto`/`PagedResultDto`、`GetQueryableAsync()` 等。因为 `components` 不依赖 `ddd-struct`（见上），示例若用了这些类型，就等于让上游组件的文档倒挂到下游，破坏组件独立闭环。
- **EF Core 集成组件**（`auditing`、`unit-of-work` 等）示例中出现原生 `DbContext` / `DbSet<T>` / `SaveChangesAsync` 是**合理且必要**的——它们本就围绕 EF Core 工作，这是自包含用法，不是违规。
- **与持久化/分层无关的组件**（`event-bus`、`object-mapping`、`exception`、`authorization` 等）示例用**中性普通类**演示（如 `OrderNotifier`、`OrderMapping`、`OrderService`），**不要**取名 `OrderAppService` 或强套 `: BaseAppService, IAppService`——那是在“蹭”DDD 概念却又不真正遵守其分层，反而误导读者。
- 需要指引消费者“在 DDD 项目里的正确做法”时，用**一句叙述性 cross-link** 指向 `ddd-struct.md`（例：“在采用 DDD 四层基座的项目里，实体通常继承 `FullAuditedEntity<TKey>`、经仓储读写”），**只作文字说明、不在示例代码里引入该类型**。
- “经仓储 + AppService/DTO 分层”的完整示范，归位到 **`framework/docs/ddd-struct/ddd-struct.md`**（它才引用这些类型）——那里是分层最佳实践的唯一权威出处，组件文档不重复、不承担这一职责。

> 一句话判据：**看这个组件的 csproj 引用了什么，示例就只能用什么**（外加原生 .NET）。示例引入了 csproj 里没有的组件类型 = 违规。

---

## 6. 公共 API 的设计与变更

### 6.1 设计

- 名称表达领域语义：类型用名词，方法用动词，接口使用 `I` 前缀，异步方法使用 `Async` 后缀；协议规定的方法名除外。
- 一个名称只表达一个职责；两个字段若没有独立变化和独立消费者，应合并为一个事实源。
- 可空性表达真实缺失状态，不照抄相邻类型；能够稳定推导的值不重复存储。
- 默认值必须可读、可用，并与 Options 验证和运行时行为一致。
- 共享映射与常量放在所有消费者可引用的最低层，派生值不得维护第二份。
- 公共接口优先保持最小；仅一个实现且没有替换需求时，不为形式一致额外抽象。

### 6.2 变更

- 首个公开版本前直接收敛到最终 API，不保留旧成员、桥接包、双配置键或迁移说明。
- 原子更新源码、测试、模板消费者、XML、组件文档和依赖 API 字面量的校验脚本。
- 同时检查签名变化、语义变化，以及删除成员后是否会静默绑定到基类同名成员。
- 公共 API 必须有真实消费者验证；Template 未消费时，使用隔离包消费项目或最小宿主覆盖主路径。
- 示例按契约维护：必须能编译，并与默认值、异常和生命周期语义一致。

---

## 7. 提交前自检

```bash
dotnet build framework/Leistd.Framework.slnx -c Release                         # 0 错误
dotnet test  framework/Leistd.Framework.slnx -c Release                         # 全绿
pwsh scripts/check-all.ps1                                                      # 全部静态闸门（-List 看清单）
pwsh framework/build/pack-local-feed.ps1                                        # 本地 NuGet feed（先清空再打包，PDB 内嵌）
pwsh framework/build/test-package-consumption.ps1                               # 包内容、还原和构建
```

`check-all.ps1` 是**闸门清单的唯一权威来源**（文档/API 漂移、Skill、退役符号、i18n、XML 注释形态、组件文档骨架、模板三道），
CI 也只调它一处；新增闸门加进那个脚本即可，本文件与 `ci.yml` 都不必跟着改。
需要构建产物或跑起来才能验的不在它里面——矩阵、PostgreSQL E2E、包消费各有自己的入口。

本地 NuGet 包统一输出到仓库根 `.tmp/local-feed`，不要临时发明其它产物目录；CI 发布产物仍使用 `framework/artifacts`。包消费检查会验证 DLL、XML、随包文档和依赖闭包，并在 `.tmp/package-consumer/` 使用隔离 NuGet 配置构建最小消费项目；本地可用 `-PackageIds Leistd.Xxx` 只检查受影响包。新增第三方包时确认已在 `framework/Directory.Packages.props` 登记；新增包发布前确认 `PackageId` 唯一。

## 8. 脚本与命令的跨平台约定

框架面向 Mac / Linux / Windows 三平台开发者，构建与工具命令必须可移植：

- **能用 `dotnet` CLI 直接表达的，不要包一层脚本**。`dotnet build/pack/nuget push` 三平台命令完全一致、零额外依赖——文档与 CI 直接写 `dotnet …`，不写 `pwsh xxx.ps1` 去包装它。
- **确需脚本的复合逻辑**（如文档校验 `framework/build/check-docs-*.ps1`）用 PowerShell 7（`pwsh`，本身跨平台），并遵守：
  - 首行 `#!/usr/bin/env pwsh`；
  - 路径分隔符用 `[\\/]` 正则或 `Join-Path`/`[System.IO.Path]`，不硬编码 `\`；
  - 不用 Windows 专属 cmdlet（`Get-WmiObject` 等）或调用 `cmd`/`*.exe`。
- **纯文本分析的静态闸门**（`scripts/check-*.py`）可用 Python 3（同样跨平台），本地与 CI 均以 Python 3 为前置。
  调用入口负责探测解释器（`python3` 优先，回落 `python` 并校验主版本为 3，找不到则报明确错误），
  不硬编码 `python3` 可执行名——Windows 上通常只有 `python`。
- **文档命令示例**优先给三平台通用形态；引用的脚本必须真实存在（不要写引用尚未创建的脚本的命令）。
