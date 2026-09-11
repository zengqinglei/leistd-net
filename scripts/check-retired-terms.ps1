<#
.SYNOPSIS
    已废弃符号与表述扫描闸门。

.DESCRIPTION
    功能权限已改为纯加法模型（行的存在即授予，没有"拒绝"这一态），随之删除了一批公开 API。
    文档与注释不会因为类型删除而编译失败，因此漂移只能靠扫描发现——此前就出现过
    随包文档仍在教 `PermissionGrantEffect` 的情况。

    **一条规则值得留在这里，前提是那个错误会复发。**只守着"已经过去且不会回来的事"的规则，
    修完就该删——否则闸门表会一路堆积，每条都有维护成本与误报面，最终没人再看它。
    判据示例：AI 按训练记忆写已删除的 API（会复发，留）；某次移植遗留的跨语言对照
    （一次性，清理完即删除该规则，2026-09-03 已按此删掉 java 规则）。

    三类规则，都刻意收窄，宁可漏也不要吵：
      1. 已删除的符号 —— 名字本身就是证据；
      2. 外部参考框架的痕迹 —— 交付面里不应出现"照 ABP 怎么做"这类指引；
      3. 断言旧模型的**短语** —— 不用裸词。"三态""显式拒绝"在别处是合法的：
         主题切换是亮/暗/跟随系统三态，资源实例授权保留了自己的 ResourceGrantEffect，
         而"没有显式拒绝"这类否定句正是新模型的正确表述。裸词黑名单会把它们一并报掉，
         一个吵闹的闸门很快就会被关掉。

    退出码非 0 表示存在残留，供 CI 阻断。
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,

    # 只跑规则自测再退出。规则是正则且刻意收窄过，写错一个边界就会静默不再命中，
    # 从那以后闸门永远是绿的——所以规则本身也需要被测。
    [switch]$SelfTest
)

$ErrorActionPreference = "Stop"
$problems = New-Object System.Collections.Generic.List[string]

# 变更日志与历史设计记录必须能提到被删掉的东西，否则就没法记录"删了什么"。
$historicalPaths = @(
    "docs/framework/versioning.md",
    "docs/plans/",
    "docs/assessments/"
)

# 与 docs/framework/versioning.md 里"已移除"清单保持一致：那份清单列了什么，这里就该拦什么。
#
# 用正则而不是子串：好几个被删掉的名字是现存名字的前缀或后缀，直接按子串拦会误伤。
#   \bPermissionGrant\b   —— PermissionGrantSet/Store/Record/Manager 都还活着，必须靠单词边界排除；
#   AddPermission          —— IPermissionGroupDefinition.AddPermission 是现行 API，只能拦带接收者的字面量；
#   PermissionName         —— PermissionGrantRecord.PermissionName 是现行属性，同上；
#   ForUser / ForRole      —— 裸词误伤面太大，只拦 PermissionGrantRecord 上的那两个工厂。
$retiredSymbols = @(
    "\bPermissionGrantEffect\b",
    "\bPermissionGrant\b",
    "\bIsGrantedToUserAsync\b",
    "\bIsGrantedToRoleAsync\b",
    "\bIsGrantedToAnyRoleAsync\b",
    "\bIsGrantedToUserOrRolesAsync\b",
    "\bGetGrantedPermissionsFor\w*\b",
    "\bGrantToUserAsync\b",
    "\bGrantToRoleAsync\b",
    "\bRevokeFromUserAsync\b",
    "\bRevokeFromRoleAsync\b",
    "\bGetEffectiveEffects\b",
    "IPermissionDefinitionContext\.AddPermission",
    "PermissionRequirement\.PermissionName",
    "PermissionGrantRecord\.ForUser",
    "PermissionGrantRecord\.ForRole",
    # 异常消息的可见性从"抛出点自己声明"改成了宿主的 MessageExposure 统一策略。
    # 这两个名字会复发：62 处抛出点是围绕旧模型写的，而"给这条异常加个 AsUserFacing"
    # 是照训练记忆最容易写出的那一行——它现在编译不过，但文档与注释不会。
    "\bAsUserFacing\b",
    "\bIsUserFacingMessage\b",
    "\bFallbackToExceptionMessage\b"
)

# 外部参考框架的痕迹。设计时参考过别的框架是正常的，但**交付面里不该留下指向它的引用**：
#
#   - framework/docs/ 与 template/ 是对外分发内容，出现"参考 ABP 的做法"会把使用者引向
#     一份我们并不遵循、也不会同步的外部契约；
#   - docs/ 里的稳定规范同理，读者无法判断哪部分是我们的决定、哪部分是照抄。
#
# 历史 assessment 与 plan 里记录"当时参考了什么"是证据，因此走 $historicalPaths 豁免。
#
# 用单词边界而不是子串：abp 是常见的 base64/哈希片段（package-lock 已排除，但 .md 里的
# 校验和、示例令牌同样会命中），裸子串会把它们一并报掉。
#
# 类型名那条必须显式关掉大小写不敏感（`(?-i:...)`）：PowerShell 的 `-match` 默认忽略大小写，
# 于是 `[A-Z]` 连小写也匹配，`abpx` 这种普通词会被当成 `AbpXxx` 类型名报出来。
# 自测里就有这个样例，别把它删掉。
$foreignFrameworkSymbols = @(
    "\bABP\b",
    "(?-i:\bAbp[A-Z]\w*)",
    "\bVolo\.",
    "abp\.io"
)

