<#
.SYNOPSIS
    已废弃符号与表述扫描闸门。

.DESCRIPTION
    功能权限已改为纯加法模型（行的存在即授予，没有"拒绝"这一态），随之删除了一批公开 API。
    文档与注释不会因为类型删除而编译失败，因此漂移只能靠扫描发现——此前就出现过
    随包文档仍在教 `PermissionGrantEffect` 的情况。

    两类规则，都刻意收窄，宁可漏也不要吵：
      1. 已删除的符号 —— 名字本身就是证据；
      2. 断言旧模型的**短语** —— 不用裸词。"三态""显式拒绝"在别处是合法的：
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
    "PermissionGrantRecord\.ForRole"
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
        @{ Rule = "phrase"; Text = "任一来源三态组合后取并集";                        ShouldMatch = $true }
        @{ Rule = "phrase"; Text = "亮/暗/跟随系统三态";                              ShouldMatch = $false }
    )

    $failures = New-Object System.Collections.Generic.List[string]
    foreach ($case in $cases) {
        $matched = $false

        if ($case.Rule -eq "symbol") {
            foreach ($symbol in $retiredSymbols) {
                if ($case.Text -match $symbol) { $matched = $true; break }
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

Write-Host "✅ 废弃符号/表述扫描通过（已扫描 $scanned 个文件）。" -ForegroundColor Green
