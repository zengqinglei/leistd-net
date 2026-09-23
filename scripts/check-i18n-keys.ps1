<#
.SYNOPSIS
    i18n 词条一致性闸门。

.DESCRIPTION
    校验模板里前后端本地化资源的键集合一致，避免"某语言漏配/多配"导致运行时漏译或裸键：
      1. 前端 en.json ⇄ zh-CN.json 键集合完全一致（public/i18n）。
      2. 后端 en.json ⇄ zh-CN.json 键集合完全一致（Api/Resources 的 texts 段）。
      3. 两侧文件均为合法 JSON、且后端声明的 culture 与文件名一致。

    同时校验错误码定义的格式、唯一性与资源键，防止模块增长后发生前缀冲突或漏译。
    退出码非 0 表示存在不一致，供 CI 阻断。
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$ErrorActionPreference = "Stop"
$problems = New-Object System.Collections.Generic.List[string]

function Get-JsonFlatKeys([object]$Node, [string]$Prefix, [System.Collections.Generic.List[string]]$Acc) {
    foreach ($prop in $Node.PSObject.Properties) {
        $path = if ($Prefix) { "$Prefix.$($prop.Name)" } else { $prop.Name }
        if ($prop.Value -is [pscustomobject]) {
            Get-JsonFlatKeys $prop.Value $path $Acc
        }
        else {
            $Acc.Add($path)
        }
    }
}

function Compare-KeySets([string]$Label, [string]$EnPath, [string]$ZhPath, [scriptblock]$Selector) {
    if (-not (Test-Path $EnPath)) { $problems.Add("$Label：缺少 $EnPath"); return }
    if (-not (Test-Path $ZhPath)) { $problems.Add("$Label：缺少 $ZhPath"); return }

    try { $enRoot = Get-Content -LiteralPath $EnPath -Raw | ConvertFrom-Json }
    catch { $problems.Add("$Label：en.json 不是合法 JSON — $($_.Exception.Message)"); return }
    try { $zhRoot = Get-Content -LiteralPath $ZhPath -Raw | ConvertFrom-Json }
    catch { $problems.Add("$Label：zh-CN.json 不是合法 JSON — $($_.Exception.Message)"); return }

    $enNode = & $Selector $enRoot
    $zhNode = & $Selector $zhRoot

    $enKeys = New-Object System.Collections.Generic.List[string]
    $zhKeys = New-Object System.Collections.Generic.List[string]
    Get-JsonFlatKeys $enNode "" $enKeys
    Get-JsonFlatKeys $zhNode "" $zhKeys

    $enSet = [System.Collections.Generic.HashSet[string]]::new([string[]]$enKeys)
    $zhSet = [System.Collections.Generic.HashSet[string]]::new([string[]]$zhKeys)

    $missingInZh = $enKeys | Where-Object { -not $zhSet.Contains($_) } | Sort-Object
    $missingInEn = $zhKeys | Where-Object { -not $enSet.Contains($_) } | Sort-Object

    if ($missingInZh) { $problems.Add("$Label：zh-CN 缺少 $($missingInZh.Count) 个键：$([string]::Join(', ', $missingInZh))") }
    if ($missingInEn) { $problems.Add("$Label：en 缺少 $($missingInEn.Count) 个键：$([string]::Join(', ', $missingInEn))") }

    if (-not $missingInZh -and -not $missingInEn) {
        Write-Host "  OK  $Label：$($enKeys.Count) 个键，en/zh 一致。" -ForegroundColor Green
    }
}

# 取占位符集合（{Name} / {0} 形式），返回排序去重的名字集合字符串用于比较
function Get-Placeholders([string]$text) {
    $set = [System.Collections.Generic.SortedSet[string]]::new()
    foreach ($m in [regex]::Matches($text, '\{([A-Za-z0-9_]+)\}')) { [void]$set.Add($m.Groups[1].Value) }
    return [string]::Join(',', $set)
}