# 路线图与升级动作短语。§4.1 把"曾经是什么样、为什么改、升级动作"归给
# docs/framework/versioning.md（仓库内、不分发），把"将来会怎样"排除在随包内容之外：
# 消费者装上某个版本时只关心它现在是什么。
#
# **只作用于分发面**——framework 的源码与随包文档、template 载荷。docs/ 是仓库内部规划场所，
# 谈将来正是它的职责，不受本规则约束。
# 分发面：随 NuGet 包或 dotnet new 出去的内容。docs/ 是仓库内部规划与规范场所，
# 既要能谈将来，也要能引用"对应 Java XXX"这类反例来说明规则本身，因此不在作用域内。
$distributionScopes = @("framework/components", "framework/ddd-struct", "framework/docs", "template/")
$roadmapPhrases = @(
    "后续引入",
    "届时",
    "升级到本版本",
    "未来版本",
    "将来支持"
)

# 短语 → 豁免路径片段。资源实例授权那一层保留了 ResourceGrantEffect，
# "显式拒绝优先"在那里是当前正确的描述，不是漂移。
$retiredPhrases = @{
    "三态组合"     = @()
    "三态权限"     = @()
    "无法表达三态" = @()
    "含显式拒绝"   = @()
    "扣除显式拒绝" = @()
    "显式拒绝优先" = @("authorization-resource", "Authorization.Resource", "authorization-data-scope", "Authorization.DataScope")
}

