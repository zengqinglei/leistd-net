# 仅由 test-template-oidc-e2e.ps1 在 -IncludeMultiTenantScenarios 下加载，依赖浏览器辅助文件的函数。
# 一个 Identity（租户控制面）加两个 Resource（orders、billing，各自管理本地授权）的多租户验收：
# 租户 alpha、gamma 共用共享库（行级隔离），beta 使用独立库（库级隔离）。
$mtTenants = @{}
$mtTokens = @{}
$mtSession = $null

function New-MtDedicatedDatabase([string]$Database) {
    Invoke-Docker @("exec", $container, "createdb", "-U", "postgres", $Database) | Out-Null
    $connection = $sharedConnection.Replace("oidc_shared", $Database)
    # 部署顺序：先为租户库跑各服务迁移，再在 Identity 登记租户（见生成项目部署文档）。
    foreach ($name in @("idp", "orders", "billing")) {
        $environment = $services[$name].Environment.Clone()
        $environment.ConnectionStrings__MigrationTarget = $connection
        Invoke-Tool "dotnet" @($services[$name].Migrator, "--apply") "mt-migrate-$name-$Database" -Variables $environment | Out-Null
    }
    return $connection
}

function Initialize-MultiTenantFixtures {
    # 记录宿主管理员 HTTP 会话在浏览器阶段之后的状态（曾出现已认证却无权限的 403），再重新登录。
    $me = Invoke-Http ($urls.idp + "/api/v1/auth/me") -Session $admin
    $permissions = Invoke-Http ($urls.idp + "/api/v1/permissions/current") -Session $admin
    $row = Invoke-Sql "oidc_shared" 'SELECT row_to_json(u) FROM "e2e-idp"."Users" u WHERE "Username"=''admin'' AND "TenantId" IS NULL;'
    Write-JsonFile (Join-Path $runRoot "mt-admin-session-probe.json") @{ me = $me.Status; meBody = $me.Data; permissions = $permissions.Status; permissionsBody = $permissions.Data; userRow = $row }
    $script:admin = Login
    $mtTenants.alpha = New-Tenant "mt-alpha"
    $mtTenants.gamma = New-Tenant "mt-gamma"
    $mtTenants.beta = New-Tenant "mt-beta" (New-MtDedicatedDatabase "oidc_mt_beta")
    Update-MtTokens
}

# 浏览器阶段访问令牌只有 90 秒；每个 HTTP 场景开头重新签发，避免令牌在场景中途到期。
function Update-MtTokens { foreach ($key in @("alpha", "gamma", "beta")) { $mtTokens[$key] = (Get-TenantToken $mtTenants[$key]).Token } }

function Get-MtDatabase([string]$Key) { if ($Key -eq "beta") { "oidc_mt_beta" } else { "oidc_shared" } }

function Grant-MtPermission([string]$Key, [string]$Service, [string]$Permission) {
    # Resource 没有授权起点：部署后没有任何人能经接口授予权限，运维只能直接写第一条授予。
    $tenant = $mtTenants[$Key]
    $subject = (Read-TokenClaims $mtTokens[$Key]).sub
    $sql = ('INSERT INTO "e2e-{0}"."PermissionGrantRecords" ("Id","TenantId","PermissionName","ProviderName","ProviderKey","CreationTime") ' +
        'VALUES (gen_random_uuid(), ''{1}'', ''{2}'', ''User'', ''{3}'', now());') -f $Service, $tenant.id, $Permission, $subject
    Invoke-Sql (Get-MtDatabase $Key) $sql | Out-Null
}