# 校验 en/zh 同一键的占位符集合一致（如 {Email} 不能只在一种语言出现）
function Compare-Placeholders([string]$Label, [string]$EnPath, [string]$ZhPath, [scriptblock]$Selector) {
    if (-not (Test-Path $EnPath) -or -not (Test-Path $ZhPath)) { return }
    $en = & $Selector (Get-Content -LiteralPath $EnPath -Raw | ConvertFrom-Json)
    $zh = & $Selector (Get-Content -LiteralPath $ZhPath -Raw | ConvertFrom-Json)
    $enKeys = New-Object System.Collections.Generic.List[string]
    Get-JsonFlatKeys $en "" $enKeys
    $mismatch = 0
    foreach ($k in $enKeys) {
        # 仅对同时存在于两侧的叶子键比对（键集合一致性已由 Compare-KeySets 保证）
        $enVal = $en; $zhVal = $zh; $ok = $true
        foreach ($seg in $k.Split('.')) {
            if ($null -ne $enVal -and $enVal.PSObject.Properties[$seg]) { $enVal = $enVal.$seg } else { $ok = $false; break }
            if ($null -ne $zhVal -and $zhVal.PSObject.Properties[$seg]) { $zhVal = $zhVal.$seg } else { $ok = $false; break }
        }
        if (-not $ok -or $enVal -isnot [string] -or $zhVal -isnot [string]) { continue }
        $enP = Get-Placeholders $enVal; $zhP = Get-Placeholders $zhVal
        if ($enP -ne $zhP) {
            $script:problems.Add("$Label 占位符不一致：键 '$k' → en{$enP} vs zh{$zhP}")
            $mismatch++
        }
    }
    if ($mismatch -eq 0) { Write-Host "  OK  $Label：占位符集合 en/zh 一致。" -ForegroundColor Green }
}

# 校验代码里静态引用的 key 都存在于资源（避免运行时裸键）
function Test-KeyReferences([string]$Label, [string[]]$SourceGlobs, [regex[]]$Patterns, [string]$EnPath, [scriptblock]$Selector, [string]$FileNameFilter = '*') {
    if (-not (Test-Path $EnPath)) { return }
    $keySet = [System.Collections.Generic.HashSet[string]]::new()
    $flat = New-Object System.Collections.Generic.List[string]
    Get-JsonFlatKeys (& $Selector (Get-Content -LiteralPath $EnPath -Raw | ConvertFrom-Json)) "" $flat
    foreach ($k in $flat) { [void]$keySet.Add($k) }

    $referenced = [System.Collections.Generic.SortedSet[string]]::new()
    foreach ($glob in $SourceGlobs) {
        Get-ChildItem -Path (Join-Path $RepoRoot $glob) -Recurse -File -Include '*.ts', '*.html', '*.cs' -ErrorAction SilentlyContinue | Where-Object {
            # 排除单测文件：describe('a.b') 等点号字符串不是 translate 引用，避免误报。
            $_.Name -notlike '*.spec.ts' -and $_.Name -like $FileNameFilter
        } | ForEach-Object {
            $content = Get-Content -LiteralPath $_.FullName -Raw
            if ([string]::IsNullOrEmpty($content)) { return }
            # 去掉 C# XML 文档注释行：示例代码里的键不是真实引用。
            # 是说明用法，不是真实引用，不该要求资源里存在
            $content = ($content -split "`n" | Where-Object { $_.TrimStart() -notlike '///*' }) -join "`n"
            foreach ($pattern in $Patterns) {
                foreach ($m in $pattern.Matches($content)) { [void]$referenced.Add($m.Groups[1].Value) }
            }
        }
    }
    $missing = @($referenced | Where-Object { -not $keySet.Contains($_) } | Sort-Object)
    if ($missing.Count -gt 0) {
        $script:problems.Add("$Label：$($missing.Count) 个被引用但资源缺失的键：$([string]::Join(', ', $missing))")
    }
    else {
        Write-Host "  OK  $Label：$($referenced.Count) 个静态引用键均存在。" -ForegroundColor Green
    }
}