function Test-Historical([string]$RelativePath) {
    # 只按前缀匹配：用 Contains 会把恰好含有相同片段的其他目录一并豁免。
    $normalized = $RelativePath.Replace('\', '/')
    foreach ($fragment in $historicalPaths) {
        if ($normalized -eq $fragment -or $normalized.StartsWith($fragment)) {
            return $true
        }
    }

    return $false
}

if ($SelfTest) {
    # 每条规则一个"应命中"与一个"不应命中"的样例。不应命中的那些全部取自现存 API，
    # 它们正是把子串换成正则的原因。
    $cases = @(
        @{ Rule = "symbol"; Text = "new PermissionGrant(name, effect)";               ShouldMatch = $true }
        @{ Rule = "symbol"; Text = "var set = new PermissionGrantSet(...);";          ShouldMatch = $false }
        @{ Rule = "symbol"; Text = "PermissionGrantRecord.PermissionName";            ShouldMatch = $false }
        @{ Rule = "symbol"; Text = "PermissionRequirement.PermissionName";            ShouldMatch = $true }
        @{ Rule = "symbol"; Text = "usersGroup.AddPermission(name, displayName)";     ShouldMatch = $false }
        @{ Rule = "symbol"; Text = "IPermissionDefinitionContext.AddPermission";      ShouldMatch = $true }
        @{ Rule = "symbol"; Text = "PermissionGrantRecord.ForUser(...)";              ShouldMatch = $true }
        @{ Rule = "symbol"; Text = "record.ForUserDisplay";                           ShouldMatch = $false }
        @{ Rule = "symbol"; Text = "grants.GetEffectiveEffects(definitions)";         ShouldMatch = $true }
        @{ Rule = "symbol"; Text = "grants.GetGrantedNames()";                        ShouldMatch = $false }
        @{ Rule = "symbol"; Text = "PermissionGrantEffect.Granted";                   ShouldMatch = $true }
        @{ Rule = "symbol"; Text = "ResourceGrantEffect.Granted";                     ShouldMatch = $false }
        @{ Rule = "symbol"; Text = ".AsUserFacing()";                                ShouldMatch = $true }
        @{ Rule = "symbol"; Text = "FallbackToExceptionMessage = true";              ShouldMatch = $true }
        @{ Rule = "symbol"; Text = "options.MessageExposure = All";                  ShouldMatch = $false }
        @{ Rule = "phrase"; Text = "任一来源三态组合后取并集";                        ShouldMatch = $true }
        @{ Rule = "phrase"; Text = "亮/暗/跟随系统三态";                              ShouldMatch = $false }
        @{ Rule = "foreign"; Text = "参考 ABP 的多租户实现";                          ShouldMatch = $true }
        @{ Rule = "foreign"; Text = "using Volo.Abp.MultiTenancy;";                   ShouldMatch = $true }
        @{ Rule = "foreign"; Text = "详见 https://abp.io/docs";                       ShouldMatch = $true }
        @{ Rule = "foreign"; Text = "AbpTenantResolver 的形态";                       ShouldMatch = $true }
        @{ Rule = "foreign"; Text = "sha512-4fjYABPvFnQ7iyaBPKKS9";                   ShouldMatch = $false }
        @{ Rule = "foreign"; Text = "labpartner 与 abpx 都是普通单词";                 ShouldMatch = $false }
        @{ Rule = "roadmap"; Text = "以及后续引入分布式权限缓存时的失效信号";                ShouldMatch = $true }
        @{ Rule = "roadmap"; Text = "需把存储改成 1:N，届时第 3、4 级按名字查";              ShouldMatch = $true }
        @{ Rule = "roadmap"; Text = "升级到本版本需要一次 EF 迁移";                        ShouldMatch = $true }
        @{ Rule = "roadmap"; Text = "当前每租户一条配置，业务 DbContext 解析到 Default";     ShouldMatch = $false }
    )

    $failures = New-Object System.Collections.Generic.List[string]
    foreach ($case in $cases) {
        $matched = $false

        if ($case.Rule -eq "symbol") {
            foreach ($symbol in $retiredSymbols) {
                if ($case.Text -match $symbol) { $matched = $true; break }
            }
        }
        elseif ($case.Rule -eq "foreign") {
            foreach ($symbol in $foreignFrameworkSymbols) {
                if ($case.Text -match $symbol) { $matched = $true; break }
            }
        }
        elseif ($case.Rule -eq "roadmap") {
            foreach ($phrase in $roadmapPhrases) {
                if ($case.Text.Contains($phrase)) { $matched = $true; break }
            }
        }
        else {
            foreach ($phrase in $retiredPhrases.Keys) {
                if ($case.Text.Contains($phrase)) { $matched = $true; break }
            }
        }

        if ($matched -ne $case.ShouldMatch) {
            $expectation = if ($case.ShouldMatch) { "应命中但没命中" } else { "不应命中却命中了" }
            $failures.Add("[$($case.Rule)] `"$($case.Text)`" $expectation")
        }
    }

    if ($failures.Count -gt 0) {
        Write-Host "规则自测失败：" -ForegroundColor Red
        foreach ($failure in $failures) { Write-Host "  $failure" -ForegroundColor Red }
        exit 1
    }

    Write-Host "✅ 规则自测通过（$($cases.Count) 个样例）。" -ForegroundColor Green
    exit 0
}

$scanRoots = @("framework", "template", "docs") |
    ForEach-Object { Join-Path $RepoRoot $_ } |
    Where-Object { Test-Path -LiteralPath $_ }

# 也扫 .html 与 .json：页面文案和 i18n 词条同样会讲解旧模型，只扫代码与文档会漏掉它们。
$files = Get-ChildItem -LiteralPath $scanRoots -Recurse -File -Include *.cs, *.ts, *.md, *.html, *.json -ErrorAction SilentlyContinue |
    Where-Object {
        $_.FullName -notmatch "[\\/](bin|obj|node_modules|dist|\.angular)[\\/]" -and
        $_.Name -notlike "package-lock.json"
    }

$scanned = 0
foreach ($file in $files) {
    $relative = $file.FullName.Substring($RepoRoot.Length).TrimStart('/', '\')
    if (Test-Historical $relative) { continue }

    $scanned++
    # 强制成数组：单行文件下 Get-Content 返回字符串，按下标取到的是字符而不是整行。
    $lines = @(Get-Content -LiteralPath $file.FullName -Encoding UTF8)

    for ($index = 0; $index -lt $lines.Count; $index++) {
        $line = $lines[$index]

        foreach ($symbol in $retiredSymbols) {
            if ($line -match $symbol) {
                $problems.Add("$relative`:$($index + 1) 引用了已删除的符号 '$symbol'")
            }
        }

        foreach ($symbol in $foreignFrameworkSymbols) {
            if ($line -match $symbol) {
                $problems.Add("$relative`:$($index + 1) 引用了外部参考框架 '$symbol'")
            }
        }

        $normalizedPath = $relative.Replace('\', '/')
        $inDistribution = $false
        foreach ($scope in $distributionScopes) {
            if ($normalizedPath.StartsWith($scope)) { $inDistribution = $true; break }
        }

        if ($inDistribution) {
            foreach ($phrase in $roadmapPhrases) {
                if ($line.Contains($phrase)) {
                    $problems.Add("$relative`:$($index + 1) 分发面出现路线图/升级动作表述 '$phrase'；归 docs/framework/versioning.md")
                }
            }
        }

        foreach ($phrase in $retiredPhrases.Keys) {
            if (-not $line.Contains($phrase)) { continue }

            $exempt = $false
            foreach ($fragment in $retiredPhrases[$phrase]) {
                if ($relative.Replace('\', '/').Contains($fragment)) { $exempt = $true; break }
            }

            if (-not $exempt) {
                $problems.Add("$relative`:$($index + 1) 使用了旧模型的表述 '$phrase'")
            }
        }
    }
}

if ($problems.Count -gt 0) {
    Write-Host "发现已废弃的符号或表述：" -ForegroundColor Red
    foreach ($problem in $problems) {
        Write-Host "  $problem" -ForegroundColor Red
    }
    exit 1
}

Write-Host "✅ 废弃符号/外部框架痕迹扫描通过（已扫描 $scanned 个文件）。" -ForegroundColor Green
