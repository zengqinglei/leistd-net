# 模板场景与 CI 归属的唯一来源；生成入口、汇总入口与场景覆盖闸门共同使用。
# Slices 记各档位里场景所属的分片：full 为全集（合入后、夜间、发布），pr 为 PR 档子集。
# 分片按"验证什么"命名，含义登记在 $MatrixSlices；CI 每片一个作业，作业名取其说明。
# check-template-scenario-coverage.py 保证每个条件行都由某个 PR 档场景生成（行覆盖）；
# 同一产物里几处条件分支的组合交互不在 PR 档保证之内，由 full 档兜底。
$scenarioMap = [ordered]@{
    "identity" = @{
        Slices = @{ full = "identity-role"; pr = "identity-default-and-all-features" }
        Arguments = @(); Frontend = $true; Lint = $true
        Present = @(
            "backend/src/{name}.Api/Controllers/AuthController.cs",
            "backend/src/{name}.Api/Controllers/TenantController.cs",
            "backend/src/{name}.Infrastructure/Persistence/IdentityControlDbContext.cs",
            "backend/src/{name}.Infrastructure/Persistence/Migrations/Control",
            "backend/src/{name}.DbMigrator"
        )
        Absent = @(
            "backend/src/{name}.Infrastructure/TenantConnections/IdentityTenantConnectionStore.cs",
            "backend/src/{name}.Infrastructure/Persistence/Migrations/Resource",
            "backend/src/{name}.Api/Notifications"
        )
        ReadmeContains = @()
        ReadmeExcludes = @()
        # Identity 侧自己是租户连接的来源，映射机器端点而不是消费远端存储
        RequiredTokens = @{
            "backend/src/{name}.Api/Hosting/ComponentEndpoints.cs" = @("MapTenantManagement<", "MapTenantConnections(")
        }
        # 外部登录关闭：Mock 路由、客户端方法与 DTO 都不得留下。
        # 这类残留编译、lint、单测全都放得过——Mock 会对一个后端返回 404 的端点回成功。
        ForbiddenTokens = @("external-auth", "ExternalLoginUrlOutputDto", "ExternalLoginCallbackInputDto")
    }
    "resource" = @{
        Slices = @{ full = "resource-and-standalone-roles" }
        Arguments = @("--service-role","Resource"); Frontend = $true; Lint = $true
        Present = @(
            "backend/src/{name}.Infrastructure/Persistence/Migrations/Resource",
            "backend/src/{name}.DbMigrator"
        )
        Absent = @(
            "backend/src/{name}.Api/Controllers/AuthController.cs",
            "backend/src/{name}.Api/Controllers/TenantController.cs",
            "backend/src/{name}.Infrastructure/Persistence/IdentityControlDbContext.cs",
            "backend/src/{name}.Infrastructure/Persistence/Migrations/Control",
            "frontend/src/app/features/account",
            "backend/src/{name}.Infrastructure/TenantConnections/IdentityTenantConnectionStore.cs"
        )
        ReadmeContains = @()
        ReadmeExcludes = @()
        # 租户连接由框架的远端存储包回源 Identity，模板不再手写客户端
        RequiredTokens = @{
            "backend/src/{name}.Infrastructure/{name}.Infrastructure.csproj" = @("Leistd.MultiTenancy.ServiceClient")
            "backend/src/{name}.Infrastructure/DependencyInjection.cs" = @("AddRemoteTenantConnectionStore(")
        }
        # Resource 固定使用普通路径路由，后端完成认证后返回站内路径。
        # 产物不携带哈希路由配置或本地口令登录契约。
        ForbiddenTokens = @("App.Tenants", "useHash", "withHashLocation", "LoginInputDto", "usernameOrEmail")
    }
    "standalone" = @{
        Slices = @{ full = "resource-and-standalone-roles" }
        # Cookie 会话形态：有本地用户与租户控制面，但不签发 OIDC 令牌。
        # 目的是不让内部系统带着用不到的授权服务器上线——未使用的 /connect/* 端点
        # 与 OpenIddict 存储不是"多余代码"，是需要防护、打补丁、审计的攻击面
        Arguments = @("--service-role","Standalone"); Frontend = $true; Lint = $true
        Present = @(
            "backend/src/{name}.Api/Controllers/AuthController.cs",
            "backend/src/{name}.Api/Controllers/UserController.cs",
            "backend/src/{name}.Api/Controllers/TenantController.cs",
            "backend/src/{name}.Infrastructure/Persistence/IdentityControlDbContext.cs"
        )
        Absent = @(
            "backend/src/{name}.Api/Controllers/ConnectController.cs",
            "backend/src/{name}.Api/Controllers/OpenApplicationController.cs",
            "backend/src/{name}.Application/OpenApplications",
            "backend/src/{name}.Domain/Auth/Options/OAuthOptions.cs",
            "frontend/src/app/features/platform/components/open-applications"
        )
        ReadmeContains = @()
        ReadmeExcludes = @()
        # OIDC 契约不得残留：端点路径与 OpenIddict 类型都不该出现在产物里
        ForbiddenTokens = @(
            "connect/token", "connect/authorize", "OpenIddict", "App.OpenApplications",
            # 外部登录同样关闭（见 identity 场景的同组断言）
            "external-auth", "ExternalLoginUrlOutputDto", "ExternalLoginCallbackInputDto"
        )
    }
    "identity-notifications" = @{
        Slices = @{ full = "identity-role"; pr = "identity-notifications-and-resource-localization" }
        Arguments = @("--include-notifications"); Frontend = $true; Lint = $true
        Present = @(
            "backend/src/{name}.Api/Notifications/NotificationSecurityAlertPublisher.cs",
            "frontend/src/app/layout/components/notifications/notification-service.ts"
        )
        Absent = @("backend/src/{name}.Api/Controllers/ExternalAuthController.cs")
        ReadmeContains = @()
        ReadmeExcludes = @()
        RequiredTokens = @{
            "backend/src/{name}.Api/Hosting/ComponentEndpoints.cs" = @("MapNotifications(")
        }
    }
    "resource-notifications" = @{
        Slices = @{ full = "resource-and-standalone-roles"; pr = "resource-notifications-and-standalone" }
        Arguments = @("--service-role","Resource","--include-notifications"); Frontend = $true; Lint = $true
        Present = @(
            "backend/src/{name}.Application/Notifications/AppNotificationTypes.cs",
            "frontend/src/app/layout/components/notifications/notification-service.ts"
        )
        Absent = @("backend/src/{name}.Api/Controllers/AuthController.cs", "frontend/src/app/features/account")
        ReadmeContains = @()
        ReadmeExcludes = @()
        RequiredTokens = @{
            "backend/src/{name}.Api/Hosting/ComponentEndpoints.cs" = @("MapNotifications(")
        }
        # 同 resource：不带本地口令登录契约。
        ForbiddenTokens = @("useHash", "withHashLocation", "LoginInputDto", "usernameOrEmail")
    }
    "identity-external-login" = @{
        Slices = @{ full = "identity-role" }
        Arguments = @("--include-external-login"); Frontend = $true; Lint = $true
        Present = @("backend/src/{name}.Api/Controllers/ExternalAuthController.cs", "frontend/src/app/features/account/components/external-auth-callback")
        Absent = @()
        ReadmeContains = @()
        ReadmeExcludes = @()
    }
    "standalone-external-login" = @{
        Slices = @{ full = "resource-and-standalone-roles"; pr = "resource-notifications-and-standalone" }
        Arguments = @("--service-role", "Standalone", "--include-external-login"); Frontend = $true; Lint = $true
        Present = @("backend/src/{name}.Api/Controllers/ExternalAuthController.cs", "frontend/src/app/features/account/components/external-auth-callback")
        Absent = @("backend/src/{name}.Api/Controllers/ConnectController.cs", "backend/src/{name}.Domain/Auth/Options/OAuthOptions.cs")
        ReadmeContains = @(); ReadmeExcludes = @()
        RequiredTokens = @{
            "backend/src/{name}.Api/Auth/ExternalAuthenticationExtensions.cs" = @("AddGoogle", "AddGitHub", "UsePkce = true", "UserEmailsEndpoint = string.Empty")
            "backend/src/{name}.Api/Auth/DistributedTicketStore.cs" = @("ITicketStore")
        }
        ForbiddenTokens = @("OpenIddict", "IOAuthProvider", "OAuthTokenInfo", "angular-auth-oidc-client")
    }
    "identity-localization" = @{
        Slices = @{ full = "identity-role" }
        Arguments = @("--include-localization"); Frontend = $true; Lint = $true
        Present = @("backend/src/{name}.Api/Resources/en.json", "frontend/public/i18n/en.json", "frontend/src/app/core/services/language-service.ts")
        Absent = @()
        ReadmeContains = @()
        ReadmeExcludes = @()
    }
    # 全开组合：三个可选特性两两之间的条件块会互相影响，而单开场景各自都能编过。
    # 曾漏过的实例：ExternalAuthController 的 InvalidState 工厂在「外部登录 + 本地化」
    # 同时开启时才编译失败（只开外部登录时 WithCode 那行被裁掉，只开本地化时整个文件被裁掉）。
    "identity-all-features" = @{
        Slices = @{ full = "identity-role"; pr = "identity-default-and-all-features" }
        Arguments = @("--include-notifications","--include-external-login","--include-localization")
        Frontend = $true; Lint = $true
        Present = @(
            "backend/src/{name}.Api/Notifications/NotificationSecurityAlertPublisher.cs",
            "backend/src/{name}.Api/Controllers/ExternalAuthController.cs",
            "backend/src/{name}.Api/Resources/en.json",
            "frontend/public/i18n/en.json",
            "frontend/src/app/features/account/components/external-auth-callback",
            "frontend/src/app/layout/components/notifications/notification-service.ts"
        )
        Absent = @()
        ReadmeContains = @()
        ReadmeExcludes = @()
    }
    "resource-localization" = @{
        Slices = @{ full = "resource-and-standalone-roles"; pr = "identity-notifications-and-resource-localization" }
        Arguments = @("--service-role","Resource","--include-localization"); Frontend = $true; Lint = $true
        Present = @("backend/src/{name}.Api/Resources/en.json", "frontend/public/i18n/en.json", "frontend/src/app/core/services/language-service.ts")
        Absent = @("backend/src/{name}.Api/Controllers/AuthController.cs", "frontend/src/app/features/account")
        ReadmeContains = @()
        ReadmeExcludes = @()
        # 同 resource：不带本地口令登录契约。
        ForbiddenTokens = @("useHash", "withHashLocation", "LoginInputDto", "usernameOrEmail")
    }
}