function Test-TemplateErrorCodeDefinitions([string[]]$SourceRoots) {
    $values = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($root in $SourceRoots) {
        Get-ChildItem -Path (Join-Path $RepoRoot $root) -Recurse -File -Filter '*ErrorCodes.cs' |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
            ForEach-Object {
                $file = $_
                $owner = $file.BaseName -replace 'ErrorCodes$', ''
                $content = Get-Content -LiteralPath $file.FullName -Raw
                foreach ($declaration in [regex]::Matches($content, 'public\s+const\s+string\s+([A-Za-z][A-Za-z0-9]*)\s*=\s*"([^"]+)"')) {
                    $member = $declaration.Groups[1].Value
                    $code = $declaration.Groups[2].Value
                    if ($code -cne "${owner}:$member") {
                        $script:problems.Add("错误码格式或所有者不一致：$($file.FullName) $member = $code，应为 ${owner}:$member")
                    }
                    if (-not $values.Add($code) -or -not $script:allErrorCodes.Add($code)) {
                        $script:problems.Add("重复业务错误码：$code ($($file.FullName))")
                    }
                }
            }
    }
    Write-Host "  模板业务错误码定义已检查：$($values.Count) 个。" -ForegroundColor Green
}

function Test-FrameworkErrorCodeDefinitions([string]$SourceRoot) {
    $count = 0
    Get-ChildItem -Path (Join-Path $RepoRoot $SourceRoot) -Recurse -File -Filter '*ErrorCodes.cs' |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
        ForEach-Object {
            $file = $_
            $content = Get-Content -LiteralPath $file.FullName -Raw
            foreach ($declaration in [regex]::Matches($content, 'public\s+const\s+string\s+[A-Za-z][A-Za-z0-9]*\s*=\s*"([^"]+)"')) {
                $code = $declaration.Groups[1].Value
                $count++
                if ($code -cnotmatch '^[A-Z][A-Za-z0-9]*:[A-Z][A-Za-z0-9]*$') {
                    $script:problems.Add("框架错误码格式不合规：$code ($($file.FullName))")
                }
                if (-not $script:allErrorCodes.Add($code)) {
                    $script:problems.Add("重复业务错误码：$code ($($file.FullName))")
                }
            }
        }
    Write-Host "  框架组件错误码定义已检查：$count 个。" -ForegroundColor Green
}

# 接口入参 DTO 上的校验特性必须显式写 ErrorMessage：不写时用的是 .NET 内置英文消息
# （"The Name field is required."），它不是资源键，本地化查不到，中文界面上照样是英文。
function Test-ValidationMessagesExplicit([string]$Label, [string]$SourceRoot) {
    $attribute = [regex]'\[(Required|StringLength|MaxLength|MinLength|Range|RegularExpression|EmailAddress|Phone|Url|Compare|Length)\b(\([^\]]*\))?\]'
    $bare = New-Object System.Collections.Generic.List[string]
    Get-ChildItem -Path (Join-Path $RepoRoot $SourceRoot) -Recurse -File -Filter '*.cs' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '[\\/]Dtos[\\/]' } |
        ForEach-Object {
            $lines = Get-Content -LiteralPath $_.FullName
            for ($i = 0; $i -lt $lines.Count; $i++) {
                if ($lines[$i].TrimStart().StartsWith('//')) { continue }
                foreach ($m in $attribute.Matches($lines[$i])) {
                    if ($m.Value -notmatch 'ErrorMessage\s*=') {
                        $bare.Add("$([IO.Path]::GetRelativePath($RepoRoot, $_.FullName)):$($i + 1) $($m.Value)")
                    }
                }
            }
        }
    if ($bare.Count -gt 0) {
        $script:problems.Add("$Label：$($bare.Count) 个校验特性未写 ErrorMessage（会落成 .NET 内置英文）：$([string]::Join('; ', $bare))")
    }
    else {
        Write-Host "  OK  $Label：入参 DTO 的校验特性均显式给出消息模板。" -ForegroundColor Green
    }
}