function Get-MtUserIds([string]$Key, [string]$Service, [hashtable]$Headers = @{}) {
    $response = Assert-Http "mt-users-$Key-$Service" 200 ($urls[$Service] + "/api/v1/users?limit=100") `
        -Token (Get-ServiceToken $mtTokens[$Key] $Service) -Headers $Headers
    return @($response.Data.items | ForEach-Object { [string]$_.id })
}

function Test-MtAuthorizationAndIsolation {
    Update-MtTokens
    # 授予先于首次权限检查，避免权限缓存掩盖结果；投影由首次访问建立。
    foreach ($key in @("alpha", "gamma", "beta")) {
        foreach ($name in @("orders", "billing")) {
            Assert-Http "mt-project-$key-$name" 200 ($urls[$name] + "/api/e2e/natural") -Token (Get-ServiceToken $mtTokens[$key] $name) | Out-Null
        }
    }
    $ids = @{}
    foreach ($key in @("alpha", "gamma", "beta")) { $ids[$key] = [string](Read-TokenClaims $mtTokens[$key]).sub }
    Assert-Http "mt-no-bootstrap-authority" 403 ($urls.orders + "/api/v1/users") -Token $mtTokens.alpha | Out-Null
    foreach ($key in @("alpha", "beta")) { Grant-MtPermission $key "orders" "App.Users" }

    $alphaUsers = @(Get-MtUserIds "alpha" "orders")
    Assert-Value "mt-alpha-sees-self" $true ($alphaUsers -contains $ids.alpha)
    Assert-Value "mt-row-isolation-list" $false ($alphaUsers -contains $ids.gamma)
    Assert-Value "mt-db-isolation-list" $false ($alphaUsers -contains $ids.beta)
    Assert-Http "mt-row-isolation-by-id" 404 ($urls.orders + "/api/v1/users/" + $ids.gamma) -Token $mtTokens.alpha | Out-Null
    $betaUsers = @(Get-MtUserIds "beta" "orders")
    Assert-Value "mt-beta-sees-only-beta" $true ($betaUsers.Count -ge 1 -and $betaUsers -contains $ids.beta -and -not ($betaUsers -contains $ids.alpha))
    Assert-Http "mt-db-isolation-by-id" 404 ($urls.orders + "/api/v1/users/" + $ids.alpha) -Token $mtTokens.beta | Out-Null

    # 框架默认租户头是 X-Tenant；Resource 只信令牌中的租户。
    $spoofed = @(Get-MtUserIds "alpha" "orders" @{ "X-Tenant" = $mtTenants.gamma.name })
    Assert-Value "mt-tenant-header-ignored" $false ($spoofed -contains $ids.gamma)
    Assert-Value "mt-tenant-header-keeps-token-tenant" $true ($spoofed -contains $ids.alpha)

    # 同一用户在不同 Resource 的授权互相独立。
    Assert-Http "mt-per-service-authorization" 403 ($urls.billing + "/api/v1/users") -Token (Get-ServiceToken $mtTokens.alpha "billing") | Out-Null
    $projection = 'SELECT count(*) FROM "e2e-billing"."Users" WHERE "Id"=''{0}'' AND "TenantId"=''{1}'';' -f $ids.alpha, $mtTenants.alpha.id
    Assert-Value "mt-billing-projection-exists" "1" (Invoke-Sql "oidc_shared" $projection)
}

function Test-MtDelegation {
    Update-MtTokens
    foreach ($key in @("alpha", "beta")) {
        $result = (Assert-Http "mt-delegate-$key" 200 ($urls.orders + "/api/e2e/billing") -Token $mtTokens[$key]).Data
        $claims = Read-TokenClaims $mtTokens[$key]
        Assert-Value "mt-delegate-user-$key" ([string]$claims.sub) ([string]$result.userId)
        Assert-Value "mt-delegate-tenant-$key" ([string]$mtTenants[$key].id) ([string]$result.tenantId)
        Assert-Value "mt-delegate-actor-$key" "orders-api" $result.clientId
    }
}

function Start-MtBrowserSession {
    $session = Invoke-Tool 'agent-browser' @('session','id','--scope','worktree','--prefix',"oidc-$runId-mt") 'mt-browser-session'
    $script:mtSession = $session.Output.Trim()
    $script:mainBrowserSession = $browserSession
    $script:browserSession = $mtSession
}

function Stop-MtBrowserSession {
    if (-not $mtSession) { return }
    try { Invoke-Browser @('close') | Out-Null } catch { $cleanupErrors.Add("多租户浏览器清理：$($_.Exception.Message)") }
    try { Invoke-Tool 'agent-browser' @('state','clear',$mtSession) 'mt-browser-state-clear' | Out-Null } catch { }
    $script:browserSession = $mainBrowserSession
}

function Login-BrowserTenant([string]$Tenant) {
    Wait-BrowserElement '#tenantName'
    $literal = ConvertTo-Json $Tenant -Compress
    Invoke-BrowserJs "(() => { const e=document.getElementById('tenantName'); e.value=$literal; e.dispatchEvent(new Event('input',{bubbles:true})); return true; })()" | Out-Null
    # 与导航点击同一原因：页面启动期点击可能被吞掉，确认租户已选定才继续，否则重试。
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        Wait-BrowserButton 'Confirm'
        Invoke-Browser @('find','role','button','click','--name','Confirm','--exact') | Out-Null
        try { Wait-BrowserElement '#tenantName[readonly]'; break }
        catch { if ($attempt -eq 3) { throw }; [IO.File]::AppendAllText((Join-Path $browserEvidence 'click-retries.log'), "tenant-confirm attempt=$attempt`n", $utf8) }
    }
    $vault = "oidc-$runId-mt-$Tenant"
    $current = Invoke-Browser @('get','url')
    Invoke-Browser @('auth','save',$vault,'--url',$current.url,'--username','admin','--password-stdin',
        '--username-selector','#usernameOrEmail','--password-selector','#password','--submit-selector','button[type="submit"]') -InputText $tenantPassword | Out-Null
    try { Invoke-Browser @('auth','login',$vault) | Out-Null }
    finally { Invoke-Tool 'agent-browser' @('auth','delete',$vault) "mt-vault-delete-$Tenant" -AllowFailure | Out-Null }
    $form = Invoke-BrowserJs '(() => { const fields=[document.getElementById("usernameOrEmail"),document.getElementById("password")]; if(fields.some(e=>!e)) return false; for(const e of fields) e.dispatchEvent(new InputEvent("input",{bubbles:true,inputType:"insertText"})); return true; })()'
    if ($form) { Wait-BrowserButton 'Sign In'; Invoke-Browser @('find','role','button','click','--name','Sign In','--exact') | Out-Null }
}