# 全量清单显式排序：定义用哈希表（无序），执行顺序要稳定才便于比对历史日志
$AllScenarios = @(
    "identity", "resource", "standalone",
    "identity-notifications", "resource-notifications",
    "identity-external-login", "standalone-external-login",
    "identity-localization", "resource-localization",
    "identity-all-features"
)

# 定义与全量清单必须一一对应。只加定义不加清单，新场景会静默不跑——
# 那比没加更糟：CI 绿着，而它本该覆盖的东西一直没被覆盖。
$definedOnly = @($scenarioMap.Keys | Where-Object { $_ -notin $AllScenarios })
$listedOnly = @($AllScenarios | Where-Object { -not $scenarioMap.Contains($_) })
if ($definedOnly.Count -gt 0) {
    throw "These scenarios are defined but absent from `$AllScenarios, so they would never run: $($definedOnly -join ', ')"
}
if ($listedOnly.Count -gt 0) {
    throw "These scenarios are listed in `$AllScenarios but have no definition: $($listedOnly -join ', ')"
}

$MatrixTiers = @("pr", "full")

# 每档的分片及其说明。片内场景按实测耗时大致均衡；分片名描述它验证什么，不用编号。
$MatrixSlices = [ordered]@{
    pr = [ordered]@{
        "identity-default-and-all-features" = "Identity 默认产物与可选特性全开"
        "resource-notifications-and-standalone"               = "Resource 通知与 Standalone 外部登录（含容器）"
        "identity-notifications-and-resource-localization"    = "Identity 通知与 Resource 本地化"
    }
    full = [ordered]@{
        "identity-role"                 = "Identity 形态全部组合"
        "resource-and-standalone-roles" = "Resource 与 Standalone 形态全部组合（含容器）"
    }
}