# 代码里不许写死中文展示文案：它绕过本地化，英文界面照样显示中文，关掉本地化的模板变体里也是中文。
# 注释与单测不算；语言切换菜单里的语言本地名（"中文"）是刻意的，登记在白名单里。
function Test-NoHardcodedCjk([string]$Label, [string]$SourceRoot, [string[]]$Extensions, [string[]]$AllowedFiles) {
    $cjk = [regex]'[\u4e00-\u9fff]'
    $found = New-Object System.Collections.Generic.List[string]
    Get-ChildItem -Path (Join-Path $RepoRoot $SourceRoot) -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $Extensions -contains $_.Extension -and $_.Name -notlike '*.spec.ts' -and $_.FullName -notmatch '[\\/](bin|obj|node_modules)[\\/]' } |
        ForEach-Object {
            $relative = [IO.Path]::GetRelativePath($RepoRoot, $_.FullName).Replace('\', '/')
            if ($AllowedFiles -contains $relative) { return }
            $text = Get-Content -LiteralPath $_.FullName -Raw
            if ([string]::IsNullOrEmpty($text)) { return }
            # 块注释按原行数替换成空行，行号才对得上
            $text = [regex]::Replace($text, '/\*[\s\S]*?\*/|<!--[\s\S]*?-->', { param($m) "`n" * ($m.Value.Split("`n").Count - 1) })
            $lines = $text -split "`n"
            for ($i = 0; $i -lt $lines.Count; $i++) {
                $code = [regex]::Replace($lines[$i], '(^|\s)//.*$', '')
                if ($cjk.IsMatch($code)) { $found.Add("${relative}:$($i + 1) $($code.Trim())") }
            }
        }
    if ($found.Count -gt 0) {
        $script:problems.Add("$Label：$($found.Count) 处写死的中文（应进语言资源）：$([string]::Join('; ', $found))")
    }
    else {
        Write-Host "  OK  $Label：源码里没有写死的中文展示文案。" -ForegroundColor Green
    }
}

# 前端 Transloco 插值必须用双大括号 {{name}}；单大括号 {name} 是常见误用（Transloco 不会替换）。
function Test-TranslocoInterpolation([string]$EnPath) {
    if (-not (Test-Path $EnPath)) { return }
    $flat = New-Object System.Collections.Generic.List[string]
    $root = Get-Content -LiteralPath $EnPath -Raw | ConvertFrom-Json
    Get-JsonFlatKeys $root '' $flat
    $bad = New-Object System.Collections.Generic.List[string]
    foreach ($k in $flat) {
        $v = $root; $ok = $true
        foreach ($seg in $k.Split('.')) { if ($v.PSObject.Properties[$seg]) { $v = $v.$seg } else { $ok = $false; break } }
        if (-not $ok -or $v -isnot [string]) { continue }
        # 先移除所有 {{...}}（合法），再看是否还剩单括号 {name}
        $stripped = [regex]::Replace($v, '\{\{[^}]+\}\}', '')
        foreach ($m in [regex]::Matches($stripped, '\{([a-zA-Z][a-zA-Z0-9_]*)\}')) {
            $name = $m.Groups[1].Value
            $bad.Add("$k → 单括号 '{$name}'（Transloco 应用 '{{$name}}'）")
        }
    }
    if ($bad.Count -gt 0) {
        $script:problems.Add("前端 Transloco 插值误用（$($bad.Count) 处）：$([string]::Join('; ', $bad))")
    }
    else {
        Write-Host "  OK  前端 Transloco 插值全部使用双大括号规范。" -ForegroundColor Green
    }
}

Write-Host "== i18n 词条一致性闸门 ==" -ForegroundColor Cyan

# 前端：整个对象即键树
Compare-KeySets `
    -Label "前端(public/i18n)" `
    -EnPath (Join-Path $RepoRoot "template/frontend/public/i18n/en.json") `
    -ZhPath (Join-Path $RepoRoot "template/frontend/public/i18n/zh-CN.json") `
    -Selector { param($r) $r }

# 后端：顶层 culture + texts 两段结构，键在 texts 段下
Compare-KeySets `
    -Label "后端(Api/Resources)" `
    -EnPath (Join-Path $RepoRoot "template/backend/src/CompanyName.ProjectName.Api/Resources/en.json") `
    -ZhPath (Join-Path $RepoRoot "template/backend/src/CompanyName.ProjectName.Api/Resources/zh-CN.json") `
    -Selector { param($r) $r.texts }