function Open-BrowserResource([string]$Name) {
    Invoke-Browser @('open', $urls[$Name]) | Out-Null
    Wait-BrowserElement 'a[href="/workspace"]'
    Invoke-BrowserNavigationClick 'link' 'Sign In'
}

function Get-BrowserMe { Invoke-BrowserJs '(async () => { const r = await fetch("/api/v1/auth/me"); return {status:r.status, body: r.status===200 ? await r.json() : null}; })()' }

function Test-MtSingleSignOn {
    Start-MtBrowserSession
    Open-BrowserResource "orders"
    Wait-BrowserUrl ($urls.idp + '/auth/login*')
    Login-BrowserTenant $mtTenants.alpha.name
    Wait-BrowserUrl ($urls.orders + '/workspace*')
    $ordersMe = Get-BrowserMe
    Assert-Value "mt-sso-orders-tenant" ([string]$mtTenants.alpha.id) ([string]$ordersMe.body.tenantId)

    # 第二个应用：Identity 会话仍有效，授权端点直接签发，不出现登录页。
    $offsets = Get-LogOffsets "idp"
    Open-BrowserResource "billing"
    Wait-BrowserUrl ($urls.billing + '/workspace*')
    $idpLog = Read-ApiLog "idp" $offsets
    Assert-Value "mt-sso-silent-authorize" $true ($idpLog -match 'GET /connect/authorize')
    Assert-Value "mt-sso-no-login-page" $false ($idpLog -match 'GET /auth/login')
    $billingMe = Get-BrowserMe
    Assert-Value "mt-sso-billing-tenant" ([string]$mtTenants.alpha.id) ([string]$billingMe.body.tenantId)
    Assert-Value "mt-sso-same-user" ([string]$ordersMe.body.id) ([string]$billingMe.body.id)
    $cookies = (Invoke-Browser @('cookies','get')).cookies
    foreach ($title in @("Orders", "Billing", "Idp")) {
        Assert-Value "mt-sso-session-cookie-$title" 1 @($cookies | Where-Object { $_.name -eq (Get-SessionCookieName $title) }).Count
    }

    # 浏览器经 orders 会话委托调用 billing，租户随令牌交换传递。
    Invoke-Browser @('open', ($urls.orders + '/workspace/dashboard')) | Out-Null
    Wait-BrowserUrl ($urls.orders + '/workspace*')
    $delegated = Invoke-BrowserJs '(async () => { const r = await fetch("/api/e2e/billing"); return {status:r.status, body: await r.json()}; })()'
    Assert-Value "mt-browser-delegate-status" 200 $delegated.status
    Assert-Value "mt-browser-delegate-tenant" ([string]$mtTenants.alpha.id) ([string]$delegated.body.tenantId)
}

