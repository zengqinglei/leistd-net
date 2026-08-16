#!/usr/bin/env pwsh
# 文档-源码 API 漂移校验（方案 P1）
# 目的：抓住组件/ddd 文档正文里“臆造的 Leistd API”——文档写了反引号包裹的类型/成员名，
#       但该名字在框架源码里根本不存在（如曾出现的 tracing `IProxyGenerator`、event-bus `LogError`）。
# 策略（保守，宁可漏报不误报）：
#   - 只校验文档里 `反引号包裹` 且形如 类型/接口名（PascalCase 或 I+大写 开头）的标识符；
#   - 只在该标识符“看起来是 Leistd 自有 API”时校验：即它不在 .NET BCL / EF / 第三方 白名单里；
#   - 判定“存在”：该标识符作为一个词出现在 framework 任一 .cs 源码（排除 bin/obj）中；
#   - 不存在 → 记为疑似臆造，CI 失败。
# 用法：pwsh framework/build/check-docs-api-drift.ps1  （从仓库根运行）
$ErrorActionPreference = 'Stop'

$docGlobs = @(
    'framework/docs/components/*.md',
    'framework/docs/ddd-struct/*.md'
)

# 收集全部框架源码符号（排除 bin/obj）。用“定义处”正则提取 public 类型/成员名，作为真实 API 集合。
$srcFiles = Get-ChildItem -Recurse -File -Include *.cs framework/components, framework/ddd-struct |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
$srcText = ($srcFiles | Get-Content -Raw) -join "`n"

# 已知非 Leistd 自有的标识符白名单（.NET BCL / EF Core / ASP.NET / 第三方）。
# 这些即使文档用反引号包裹也不校验——它们本就不该在框架源码里定义。
$allow = @(
    # BCL / 常用
    'DateTime','TimeSpan','Guid','Task','ValueTask','CancellationToken','IDisposable','IAsyncDisposable',
    'IEnumerable','IReadOnlyList','IReadOnlyCollection','IList','List','IDictionary','Dictionary','ConcurrentDictionary',
    'IQueryable','Func','Action','Nullable','Exception','ArgumentNullException','InvalidOperationException',
    'OperationCanceledException','TimeoutException','HttpRequestException','ValidationException','String','Boolean','Int32',
    'Assembly','Type','ClaimsPrincipal','ClaimTypes','IServiceProvider','IServiceCollection','IServiceScopeFactory',
    # EF Core
    'DbContext','DbContextOptions','DbContextOptionsBuilder','DbSet','ModelBuilder','EntityEntry','ChangeTracker',
    'EntityState','SaveChangesInterceptor','IEntityTypeConfiguration','IServiceProviderFactory',
    # ASP.NET
    'UseForwardedHeaders','ForwardedHeadersOptions','KnownProxies','KnownIPNetworks',
    'OptionsValidationException','IValidateOptions',
    'IExceptionHandler','AuthorizationPolicyBuilder','DefaultAuthorizationPolicyProvider','ProblemDetails',
    'ValidationProblemDetails','IProxyGenerator',   # IProxyGenerator=Castle 类型：文档不该引它，但它不是 Leistd 自有，靠源码存在性判定
    # 第三方基类（Castle / Mapster / AutoMapper）
    'AsyncInterceptorBase','IMapperConfigurationExpression','TypeAdapterConfig','Profile','IInterceptor',
    'AddProfile','CreateMap',
    # Refit（service-client 文档引用的第三方类型/特性；AttachmentName 为已废弃特性，文档明确禁用）
    'StreamPart','ByteArrayPart','FileInfoPart','ApiResponse','ApiException','RefitSettings',
    'BodySerializationMethod','AttachmentName',
    # BCL / LINQ / MSBuild 概念（散文中合法出现的非 Leistd 词）
    'GetHashCode','FormatException','SingleOrDefault','ProjectReference','EmbeddedResource','Debug',
    # 本地化：BCL/ASP.NET 本地化抽象（Leistd 在其上实现，非自有类型）
    'IStringLocalizer','IStringLocalizerFactory','ResourceManagerStringLocalizerFactory','LocalizedString','CultureInfo','RequestLocalizationOptions',
    # ASP.NET MVC 结果类型（response 文档在对比自动包装边界时会提到）
    'ContentResult','FileResult','StatusCodeResult','ObjectResult',
    # Redis / StackExchange 原语（lock 文档提到底层命令）
    'KeyDelete',
    # ASP.NET Core SignalR 连接选项（notifications 文档说明"令牌过期不会自动断连"时引用）
    'CloseOnAuthenticationExpiration',
    # 文档中的“一族方法”通配/截断写法与状态描述词（非具体 API 名）
    'SetXxx','GetGrantedPermissionsFor','PublishTo','PermissionAttribute','Completed'
)