# 框架组件的随包译文：自动枚举，不手工列举。
# 这些键是公共契约——宿主按键覆盖组件译文，错误码按键找句子，抛异常那侧按占位符名传参。
# 手工列举时只有 Localization.Core 在内，另外四个组件（多租户、权限、设置、通知）
# 一直没被比对过；新增带资源的组件也不会自动进来。
$frameworkResources = Get-ChildItem -Path (Join-Path $RepoRoot "framework/components") -Recurse -Directory -Filter "Resources" |
    Where-Object { (Test-Path (Join-Path $_.FullName "en.json")) -and (Test-Path (Join-Path $_.FullName "zh-CN.json")) } |
    Sort-Object FullName
if ($frameworkResources.Count -eq 0) {
    $problems.Add("框架组件：一个随包译文目录都没找到，枚举条件多半写错了")
}
foreach ($dir in $frameworkResources) {
    Compare-KeySets `
        -Label "框架($($dir.Parent.Name))" `
        -EnPath (Join-Path $dir.FullName "en.json") `
        -ZhPath (Join-Path $dir.FullName "zh-CN.json") `
        -Selector { param($r) $r.texts }
}

Write-Host ""
Write-Host "-- 占位符一致性 --" -ForegroundColor Cyan
Compare-Placeholders "后端(Api/Resources)" `
    (Join-Path $RepoRoot "template/backend/src/CompanyName.ProjectName.Api/Resources/en.json") `
    (Join-Path $RepoRoot "template/backend/src/CompanyName.ProjectName.Api/Resources/zh-CN.json") { param($r) $r.texts }
Compare-Placeholders "前端(public/i18n)" `
    (Join-Path $RepoRoot "template/frontend/public/i18n/en.json") `
    (Join-Path $RepoRoot "template/frontend/public/i18n/zh-CN.json") { param($r) $r }
foreach ($dir in $frameworkResources) {
    Compare-Placeholders "框架($($dir.Parent.Name))" `
        (Join-Path $dir.FullName "en.json") `
        (Join-Path $dir.FullName "zh-CN.json") { param($r) $r.texts }
}

Write-Host ""
Write-Host "-- 代码引用键存在性（静态可发现部分）--" -ForegroundColor Cyan
$script:allErrorCodes = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
Test-FrameworkErrorCodeDefinitions "framework/components"
# 模板业务码已常量化；从常量定义反查资源，避免把全部抛出点改为常量后误报“0 个引用”。
# 组件自己的码由组件资源负责，不在模板这里重复维护。
Test-TemplateErrorCodeDefinitions @(
    "template/backend/src/CompanyName.ProjectName.Domain",
    "template/backend/src/CompanyName.ProjectName.Application"
)
Test-KeyReferences "后端业务错误码常量" `
    @("template/backend/src/CompanyName.ProjectName.Domain", "template/backend/src/CompanyName.ProjectName.Application") `
    ([regex]'public\s+const\s+string\s+\w+\s*=\s*"([^"]+)"') `
    (Join-Path $RepoRoot "template/backend/src/CompanyName.ProjectName.Api/Resources/en.json") { param($r) $r.texts } '*ErrorCodes.cs'
foreach ($dir in $frameworkResources) {
    $relativeRoot = [IO.Path]::GetRelativePath($RepoRoot, $dir.Parent.FullName)
    if (@(Get-ChildItem -LiteralPath $dir.Parent.FullName -Recurse -File -Filter '*ErrorCodes.cs').Count -eq 0) { continue }
    Test-KeyReferences "框架错误码($($dir.Parent.Name))" `
        @($relativeRoot) `
        ([regex]'public\s+const\s+string\s+\w+\s*=\s*"([^"]+)"') `
        (Join-Path $dir.FullName "en.json") { param($r) $r.texts } '*ErrorCodes.cs'
}
$literalBusinessCodes = @(Get-ChildItem -Path (Join-Path $RepoRoot "template/backend/src") -Recurse -File -Filter "*.cs" |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    Where-Object { [regex]::IsMatch((Get-Content -LiteralPath $_.FullName -Raw), 'new\s+BusinessException\(\s*"[^"]+"') } |
    ForEach-Object { [IO.Path]::GetRelativePath($RepoRoot, $_.FullName) })
