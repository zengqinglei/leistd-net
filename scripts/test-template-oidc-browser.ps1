# 仅由 test-template-oidc-e2e.ps1 加载。夹具仅写入 .tmp 的生成宿主。
$browserFixtureSecret = New-RandomValue 'Browser!1'
$browserCertificateThumbprint = ''
$browserSession = $null
$browserStarted = $false
$browserVault = $null
$browserVaultCreated = $false
$browserCommand = 0
$expectedBrowserAuthorizes = @{}
$browserEvidence = Join-Path $runRoot 'browser'
New-Item -ItemType Directory -Force $browserEvidence | Out-Null

function Initialize-BrowserCertificate {
    $rsa = [Security.Cryptography.RSA]::Create(2048)
    try {
        $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=localhost', $rsa,
            [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $san = [Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
        $san.AddDnsName('localhost'); $san.AddIpAddress([Net.IPAddress]::Loopback)
        $request.CertificateExtensions.Add($san.Build())
        $certificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-5), [DateTimeOffset]::UtcNow.AddDays(1))
        try {
            $script:browserCertificateThumbprint = $certificate.Thumbprint
            [IO.File]::WriteAllBytes((Join-Path $runRoot 'browser-tls.pfx'), $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx, ''))
        } finally { $certificate.Dispose() }
    } finally { $rsa.Dispose() }
}

# .NET 回调而非 PowerShell 回调：后台 HttpClient 线程没有 PowerShell runspace。
Add-Type -TypeDefinition @'
using System.Net.Http;
public static class OidcBrowserTls
{
    public static HttpClientHandler CreateHandler(string thumbprint) => new HttpClientHandler {
        ServerCertificateCustomValidationCallback = (request, certificate, chain, errors) => certificate?.Thumbprint == thumbprint
    };
}
'@

function Build-BrowserFrontend([string]$Name, [string]$Generated, [string]$ApiDirectory) {
    $frontend = Join-Path $Generated 'frontend'
    Invoke-Tool 'npm' @('ci', '--no-audit', '--no-fund') "browser-npm-$Name" -WorkingDirectory $frontend | Out-Null
    $output = Join-Path $runRoot "frontend-$Name"
    Invoke-Tool 'npm' @('exec', '--', 'ng', 'build', '--configuration', 'production', '--output-path', $output) "browser-build-$Name" -WorkingDirectory $frontend | Out-Null
    $webroot = Join-Path $ApiDirectory "bin/$Configuration/net10.0/wwwroot"
    New-Item -ItemType Directory -Force $webroot | Out-Null
    Copy-Item (Join-Path $output 'browser/*') $webroot -Recurse -Force
}

function Add-BrowserFixture([string]$Name, [string]$ApiDirectory) {
    $title = [Globalization.CultureInfo]::InvariantCulture.TextInfo.ToTitleCase($Name)
    $registration = @'
using System.Net.Http;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
namespace E2E.__NAME__.Api;
internal static class BrowserFixtureRegistration
{
    internal static HttpClientHandler Tls(IConfiguration config) => new() {
        ServerCertificateCustomValidationCallback = (request, cert, chain, errors) => cert?.Thumbprint == config["E2E:CertificateThumbprint"]
    };
    public static void Add(IServiceCollection services, IConfiguration config)
    {
        services.PostConfigureAll<HttpClientFactoryOptions>(options => options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = Tls(config)));
        services.Configure<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(E2E.__NAME__.Application.Shared.AuthenticationSchemeNames.SessionCookie, options => {
            var validate = options.Events.OnValidatePrincipal;
            options.Events.OnValidatePrincipal = context => context.Request.Path == "/api/e2e/browser-ticket" && context.Request.Headers["X-E2E-Secret"] == config["E2E:BrowserSecret"]
                ? Task.CompletedTask : validate(context);
        });
        __REGISTER__
    }
}
'@
    $extra = ''
    if ($Name -in @('orders','billing')) {
        $extra = 'services.Configure<Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectOptions>(E2E.__NAME__.Application.Shared.AuthenticationSchemeNames.OpenIdConnect, options => options.BackchannelHttpHandler = Tls(config));'
    }
    if ($Name -eq 'idp') {
        $extra = @'
        services.Configure<OpenIddict.Server.OpenIddictServerOptions>(options => {
            if (int.TryParse(config["E2E:BrowserAccessTokenSeconds"], out var seconds)) options.AccessTokenLifetime = TimeSpan.FromSeconds(seconds);
        });
        services.Configure<Microsoft.AspNetCore.Authentication.Google.GoogleOptions>(E2E.Idp.Application.Shared.AuthenticationSchemeNames.ExternalProviderPrefix + "google", options => Configure(options, "google"));
        services.Configure<AspNet.Security.OAuth.GitHub.GitHubAuthenticationOptions>(E2E.Idp.Application.Shared.AuthenticationSchemeNames.ExternalProviderPrefix + "github", options => Configure(options, "github"));
        void Configure(Microsoft.AspNetCore.Authentication.OAuth.OAuthOptions options, string provider)
        {
            var origin = config["E2E:ProviderOrigin"]!;
            options.AuthorizationEndpoint = origin + "/api/e2e/provider/" + provider + "/authorize";
            options.TokenEndpoint = origin + "/api/e2e/provider/" + provider + "/token";
            options.UserInformationEndpoint = origin + "/api/e2e/provider/" + provider + "/user";
            options.BackchannelHttpHandler = new ProviderBackchannel(config, Tls(config));
        }
'@
        [IO.File]::WriteAllText((Join-Path $ApiDirectory 'BrowserProviderController.cs'), $browserProviderSource, $utf8)
    }
    $registration = $registration.Replace('__REGISTER__',$extra).Replace('__NAME__',$title)
    [IO.File]::WriteAllText((Join-Path $ApiDirectory 'BrowserFixtureRegistration.cs'), $registration, $utf8)
    [IO.File]::WriteAllText((Join-Path $ApiDirectory 'BrowserTicketController.cs'), $browserTicketSource.Replace('__NAME__',$title), $utf8)
}