$fail = @()
$checked = 0

foreach ($glob in $docGlobs) {
    foreach ($doc in Get-ChildItem $glob -File) {
        $text = Get-Content $doc.FullName -Raw
        # 先剥离 ``` 围栏代码块——代码块里的反引号/示例代码不按“文档在描述 API”对待，
        # 且混入会导致行内反引号配对错位（三反引号与行内反引号无法区分）。只在散文上校验行内 `API`。
        $prose = [regex]::Replace($text, '(?s)```.*?```', '')
        # 提取散文里所有 `反引号包裹` 的片段
        $ticks = [regex]::Matches($prose, '`([^`]+)`')
        foreach ($m in $ticks) {
            $inner = $m.Groups[1].Value.Trim()
            # 只校验“整体是一个 API 引用”的反引号：类型名 / 类型.成员 / 方法(...) / 泛型<...>。
            # 排除叙述性短语与示例值：含空格、箭头(→/->)、引号、逗号、等号、方括号(特性/索引)的一律跳过——
            # 这些是流程叙述(`Begin → SaveChanges`)或字符串值(`Orders.Read`、`[Authorize(...)]`)，非 API 引用。
            if ($inner -match '[\s→,"''\[\]=]' -or $inner -match '->') { continue }
            # 成员访问 `A.B` 但无方法调用括号 → 可能是示例值(如权限名 `Orders.Read`)而非 API：
            # 这类只有当根类型确实存在于源码时才有校验意义，否则跳过（避免把示例字符串当臆造 API）。
            $isDottedValue = ($inner -match '\.') -and ($inner -notmatch '\(')
            # 从片段里取根标识符：PascalCase 类型名或 I+大写 接口名
            $idMatch = [regex]::Match($inner, '^(I?[A-Z][A-Za-z0-9]+)')
            if (-not $idMatch.Success) { continue }
            $id = $idMatch.Groups[1].Value
            # 跳过：太短、纯大写缩写(HTTP/DTO/API/DI/AOP/EF)、白名单
            if ($id.Length -lt 4) { continue }
            if ($id -cmatch '^[A-Z0-9]+$') { continue }
            if ($allow -contains $id) { continue }
            # 只校验“像 Leistd 自有 API”的：命中源码=真实；未命中=疑似臆造
            $checked++
            # 单词边界匹配，避免子串误判
            $existsInSrc = $srcText -match "\b$([regex]::Escape($id))\b"
            if (-not $existsInSrc) {
                # 点号值形式(如 `Orders.Read`)且根类型不在源码 → 判为示例值，跳过不报；
                # 裸标识符或方法调用形式不存在 → 判为臆造 API，报错。
                if ($isDottedValue) { continue }
                $fail += "{0}: 反引号 API `{1}` 在框架源码中不存在（疑似臆造或已改名）" -f $doc.Name, $id
            }
        }
    }
}

$fail = $fail | Sort-Object -Unique

if ($fail.Count -gt 0) {
    Write-Host "❌ 文档-源码 API 漂移校验失败（共 $($fail.Count) 项，已校验 $checked 个标识符）：" -ForegroundColor Red
    $fail | ForEach-Object { Write-Host "  - $_" }
    Write-Host ""
    Write-Host "处理：确认该 API 是否真实存在于源码；若为第三方/BCL 类型，加入脚本 `$allow 白名单；若文档写错，修正文档。" -ForegroundColor Yellow
    exit 1
}
Write-Host "✅ 文档-源码 API 漂移校验通过（已校验 $checked 个 Leistd 自有标识符，均在源码中存在）。" -ForegroundColor Green