if ($literalBusinessCodes.Count -gt 0) {
    $problems.Add("模板 BusinessException 必须引用所属模块的错误码常量：$($literalBusinessCodes -join ', ')")
}
# 后端 DataAnnotations：字段显示名与消息模板都是资源键（DataAnnotationLocalizerProvider 按原文查词条）。
# 缺一条，中文界面上就出现"Role name只能包含字母、数字和下划线"这种半截英文。
Test-KeyReferences "后端 DataAnnotations" `
    @("template/backend/src") `
    @(
        [regex]'Display\(Name\s*=\s*"([^"]+)"',
        [regex]'ErrorMessage\s*=\s*"([^"]+)"'
    ) `
    (Join-Path $RepoRoot "template/backend/src/CompanyName.ProjectName.Api/Resources/en.json") { param($r) $r.texts }
Test-ValidationMessagesExplicit "后端入参 DTO" "template/backend/src"
# 前端 transloco.translate('key') 与 'key' | transloco（点号分段键，排除动态拼接）。
# 必须锚定在 transloco 上下文里：仅凭「带点号的字符串字面量」判定会把权限名等常量表误判成翻译键。
Test-KeyReferences "前端 translate/pipe" `
    @("template/frontend/src") `
    @(
        [regex]"transloco\.translate\(\s*'([a-zA-Z][a-zA-Z0-9_]*(?:\.[a-zA-Z0-9_]+)+)'",
        [regex]"'([a-zA-Z][a-zA-Z0-9_]*(?:\.[a-zA-Z0-9_]+)+)'\s*\|\s*transloco"
    ) `
    (Join-Path $RepoRoot "template/frontend/public/i18n/en.json") { param($r) $r }

Write-Host ""
Write-Host "-- 写死的中文展示文案 --" -ForegroundColor Cyan
Test-NoHardcodedCjk "前端源码" "template/frontend/src" @('.ts', '.html') @(
    # 语言切换菜单：每种语言用它自己的文字显示，不随界面语言变化
    'template/frontend/src/app/core/services/language-service.ts'
)
Test-NoHardcodedCjk "后端源码" "template/backend/src" @('.cs') @()

Write-Host ""
Write-Host "-- Transloco 插值大括号 --" -ForegroundColor Cyan
Test-TranslocoInterpolation (Join-Path $RepoRoot "template/frontend/public/i18n/en.json")

# 宿主资源不复制组件译文：同名键会覆盖组件自带的句子，组件改文案时旧句子被静默钉死
# （docs/framework/development-guide.md「宿主不要复制组件的译文」）。确要改写组件文案的键登记在白名单里并写明原因。
Write-Host ""
Write-Host "-- 宿主不复制组件译文 --" -ForegroundColor Cyan
$hostOverrideAllowed = @(
    # 补充模板自带的迁移入口（ConnectionStrings:MigrationTarget、DbMigrator --apply）：组件不知道宿主用什么迁移工具
    'Tenant:DedicatedDatabaseMissing'
    'Tenant:DedicatedDatabaseNotMigrated'
    'Tenant:DedicatedDatabaseUnreachable'
)
$hostKeys = (Get-Content -Raw -Encoding UTF8 (Join-Path $RepoRoot "template/backend/src/CompanyName.ProjectName.Api/Resources/en.json") |
    ConvertFrom-Json).texts.PSObject.Properties.Name
$copies = 0
foreach ($dir in $frameworkResources) {
    $componentKeys = (Get-Content -Raw -Encoding UTF8 (Join-Path $dir.FullName "en.json") | ConvertFrom-Json).texts.PSObject.Properties.Name
    foreach ($key in ($componentKeys | Where-Object { $hostKeys -ccontains $_ -and $hostOverrideAllowed -cnotcontains $_ })) {
        $problems.Add("宿主资源重复了 $($dir.Parent.Name) 自带的键 ${key}：删除宿主那条，或登记进 hostOverrideAllowed 并写明原因")
        $copies++
    }
}
if ($copies -eq 0) { Write-Host "  OK  宿主资源没有复制组件自带的译文。" -ForegroundColor Green }

if ($problems.Count -gt 0) {
    Write-Host ""
    Write-Host "i18n 键不一致：" -ForegroundColor Red
    foreach ($p in $problems) { Write-Host "  - $p" -ForegroundColor Red }
    exit 1
}

Write-Host ""
Write-Host "全部 i18n 资源键一致。" -ForegroundColor Green
exit 0