function Test-MtSingleLogout {
    # orders 发起 RP 退出：结束 orders 与 Identity 会话；没有后通道退出，billing 会话不受影响。
    Click-BrowserLogout
    Invoke-Browser @('open', ($urls.orders + '/workspace')) | Out-Null
    Wait-BrowserUrl ($urls.idp + '/auth/login*')
    Invoke-Browser @('open', ($urls.billing + '/workspace/dashboard')) | Out-Null
    Wait-BrowserUrl ($urls.billing + '/workspace*')
    Assert-Value "mt-slo-billing-session-survives" 200 (Get-BrowserMe).status
    # 签发方退出不撤销 billing 持有的 refresh token：访问令牌到期后仍能静默续期。
    $before = Get-BrowserTicket 'billing'
    Wait-RealTicketExpiration $before 'MT-SLO'
    Invoke-Browser @('open', ($urls.billing + '/workspace/dashboard')) | Out-Null
    Wait-BrowserUrl ($urls.billing + '/workspace*')
    Assert-Value "mt-slo-billing-refresh-after-idp-logout" 200 (Get-BrowserMe).status
    $after = Get-BrowserTicket 'billing'
    Assert-Value "mt-slo-billing-token-rotated" $true ($after.accessHash -ne $before.accessHash)
}

function Test-MtTenantDisable {
    $before = Get-BrowserTicket 'billing'
    Assert-Http "mt-disable-alpha" 200 ($urls.idp + "/api/v1/tenants/" + $mtTenants.alpha.id + "/activation") -Method PUT -Body @{ isActive = $false } -Session $admin | Out-Null
    Assert-Value "mt-disable-window-billing" 200 (Get-BrowserMe).status
    $denied = Invoke-Http ($urls.idp + "/api/v1/auth/session-login") -Method POST -Body @{ usernameOrEmail = "admin"; password = $tenantPassword } -Headers @{ "X-Tenant" = $mtTenants.alpha.name }
    Assert-Value "mt-disabled-tenant-login-denied" $true ($denied.Status -ne 200)
    Wait-RealTicketExpiration $before 'MT-DISABLE'
    Invoke-Browser @('open', ($urls.billing + '/workspace/dashboard')) | Out-Null
    Wait-BrowserUrl ($urls.idp + '/auth/login*')
    # 落到登录页后保持稳定，不在刷新失败与重新认证之间循环。
    $offsets = Get-LogOffsets "idp"
    Start-Sleep -Seconds 5
    Assert-Value "mt-disable-no-redirect-loop" $true (([regex]::Matches((Read-ApiLog "idp" $offsets), 'GET /connect/authorize')).Count -le 1)
    Assert-Value "mt-disable-billing-ticket-removed" $false (Get-BrowserTicket 'billing' $before.key).exists
    Save-BrowserEvidence 'MT-disabled-tenant'
}

function Invoke-MultiTenantScenarios {
    try {
        Invoke-Scenario 'MT0-fixtures' { Initialize-MultiTenantFixtures }
        Invoke-Scenario 'MT1-authorization-isolation' { Test-MtAuthorizationAndIsolation }
        Invoke-Scenario 'MT2-delegation' { Test-MtDelegation }
        Invoke-Scenario 'MT3-sso-two-resources' { Test-MtSingleSignOn }
        Invoke-Scenario 'MT4-single-logout' { Test-MtSingleLogout }
        Invoke-Scenario 'MT5-tenant-disable' { Test-MtTenantDisable }
    }
    finally { Stop-MtBrowserSession }
}