$browserTicketSource = @'
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using E2E.__NAME__.Api.Auth;
using E2E.__NAME__.Application.Shared;
using System.Security.Cryptography;
using System.Text;
namespace E2E.__NAME__.Api;
[ApiController, AllowAnonymous, Route("api/e2e/browser-ticket")]
public sealed class BrowserTicketController(IConfiguration config, IOptionsMonitor<CookieAuthenticationOptions> options, IServiceProvider services) : ControllerBase
{
    [HttpPost("delete")]
    public async Task<IActionResult> Delete(string key)
    {
        if (Request.Headers["X-E2E-Secret"] != config["E2E:BrowserSecret"]) return Unauthorized();
        await services.GetRequiredService<DistributedTicketStore>().RemoveAsync(key);
        return Ok(new { deleted = true });
    }
    [HttpGet]
    public async Task<IActionResult> Get(string? key = null)
    {
        if (Request.Headers["X-E2E-Secret"] != config["E2E:BrowserSecret"]) return Unauthorized();
        var cookie = options.Get(AuthenticationSchemeNames.SessionCookie);
        // 跳过 Cookie Authenticate，观测不能提前触发 OnValidatePrincipal 续期。
        var reference = cookie.TicketDataFormat.Unprotect(cookie.CookieManager.GetRequestCookie(HttpContext, cookie.Cookie.Name!) ?? "");
        key ??= reference?.Principal.FindFirst("Microsoft.AspNetCore.Authentication.Cookies-SessionId")?.Value;
        var ticket = key is null ? null : await services.GetRequiredService<DistributedTicketStore>().RetrieveAsync(key);
        var access = ticket?.Properties.GetTokenValue("access_token");
        return Ok(new {
            exists = ticket is not null, key,
            referenceTokenCount = reference?.Properties.GetTokens().Count() ?? 0,
            referenceClaims = reference?.Principal.Claims.Select(c => c.Type).ToArray() ?? [],
            tokenNames = ticket?.Properties.GetTokens().Select(t => t.Name).ToArray() ?? [],
            expiresAt = ticket?.Properties.GetTokenValue("expires_at"),
            accessHash = access is null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(access))),
            userId = ticket?.Principal.FindFirst("e2e_user_id")?.Value ?? ticket?.Principal.FindFirst("sub")?.Value
        });
    }
}
'@

$browserProviderSource = @'
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using OpenIddict.Abstractions;
namespace E2E.Idp.Api;
internal sealed class ProviderBackchannel(IConfiguration config, HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // GitHub 的官方资料映射固定访问 /user/emails，只重定向传输目标，保留生产 OnCreatingTicket。
        if (request.RequestUri?.AbsoluteUri == "https://api.github.com/user/emails") request.RequestUri = new Uri(config["E2E:ProviderOrigin"] + "/api/e2e/provider/github/emails");
        return base.SendAsync(request, cancellationToken);
    }
}
[ApiController, AllowAnonymous, Route("api/e2e/provider")]
public sealed class BrowserProviderController(IConfiguration config, IOpenIddictTokenManager tokens) : ControllerBase
{
    private static readonly ConcurrentDictionary<string, string> Codes = new();
    [HttpGet("{provider}/authorize")]
    public IActionResult AuthorizeProvider(string provider, string redirect_uri, string state, string code_challenge, string code_challenge_method)
    {
        if (code_challenge_method != "S256" || !redirect_uri.StartsWith(config["E2E:ProviderOrigin"] + "/api/v1/external-auth/" + provider + "/signin", StringComparison.Ordinal)) return BadRequest();
        var code = Guid.NewGuid().ToString("N"); Codes[code] = code_challenge;
        var callback = QueryHelpers.AddQueryString(redirect_uri, new Dictionary<string,string?> { ["code"] = code, ["state"] = state });
        var html = "<!doctype html><html><title>Local " + provider + " provider</title><h1>Local " + provider + " protocol fixture</h1><a id='approve' href='" + System.Net.WebUtility.HtmlEncode(callback) + "'>Approve sign in</a></html>";
        return Content(html, "text/html");
    }
    [HttpPost("{provider}/token")]
    public async Task<IActionResult> Token(string provider)
    {
        var form = await Request.ReadFormAsync();
        var actual = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"].ToString())));
        if (form["client_secret"] != config["E2E:BrowserSecret"] || !Codes.TryRemove(form["code"].ToString(), out var expected) || expected != actual) return BadRequest(new { error = "invalid_grant" });
        // 留协议元数据；不记录 code、verifier 或令牌。
        System.IO.File.AppendAllText(Path.Combine(config["E2E:EvidenceDirectory"]!, "provider-events.jsonl"), JsonSerializer.Serialize(new { provider, pkce = true, operation = "token" }) + "\n");
        return Ok(new { access_token = "local-provider-access-" + provider, token_type = "Bearer", expires_in = 300 });
    }
    [HttpGet("{provider}/user")]
    public IActionResult UserInfo(string provider) => Ok(new {
        id = provider == "github" ? "900123" : "google-e2e-user", sub = "google-e2e-user", login = "github_e2e",
        email = provider + "@oidc-e2e.example.test", email_verified = true,
        name = provider + " browser e2e", avatar_url = (string?)null, picture = (string?)null
    });
    [HttpGet("github/emails")]
    public IActionResult Emails() => Ok(new[] { new { email = "github@oidc-e2e.example.test", primary = true, verified = true } });
    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke(string subject)
    {
        if (Request.Headers["X-E2E-Secret"] != config["E2E:BrowserSecret"]) return Unauthorized();
        var count = 0;
        await foreach (var token in tokens.FindBySubjectAsync(subject))
            if (await tokens.GetTypeAsync(token) == OpenIddictConstants.TokenTypeIdentifiers.RefreshToken && await tokens.TryRevokeAsync(token)) count++;
        return Ok(new { count });
    }
}
'@