# 镜像与场景特性无关，每档构建一次即可：挂在两档都有的这个场景上
$ContainerScenario = "standalone-external-login"

# 本文件被各入口 dot-source：变量名不得与调用方参数同名（PowerShell 变量名不分大小写，
# 循环变量写成 $tier、$slice 会覆盖调用方的 -Tier、-Slice），故统一加 registered 前缀。
# 每个登记场景必须归属全集的一个分片；进 PR 档的场景也须归属 PR 档的一个分片；每个分片至少一个场景。
foreach ($registeredScenario in $AllScenarios) {
    $registeredSlices = $scenarioMap[$registeredScenario].Slices
    if (-not $MatrixSlices.full.Contains([string]$registeredSlices.full)) {
        throw "Scenario '$registeredScenario' must belong to a full-tier slice."
    }
    if ($registeredSlices.Contains("pr") -and -not $MatrixSlices.pr.Contains([string]$registeredSlices.pr)) {
        throw "Scenario '$registeredScenario' names an unknown PR-tier slice '$($registeredSlices.pr)'."
    }
}
foreach ($registeredTier in $MatrixTiers) {
    foreach ($registeredSlice in $MatrixSlices[$registeredTier].Keys) {
        $registeredMembers = @($AllScenarios | Where-Object { $scenarioMap[$_].Slices[$registeredTier] -eq $registeredSlice })
        if ($registeredMembers.Count -eq 0) { throw "The $registeredTier-tier slice '$registeredSlice' has no scenarios." }
    }
    if (-not $scenarioMap[$ContainerScenario].Slices.Contains($registeredTier)) {
        throw "Container scenario '$ContainerScenario' must belong to the $registeredTier tier."
    }
}

function Get-TierScenarios([string]$TierName, [string]$SliceName = "") {
    @($AllScenarios | Where-Object {
        $tierSlices = $scenarioMap[$_].Slices
        $tierSlices.Contains($TierName) -and (-not $SliceName -or $tierSlices[$TierName] -eq $SliceName)
    })
}
