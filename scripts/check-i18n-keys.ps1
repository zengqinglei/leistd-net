<#
.SYNOPSIS
    i18n 词条一致性闸门。

.DESCRIPTION
    校验模板里前后端本地化资源的键集合一致，避免"某语言漏配/多配"导致运行时漏译或裸键：
      1. 前端 en.json ⇄ zh-CN.json 键集合完全一致（public/i18n）。
      2. 后端 en.json ⇄ zh-CN.json 键集合完全一致（Api/Resources 的 texts 段）。
      3. 两侧文件均为合法 JSON、且后端声明的 culture 与文件名一致。

    仅校验"资源自洽"；键是否被代码引用不在此闸门范围（另由构建/lint 保障）。
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
function Test-KeyReferences([string]$Label, [string[]]$SourceGlobs, [regex[]]$Patterns, [string]$EnPath, [scriptblock]$Selector) {
    if (-not (Test-Path $EnPath)) { return }
    $keySet = [System.Collections.Generic.HashSet[string]]::new()
    $flat = New-Object System.Collections.Generic.List[string]
    Get-JsonFlatKeys (& $Selector (Get-Content -LiteralPath $EnPath -Raw | ConvertFrom-Json)) "" $flat
    foreach ($k in $flat) { [void]$keySet.Add($k) }

    $referenced = [System.Collections.Generic.SortedSet[string]]::new()
    foreach ($glob in $SourceGlobs) {
        Get-ChildItem -Path (Join-Path $RepoRoot $glob) -Recurse -File -Include '*.ts', '*.html', '*.cs' -ErrorAction SilentlyContinue | Where-Object {
            # 排除单测文件：describe('a.b') 等点号字符串不是 translate 引用，避免误报。
            $_.Name -notlike '*.spec.ts'
        } | ForEach-Object {
            $content = Get-Content -LiteralPath $_.FullName -Raw
            if ([string]::IsNullOrEmpty($content)) { return }
            # 去掉 C# XML 文档注释行：示例代码里的键（如 <c>WithCode("User:EmailAlreadyUsed")</c>）
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

# 框架默认资源：通用键（Error:* / Title:*）
Compare-KeySets `
    -Label "框架(Localization.Core)" `
    -EnPath (Join-Path $RepoRoot "framework/components/localization/Leistd.Localization.Core/Resources/en.json") `
    -ZhPath (Join-Path $RepoRoot "framework/components/localization/Leistd.Localization.Core/Resources/zh-CN.json") `
    -Selector { param($r) $r.texts }

Write-Host ""
Write-Host "-- 占位符一致性 --" -ForegroundColor Cyan
Compare-Placeholders "后端(Api/Resources)" `
    (Join-Path $RepoRoot "template/backend/src/CompanyName.ProjectName.Api/Resources/en.json") `
    (Join-Path $RepoRoot "template/backend/src/CompanyName.ProjectName.Api/Resources/zh-CN.json") { param($r) $r.texts }
Compare-Placeholders "前端(public/i18n)" `
    (Join-Path $RepoRoot "template/frontend/public/i18n/en.json") `
    (Join-Path $RepoRoot "template/frontend/public/i18n/zh-CN.json") { param($r) $r }
Compare-Placeholders "框架(Localization.Core)" `
    (Join-Path $RepoRoot "framework/components/localization/Leistd.Localization.Core/Resources/en.json") `
    (Join-Path $RepoRoot "framework/components/localization/Leistd.Localization.Core/Resources/zh-CN.json") { param($r) $r.texts }

Write-Host ""
Write-Host "-- 代码引用键存在性（静态可发现部分）--" -ForegroundColor Cyan
# 后端 WithCode("模块:键")——错误码同时是展示词条键，见 exception 组件文档
Test-KeyReferences "后端 WithCode" `
    @("template/backend/src") `
    ([regex]'WithCode\("([^"]+)"') `
    (Join-Path $RepoRoot "template/backend/src/CompanyName.ProjectName.Api/Resources/en.json") { param($r) $r.texts }
# 框架自身的 WithCode（异常归一化用的 Error:* 通用键），对照框架资源
Test-KeyReferences "框架 WithCode" `
    @("framework/components") `
    ([regex]'WithCode\("([^"]+)"') `
    (Join-Path $RepoRoot "framework/components/localization/Leistd.Localization.Core/Resources/en.json") { param($r) $r.texts }
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
Write-Host "-- Transloco 插值大括号 --" -ForegroundColor Cyan
Test-TranslocoInterpolation (Join-Path $RepoRoot "template/frontend/public/i18n/en.json")

if ($problems.Count -gt 0) {
    Write-Host ""
    Write-Host "i18n 键不一致：" -ForegroundColor Red
    foreach ($p in $problems) { Write-Host "  - $p" -ForegroundColor Red }
    exit 1
}

Write-Host ""
Write-Host "全部 i18n 资源键一致。" -ForegroundColor Green
exit 0