function Invoke-Browser([string[]]$Arguments, [string]$InputText, [switch]$AllowFailure) {
    $script:browserCommand++
    # 一次性验收不持久化浏览器状态；自动保存会用临时页面访问已见过的源，干扰真实到期与 UI 导航。
    $browserArgs = @('--session', $browserSession, '--restore', '--restore-save', 'never', '--headed', '--ignore-https-errors', '--args', '--disable-gpu', '--json') + $Arguments
    $response = Invoke-Tool 'agent-browser' $browserArgs ('browser/command-{0:d4}' -f $browserCommand) -StandardInput $InputText -AllowFailure -TimeoutSeconds 45
    [IO.File]::WriteAllText((Join-Path $runRoot ('browser/command-{0:d4}.log' -f $browserCommand)), ('command={0}; exit={1}' -f ($Arguments[0]), $response.ExitCode), $utf8)
    if (-not $response.Output) { if ($AllowFailure) { return $null }; throw "浏览器操作 $($Arguments[0]) 超时或没有响应。" }
    $parsed = ConvertFrom-Json -InputObject $response.Output -AsHashtable
    if (-not $parsed.success) { if ($AllowFailure) { return $null }; throw "浏览器操作失败：$($parsed.error)" }
    return $parsed.data
}

function Invoke-BrowserJs([string]$Code) { (Invoke-Browser @('eval', '--stdin') -InputText $Code).result }
function Get-BrowserTicket([string]$Name = 'orders', [string]$Key) {
    $cookies = Invoke-Browser @('cookies','get')
    $title = [Globalization.CultureInfo]::InvariantCulture.TextInfo.ToTitleCase($Name)
    $reference = @($cookies.cookies | Where-Object { $_.name -eq (Get-SessionCookieName $title) })
    $headers = @{ 'X-E2E-Secret' = $browserFixtureSecret }
    if ($reference.Count) { $headers.Cookie = $reference[0].name + '=' + $reference[0].value }
    $url = $urls[$Name] + '/api/e2e/browser-ticket'
    if ($Key) { $url += '?key=' + [Uri]::EscapeDataString($Key) }
    return (Assert-Http 'ticket-probe-status' 200 $url -Headers $headers).Data
}
function Wait-BrowserUrl([string]$Pattern) {
    $prefix = $Pattern.EndsWith('*')
    $target = $Pattern.TrimEnd('*')
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
    do {
        $actual = (Invoke-Browser @('get','url')).url
        if (($prefix -and $actual.StartsWith($target, [StringComparison]::Ordinal)) -or $actual -ceq $target) { return }
        Start-Sleep -Milliseconds 200
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    # 失败现场：截图、按钮状态与控制台，供定位"操作已执行但页面未跳转"。
    # 每项单独容错：页面上下文已销毁或浏览器失联时，抛出的仍是原始的导航超时。
    $label = 'url-timeout-{0:d4}' -f $browserCommand
    $captures = [ordered]@{
        screenshot = { Invoke-Browser @('screenshot', (Join-Path $browserEvidence "$label.png")) | Out-Null }
        buttons = { Write-JsonFile (Join-Path $browserEvidence "$label-buttons.json") (Invoke-BrowserJs '[...document.querySelectorAll("button")].map(b=>{const r=b.getBoundingClientRect();return {text:b.textContent.trim(),disabled:b.disabled,x:r.x,y:r.y,w:r.width,h:r.height};})') }
        console = { Write-JsonFile (Join-Path $browserEvidence "$label-console.json") (Invoke-Browser @('console')) }
    }
    $captureErrors = @()
    foreach ($capture in $captures.GetEnumerator()) {
        try { & $capture.Value } catch { $captureErrors += "$($capture.Key)：$($_.Exception.Message)" }
    }
    if ($captureErrors) { [IO.File]::WriteAllText((Join-Path $browserEvidence "$label-capture-errors.txt"), ($captureErrors -join "`n"), $utf8) }
    throw "浏览器未到达 $target（实际路由 $(([Uri]$actual).GetLeftPart([UriPartial]::Path))），现场见 browser/$label*。"
}
function Save-BrowserEvidence([string]$Label) {
    $current = (Invoke-Browser @('get','url')).url
    if ($current.StartsWith($urls.orders + '/workspace', [StringComparison]::Ordinal)) { Wait-BrowserElement 'app-user-menu button' } else { Wait-BrowserElement '#usernameOrEmail' }
    $picture = Join-Path $browserEvidence "$Label.png"
    Invoke-Browser @('screenshot', $picture) | Out-Null
    Assert-Value "$Label-screenshot-saved" $true ((Test-Path $picture) -and (Get-Item $picture).Length -gt 0)
    $state = Invoke-BrowserJs '({url:location.href,local:{...localStorage},session:{...sessionStorage},visibleCookies:document.cookie})'
    Write-JsonFile (Join-Path $browserEvidence "$Label-storage.json") $state
    Assert-Value "$Label-no-oauth-storage" $false ([bool](($state.local,$state.session,$state.visibleCookies | ConvertTo-Json -Depth 10 -Compress) -match '(access_token|refresh_token|id_token|local-provider-access|eyJ[a-zA-Z0-9_-]+\.)'))
    $errors = Invoke-Browser @('errors')
    Write-JsonFile (Join-Path $browserEvidence "$Label-errors.json") $errors
    Assert-Value "$Label-no-page-errors" 0 @($errors.errors).Count
    $console = Invoke-Browser @('console')
    Write-JsonFile (Join-Path $browserEvidence "$Label-console.json") $console
    Assert-Value "$Label-no-console-errors" 0 @($console.messages | Where-Object { $_.type -eq 'error' }).Count
}
function Wait-BrowserElement([string]$Selector) {
    $literal = ConvertTo-Json $Selector -Compress
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
    do {
        $ready = Invoke-BrowserJs "(() => { const e=document.querySelector($literal); return !!e && e.getBoundingClientRect().width>0; })()"
        if ($ready) { return }
        Start-Sleep -Milliseconds 200
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "浏览器元素未就绪：$Selector。"
}
# 页面刚启动时 Angular 可能仍在替换节点，点击落到被换下的元素上会被静默丢弃（实测偶发）。
# 点击后确认地址确实离开当前页；没离开才重试，不对真实断言放宽。
function Invoke-BrowserNavigationClick([string]$Role, [string]$Name) {
    $from = (Invoke-Browser @('get','url')).url
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        Invoke-Browser @('find','role',$Role,'click','--name',$Name) | Out-Null
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(5)
        do {
            if ((Invoke-Browser @('get','url')).url -ne $from) {
                if ($attempt -gt 1) { [IO.File]::AppendAllText((Join-Path $browserEvidence 'click-retries.log'), "$Role $Name attempt=$attempt`n", $utf8) }
                return
            }
            Start-Sleep -Milliseconds 200
        } while ([DateTimeOffset]::UtcNow -lt $deadline)
    }
}

function Start-BrowserResource {
    Invoke-Browser @('open', $urls.orders) | Out-Null
    Wait-BrowserElement 'a[href="/workspace"]'
    Invoke-BrowserNavigationClick 'link' 'Sign In'
    # Resource 首页的 Sign In 指向受保护工作台，authGuard 直接发起后端 challenge。
    Wait-BrowserUrl ($urls.idp + '/auth/login*')
}
function Wait-BrowserButton([string]$Name) {
    $label = ConvertTo-Json $Name -Compress
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
    do {
        $ready = Invoke-BrowserJs "[...document.querySelectorAll('button')].some(b => b.textContent.trim() === $label && !b.disabled)"
        if ($ready) { return }
        Start-Sleep -Milliseconds 200
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "浏览器按钮未就绪：$Name。"
}
function Login-BrowserPassword {
    $current = Invoke-Browser @('get','url')
    Invoke-Browser @('auth','save',$browserVault,'--url',$current.url,'--username','admin','--password-stdin',
        '--username-selector','#usernameOrEmail','--password-selector','#password','--submit-selector','button[type="submit"]') -InputText $adminPassword | Out-Null
    $script:browserVaultCreated = $true
    Invoke-Browser @('auth','login',$browserVault) | Out-Null
    # 填表后立即删除凭据：中断的运行跑不到 finally，口令不能留在保管库；再次登录会重新保存。
    Invoke-Tool 'agent-browser' @('auth','delete',$browserVault) 'browser-vault-delete' | Out-Null
    $script:browserVaultCreated = $false
    # 凭据由 vault 输入；补发 input 事件使 Signal Forms 接收原生自动填充，再由真实按钮提交。
    $form = Invoke-BrowserJs '(() => { const fields=[document.getElementById("usernameOrEmail"),document.getElementById("password")]; if(fields.some(e=>!e)) return false; for(const e of fields) e.dispatchEvent(new InputEvent("input",{bubbles:true,inputType:"insertText"})); return true; })()'
    if ($form) { Wait-BrowserButton 'Sign In'; Invoke-Browser @('find','role','button','click','--name','Sign In','--exact') | Out-Null }
    Wait-BrowserUrl ($urls.orders + '/workspace*')
}
function Assert-BrowserProtected([string]$Label) {
    Wait-BrowserElement 'app-user-menu button'
    $response = Invoke-BrowserJs '(async () => { const r = await fetch("/api/e2e/natural"); return {status:r.status,body:await r.json()}; })()'
    Assert-Value "$Label-api-status" 200 $response.status
    Assert-Value "$Label-user-present" $true ([bool]$response.body.userId)
    Write-JsonFile (Join-Path $browserEvidence "$Label-api.json") $response
    $row = Invoke-Sql 'oidc_shared' ('SELECT "Id"||''|''||"Username"||''|''||coalesce("Email",'''') FROM "e2e-orders"."Users" WHERE "Id"=''{0}'';' -f $response.body.userId)
    Assert-Value "$Label-user-projected" $true ([bool]$row)
    [IO.File]::WriteAllText((Join-Path $browserEvidence "$Label-sql.txt"), $row, $utf8)
    $username = $row.Split('|')[1]
    $displayed = Invoke-BrowserJs '[...document.querySelectorAll("app-workspace-dashboard dd")].map(e=>e.textContent.trim())'
    Assert-Value "$Label-ui-displays-projected-user" $true ($displayed -contains $username)
    Write-JsonFile (Join-Path $browserEvidence "$Label-ui.json") @{ displayed = $displayed; username = $username }
    $ticket = Get-BrowserTicket
    Assert-Value "$Label-server-ticket" $true $ticket.exists
    Assert-Value "$Label-reference-no-tokens" 0 $ticket.referenceTokenCount
    Assert-Value "$Label-reference-only-session-id" @('Microsoft.AspNetCore.Authentication.Cookies-SessionId') @($ticket.referenceClaims)
    Assert-Value "$Label-server-has-access" $true ($ticket.tokenNames -contains 'access_token')
    $cookies = Invoke-Browser @('cookies','get')
    $auth = @($cookies.cookies | Where-Object { $_.name -eq (Get-SessionCookieName 'Orders') })
    Assert-Value "$Label-reference-cookie-present" 1 $auth.Count
    Assert-Value "$Label-cookie-httpOnly" $true $auth[0].httpOnly
    Assert-Value "$Label-cookie-secure" $true $auth[0].secure
    $identityTicket = Get-BrowserTicket 'idp'
    Assert-Value "$Label-identity-reference-no-tokens" 0 $identityTicket.referenceTokenCount
    Assert-Value "$Label-identity-local-ticket-no-oauth" 0 @($identityTicket.tokenNames).Count
    Assert-Value "$Label-no-external-ticket-left" 0 @($cookies.cookies | Where-Object { $_.name -match '\.External$' }).Count
    Write-JsonFile (Join-Path $browserEvidence "$Label-ticket.json") $ticket
    Save-BrowserEvidence $Label
    return $ticket
}
function Get-BrowserExpiration($Value) {
    if ($Value -is [DateTimeOffset]) { return $Value }
    if ($Value -is [DateTime]) { return [DateTimeOffset]::new($Value) }
    return [DateTimeOffset]::Parse([string]$Value, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind)
}
function Wait-RealTicketExpiration($Ticket, [string]$Label) {
    $expiration = Get-BrowserExpiration $Ticket.expiresAt
    Write-Host "$Label：真实等待服务端 access_token 边界 $($expiration.ToString('o'))（浏览器不持有令牌）。"
    while ([DateTimeOffset]::UtcNow -le $expiration.AddSeconds(1)) { Start-Sleep -Seconds 2 }
    Assert-Value "$Label-real-expired" $true ([DateTimeOffset]::UtcNow -gt $expiration)
}
function Click-BrowserLogout {
    Wait-BrowserElement 'app-user-menu button'
    Invoke-Browser @('click','app-user-menu button') | Out-Null
    Wait-BrowserElement '[role="menuitem"]'
    Invoke-Browser @('find','role','menuitem','click','--name','Sign out') | Out-Null
    Wait-BrowserUrl ($urls.orders + '/')
}
function Invoke-BrowserScenarios {
    if (-not $BrowserOnly) {
        # HTTP 场景仍用原来的 600 秒；只在浏览器阶段改为真实 90 秒边界。
        Stop-Api 'idp'
        $services.idp.Environment.E2E__BrowserAccessTokenSeconds = '90'
        Start-Api 'idp'
    }
    $session = Invoke-Tool 'agent-browser' @('session','id','--scope','worktree','--prefix',"oidc-$runId") 'browser-session'
    $script:browserSession = $session.Output.Trim()
    $script:browserVault = "oidc-$runId-admin"
    $script:browserStarted = $true
    Invoke-Browser @('open',$urls.orders) | Out-Null
    $version = Invoke-Tool 'agent-browser' @('--version') 'browser-version'
    Write-JsonFile (Join-Path $browserEvidence 'runtime.json') @{ session = $browserSession; agentBrowser = $version.Output; userAgent = (Invoke-BrowserJs 'navigator.userAgent'); headed = $true }
    Invoke-Browser @('network','har','start') | Out-Null
    Invoke-Scenario 'S1-browser' {
        Start-BrowserResource
        Login-BrowserPassword
        $script:initialBrowserTicket = Assert-BrowserProtected 'S1'
    }
    Invoke-Scenario 'S9-renewal' {
        $before = Get-BrowserTicket
        Wait-RealTicketExpiration $before 'S9-success'
        Invoke-Browser @('open', ($urls.orders + '/workspace/dashboard')) | Out-Null
        Wait-BrowserUrl ($urls.orders + '/workspace*')
        $after = Assert-BrowserProtected 'S9-renewed'
        Assert-Value 'S9-access-rotated' $true ($after.accessHash -ne $before.accessHash)
        Assert-Value 'S9-expiration-extended' $true ((Get-BrowserExpiration $after.expiresAt) -gt (Get-BrowserExpiration $before.expiresAt))
    }
    Invoke-Scenario 'S9-rejection' {
        $before = Get-BrowserTicket
        $beforeCookies = Invoke-Browser @('cookies','get')
        $copied = @($beforeCookies.cookies | Where-Object { $_.name -eq (Get-SessionCookieName 'Orders') })[0]
        $revoked = (Assert-Http 'S9-revoke-refresh' 200 ($urls.idp + '/api/e2e/provider/revoke?subject=' + $before.userId) -Method POST -Headers @{ 'X-E2E-Secret' = $browserFixtureSecret }).Data
        Assert-Value 'S9-refresh-revoked' $true ($revoked.count -gt 0)
        # 签发方会话同样结束，保证本例回到交互登录；有效 IdP 会话可以正常 SSO 再认证。
        $identityTicket = Get-BrowserTicket 'idp'
        Assert-Value 'S9-identity-ticket-present' $true $identityTicket.exists
        Assert-Http 'S9-end-identity-session' 200 ($urls.idp + '/api/e2e/browser-ticket/delete?key=' + [Uri]::EscapeDataString($identityTicket.key)) -Method POST -Headers @{ 'X-E2E-Secret' = $browserFixtureSecret } | Out-Null
        Wait-RealTicketExpiration $before 'S9-failure'
        # UI 发出的受保护请求收到 401，由真正的 auth interceptor/guard 回到登录。
        Invoke-Browser @('open', ($urls.orders + '/workspace/dashboard')) | Out-Null
        Wait-BrowserUrl ($urls.idp + '/auth/login*')
        $ticket = Get-BrowserTicket 'orders' $before.key
        Assert-Value 'S9-rejected-ticket-deleted' $false $ticket.exists
        $cookies = Invoke-Browser @('cookies','get')
        Assert-Value 'S9-rejected-cookie-cleared' 0 @($cookies.cookies | Where-Object { $_.name -eq (Get-SessionCookieName 'Orders') }).Count
        Assert-Http 'S9-copied-cookie-rejected' 401 ($urls.orders + '/api/v1/auth/me') -Headers @{ Cookie = $copied.name + '=' + $copied.value } | Out-Null
        Save-BrowserEvidence 'S9-rejected'
    }
    Invoke-Scenario 'S11-browser' {
        # 上一场景结束了 Identity 会话；从登录 UI 重新建立会话后，从真实菜单退出。
        Start-BrowserResource
        Login-BrowserPassword
        $before = Assert-BrowserProtected 'S11-before'
        $cookies = Invoke-Browser @('cookies','get')
        $copied = @($cookies.cookies | Where-Object { $_.name -eq (Get-SessionCookieName 'Orders') })[0]
        $offsets = Get-LogOffsets 'idp'
        Click-BrowserLogout
        # 退出携带指向当前会话的 hint：不出现确认页。SPA 路由在请求日志里记成 /index.html，
        # 所以观察确认页一定会调的核对接口；S12 用同一判据做正向对照
        Assert-Value 'S11-logout-without-confirmation' $false ((Read-ApiLog 'idp' $offsets) -match 'GET /api/v1/auth/logout-confirmation')
        $ticket = Get-BrowserTicket 'orders' $before.key
        Assert-Value 'S11-ticket-deleted' $false $ticket.exists
        Assert-Http 'S11-copied-cookie-rejected' 401 ($urls.orders + '/api/v1/auth/me') -Headers @{ Cookie = $copied.name + '=' + $copied.value } | Out-Null
        Invoke-Browser @('open', ($urls.orders + '/workspace')) | Out-Null
        Wait-BrowserUrl ($urls.idp + '/auth/login*')
        Save-BrowserEvidence 'S11-login-required'
    }
    foreach ($provider in @('google','github')) {
        Invoke-Scenario "external-$provider-browser" {
            if ($provider -ne 'google') { Click-BrowserLogout; Start-BrowserResource }
            $expectedBrowserAuthorizes[$provider] = Invoke-BrowserJs '(new URL(location.href)).searchParams.get("returnUrl")'
            Assert-Value "$provider-authorize-return-present" $true ([bool]$expectedBrowserAuthorizes[$provider])
            Wait-BrowserButton $(if ($provider -eq 'google') { 'Google' } else { 'GitHub' })
            Invoke-BrowserNavigationClick 'button' $(if ($provider -eq 'google') { 'Google' } else { 'GitHub' })
            Wait-BrowserUrl ($urls.idp + "/api/e2e/provider/$provider/authorize*")
            Invoke-Browser @('find','role','link','click','--name','Approve sign in','--exact') | Out-Null
            Wait-BrowserUrl ($urls.orders + '/workspace*')
            $ticket = Assert-BrowserProtected "external-$provider"
            $events = @(Get-Content (Join-Path $runRoot 'provider-events.jsonl') | ForEach-Object { ConvertFrom-Json $_ -AsHashtable } | Where-Object { $_.provider -eq $provider })
            Assert-Value "$provider-official-handler-pkce" $true (@($events | Where-Object { $_.pkce }).Count -gt 0)
            $sql = Invoke-Sql 'oidc_shared' ('SELECT "Provider"||''|''||"ProviderUserId" FROM "e2e-idp"."ExternalLoginConnections" WHERE "UserId"=''{0}'';' -f $ticket.userId)
            [IO.File]::WriteAllText((Join-Path $browserEvidence "external-$provider-link.sql.txt"), $sql, $utf8)
            Assert-Value "$provider-external-link-persisted" $true ($sql -match $provider)
        }
    }
    Invoke-Scenario 'S12-logout-confirmation' {
        # 不带 hint 的依赖方退出：Identity 在本源确认页征得同意；取消保持会话，确认后结束会话并回到登记的退出回调
        $logout = $urls.idp + '/connect/logout?client_id=orders-browser&state=s12&post_logout_redirect_uri=' +
            [Uri]::EscapeDataString($urls.orders + '/api/v1/auth/signout')
        # 第一次从另一个 site 发起（localhost 与 127.0.0.1 不同站）：真实浏览器的跨站表单 POST 不带 Lax 会话 Cookie，
        # 请求缓存后的顶层 GET 重入才带上；能进确认页（不是直接回跳）说明会话 Cookie 在重入时恢复了
        $offsets = Get-LogOffsets 'idp'
        $crossSite = ([Uri]$urls.idp).GetLeftPart([UriPartial]::Authority).Replace('127.0.0.1', 'localhost')
        Invoke-Browser @('open', $crossSite + '/') | Out-Null
        Wait-BrowserUrl ($crossSite + '/*')
        $fields = ConvertTo-Json @{ client_id = 'orders-browser'; state = 's12'; post_logout_redirect_uri = $urls.orders + '/api/v1/auth/signout' } -Compress
        $action = ConvertTo-Json ($urls.idp + '/connect/logout') -Compress
        Invoke-BrowserJs "(() => { const f=document.createElement('form'); f.method='POST'; f.action=$action; for (const [k,v] of Object.entries($fields)) { const i=document.createElement('input'); i.type='hidden'; i.name=k; i.value=v; f.appendChild(i); } document.body.appendChild(f); f.submit(); return true; })()" | Out-Null
        Wait-BrowserUrl ($urls.idp + '/auth/logout-confirm*')
        # 按钮在核对接口返回有效之后才渲染：等到它，核对请求一定已经记进日志
        Wait-BrowserElement '[data-testid="logout-confirm-cancel"]'
        $log = Read-ApiLog 'idp' $offsets
        Assert-Value 'S12-cross-site-post-cached' $true ($log -match 'POST /connect/logout')
        Assert-Value 'S12-confirmation-check-observed' $true ($log -match 'GET /api/v1/auth/logout-confirmation')
        Assert-Value 'S12-confirm-url-has-no-hint' $false ((Invoke-Browser @('get','url')).url -match '(?i)id_token')
        Wait-BrowserElement '[data-testid="logout-confirm-cancel"]'
        Invoke-Browser @('click','[data-testid="logout-confirm-cancel"]') | Out-Null
        Wait-BrowserUrl ($urls.idp + '/*')
        Assert-Value 'S12-cancel-keeps-session' 200 (Invoke-BrowserJs '(async () => (await fetch("/api/v1/auth/me")).status)()')
        Invoke-Browser @('open', $logout) | Out-Null
        Wait-BrowserUrl ($urls.idp + '/auth/logout-confirm*')
        Wait-BrowserElement '[data-testid="logout-confirm-submit"]'
        Invoke-Browser @('click','[data-testid="logout-confirm-submit"]') | Out-Null
        Wait-BrowserUrl ($urls.orders + '/*')
        # 回到 Identity 本源再查会话（不带 returnUrl 的登录页会转回首页，不以页面路由作判据）
        Invoke-Browser @('open', $urls.idp + '/') | Out-Null
        Wait-BrowserUrl ($urls.idp + '/*')
        Assert-Value 'S12-confirm-ends-session' 401 (Invoke-BrowserJs '(async () => (await fetch("/api/v1/auth/me")).status)()')
        # 停在 Identity 首页，不是登录页或工作区：只截图存证（Save-BrowserEvidence 按这两种页面等待就绪）
        $picture = Join-Path $browserEvidence 'S12-logout-confirmed.png'
        Invoke-Browser @('screenshot', $picture) | Out-Null
        Assert-Value 'S12-screenshot-saved' $true ((Test-Path $picture) -and (Get-Item $picture).Length -gt 0)
    }
    Complete-BrowserNetwork
}
function Close-BrowserFixtures {
    if ($browserStarted) {
        try { Invoke-Browser @('close') | Out-Null } catch { $cleanupErrors.Add("浏览器清理：$($_.Exception.Message)") }
        try { Invoke-Tool 'agent-browser' @('state','clear',$browserSession) 'browser-state-clear' | Out-Null } catch { $cleanupErrors.Add("本轮浏览器状态清理：$($_.Exception.Message)") }
    }
    if ($browserVaultCreated) {
        try { Invoke-Tool 'agent-browser' @('auth','delete',$browserVault) 'browser-vault-delete' | Out-Null } catch { $cleanupErrors.Add("本轮凭据清理：$($_.Exception.Message)") }
    }
}

function Complete-BrowserNetwork {
    $path = Join-Path $browserEvidence 'network.private.har'
    try {
        Invoke-Browser @('network','har','stop',$path) | Out-Null
        $har = ConvertFrom-Json ([IO.File]::ReadAllText($path)) -AsHashtable
        $entries = @($har.log.entries)
        $routes = @($entries | ForEach-Object { ([Uri]$_.request.url).AbsolutePath })
        $continuations = @{}
        Assert-Value 'browser-no-token-endpoint' $false ($routes -contains '/connect/token')
        foreach ($route in @('/api/v1/auth/login','/connect/authorize','/api/v1/auth/signin','/api/v1/auth/me','/api/v1/auth/logout','/connect/logout','/api/v1/auth/signout')) {
            Assert-Value "browser-traversed-$route" $true ($routes -contains $route)
        }
        foreach ($provider in @('google','github')) {
            Assert-Value "$provider-browser-no-token-endpoint" $false ($routes -contains "/api/e2e/provider/$provider/token")
            $original = $expectedBrowserAuthorizes[$provider]
            $continued = @($entries | Where-Object { ([Uri]$_.request.url).PathAndQuery -ceq $original })
            Assert-Value "$provider-original-authorize-resumed" $true ($continued.Count -ge 2)
            $continuations[$provider] = @{ originalAuthorizeObservedCount = $continued.Count; resumed = $continued.Count -ge 2 }
            foreach ($stage in @('challenge','signin','complete')) {
                Assert-Value "$provider-official-$stage" $true ($routes -contains "/api/v1/external-auth/$provider/$stage")
            }
        }
        Write-JsonFile (Join-Path $browserEvidence 'network-summary.json') @{ requests = $entries.Count; routes = $routes; continuations = $continuations }
        foreach ($entry in $entries) {
            Assert-Value 'network-url-no-oauth' $false ($entry.request.url -match '(?i)(access_token|refresh_token|id_token|id_token_hint)=')
            if ($entry.response.content.ContainsKey('text') -and $entry.response.content.mimeType -match 'json') {
                Assert-Value 'browser-json-no-oauth' $false ([string]$entry.response.content.text -match '"(?:access_token|refresh_token|id_token)"\s*:')
            }
            $entry.request.url = ([Uri]$entry.request.url).GetLeftPart([UriPartial]::Path)
            $entry.request.queryString = @(); $entry.request.cookies = @()
            foreach ($header in $entry.request.headers) {
                if ($header.name -match '(?i)^(authorization|cookie|x-e2e-secret)$') { $header.value = '[redacted]' }
                if ($header.name -match '(?i)^(referer|origin)$') { $header.value = $header.value.Split('?')[0] }
            }
            if ($entry.request.ContainsKey('postData')) { $entry.request.postData = @{ mimeType = $entry.request.postData.mimeType; text = '[redacted]' } }
            $entry.response.cookies = @()
            foreach ($header in $entry.response.headers) {
                if ($header.name -match '(?i)^set-cookie$') { $header.value = '[redacted]' }
                if ($header.name -match '(?i)^location$') { $header.value = $header.value.Split('?')[0] }
            }
            $null = $entry.response.content.Remove('text'); $null = $entry.response.content.Remove('encoding')
            if ($entry.response.redirectURL) { $entry.response.redirectURL = $entry.response.redirectURL.Split('?')[0] }
        }
        Write-JsonFile (Join-Path $browserEvidence 'network.har') $har
    } finally { if (Test-Path $path) { Remove-Item $path -Force } }
}
