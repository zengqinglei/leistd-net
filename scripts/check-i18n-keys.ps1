<#
.SYNOPSIS
    i18n 词条一致性闸门。

.DESCRIPTION
    校验模板里前后端本地化资源的键集合一致，避免"某语言漏配/多配"导致运行时漏译或裸键：
      1. 前端 en.json ⇄ zh-CN.json 键集合完全一致（public/i18n）。
      2. 后端 en.json ⇄ zh-CN.json 键集合完全一致（Api/Resources 的 texts 段）。
      3. 两侧文件均为合法 JSON、且后端声明的 culture 与文件名一致。
      4. 前端表单用到的错误类型在 validation 段都有句子，不含本地化形态的校验提示英文表与之一致。

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

# 不含本地化的形态里，组件模板的 t('key') 解析到组件自带的英文表（ENGLISH）。
# 表漏一个键，界面就显示裸键名；值与 en.json 不一致，两种形态的英文界面就各说各话——两者都不会编译失败。
function Test-EnglishTables([string]$SourceRoot, [string]$EnPath) {
    if (-not (Test-Path $EnPath)) { return }
    $root = Get-Content -LiteralPath $EnPath -Raw | ConvertFrom-Json
    $flat = New-Object System.Collections.Generic.List[string]
    Get-JsonFlatKeys $root '' $flat
    $en = @{}
    foreach ($k in $flat) {
        $v = $root
        foreach ($seg in $k.Split('.')) { $v = $v.$seg }
        $en[$k] = [string]$v
    }
    # ENGLISH 是组件自带的表；ENGLISH_VALIDATION 是 english-text.ts 并入每张表的校验提示
    $tablePattern = [regex]'(?s)const ENGLISH(_VALIDATION)?: Record<string, string> = \{(.*?)\n\};'
    $entryPattern = [regex]"'([\w.]+)':\s*('(?:[^'\\]|\\.)*'|""(?:[^""\\]|\\.)*"")\s*,"
    $refPattern = [regex]"\bt\(\s*'([a-zA-Z][a-zA-Z0-9_]*(?:\.[a-zA-Z0-9_]+)+)'"
    $bad = New-Object System.Collections.Generic.List[string]
    $tables = 0
    $validationTableFound = $false
    Get-ChildItem -Path (Join-Path $RepoRoot $SourceRoot) -Recurse -File -Filter '*.ts' |
        Where-Object { $_.Name -notlike '*.spec.ts' } |
        ForEach-Object {
            $text = Get-Content -LiteralPath $_.FullName -Raw
            $match = $tablePattern.Match($text)
            if (-not $match.Success) { return }
            $tables++
            $relative = [IO.Path]::GetRelativePath($RepoRoot, $_.FullName).Replace('\', '/')
            $table = @{}
            foreach ($entry in $entryPattern.Matches($match.Groups[2].Value)) {
                $literal = $entry.Groups[2].Value
                $value = [regex]::Replace($literal.Substring(1, $literal.Length - 2), '\\(.)', '$1')
                $key = $entry.Groups[1].Value
                $table[$key] = $value
                if (-not $en.ContainsKey($key)) { $bad.Add("${relative}：英文表的键 '$key' 不在 en.json 里") }
                elseif ($en[$key] -cne $value) { $bad.Add("${relative}：英文表 '$key' 与 en.json 不一致") }
            }
            # 校验提示在模板里按错误类型拼键（t('validation.' + error.kind)），静态引用查不到漏项：
            # 这张表必须覆盖 en.json 的整个 validation 段
            if ($match.Groups[1].Success) {
                $validationTableFound = $true
                foreach ($key in ($en.Keys | Where-Object { $_ -like 'validation.*' } | Sort-Object)) {
                    if (-not $table.ContainsKey($key)) { $bad.Add("${relative}：校验提示表缺 en.json 的 '$key'") }
                }
            }
            $html = [IO.Path]::ChangeExtension($_.FullName, '.html')
            $usage = $text.Remove($match.Index, $match.Length)
            if (Test-Path -LiteralPath $html) { $usage += Get-Content -LiteralPath $html -Raw }
            foreach ($ref in $refPattern.Matches($usage)) {
                $key = $ref.Groups[1].Value
                if (-not $table.ContainsKey($key)) { $bad.Add("${relative}：t('$key') 不在英文表里") }
            }
        }
    if (-not $validationTableFound) { $bad.Add("没找到校验提示英文表 ENGLISH_VALIDATION（shared/utils/english-text.ts）") }
    if ($bad.Count -gt 0) {
        $script:problems.Add("组件英文表（$($bad.Count) 处）：$([string]::Join('; ', ($bad | Sort-Object -Unique)))")
    }
    else {
        Write-Host "  OK  组件英文表：$tables 张表覆盖各自模板的 t() 引用，值与 en.json 一致。" -ForegroundColor Green
    }
}

# 校验提示按错误类型取词条（模板里 t('validation.' + error.kind)），键是拼出来的，静态引用查不到。
# 表单里出现的错误类型——自定义的 kind 字面量与所用的内置校验器——都必须在 en.json 的 validation 段有句子。
function Test-ValidationKinds([string]$SourceRoot, [string]$EnPath) {
    if (-not (Test-Path $EnPath)) { return }
    $validation = (Get-Content -LiteralPath $EnPath -Raw | ConvertFrom-Json).validation
    $known = if ($validation) { @($validation.PSObject.Properties.Name) } else { @() }
    $builtIns = @{ required = 'required'; minLength = 'minLength'; maxLength = 'maxLength'; email = 'email'; min = 'min'; max = 'max' }
    $bad = New-Object System.Collections.Generic.List[string]
    $kinds = [System.Collections.Generic.SortedSet[string]]::new()
    Get-ChildItem -Path (Join-Path $RepoRoot $SourceRoot) -Recurse -File -Filter '*.ts' |
        Where-Object { $_.Name -notlike '*.spec.ts' } |
        ForEach-Object {
            $text = Get-Content -LiteralPath $_.FullName -Raw
            $import = [regex]::Match($text, "import \{([^}]*)\} from '@angular/forms/signals'")
            if (-not $import.Success) { return }
            $relative = [IO.Path]::GetRelativePath($RepoRoot, $_.FullName).Replace('\', '/')
            foreach ($m in [regex]::Matches($text, "\bkind:\s*'(\w+)'")) {
                [void]$kinds.Add($m.Groups[1].Value)
                if ($known -cnotcontains $m.Groups[1].Value) { $bad.Add("${relative}：错误类型 '$($m.Groups[1].Value)' 在 en.json 的 validation 段没有句子") }
            }
            # pattern 的默认错误类型只说"格式不对"，各处都给了字段自己的类型（error 选项）；漏给的才要 validation.pattern
            foreach ($call in [regex]::Matches($text, '(?s)\bpattern\((.*?)\);')) {
                if ($call.Groups[1].Value -notmatch '\berror:') {
                    [void]$kinds.Add('pattern')
                    if ($known -cnotcontains 'pattern') { $bad.Add("${relative}：pattern 校验器没给 error 选项，错误类型 'pattern' 在 en.json 的 validation 段没有句子") }
                }
            }
            foreach ($name in ($import.Groups[1].Value -split ',')) {
                $imported = ($name.Trim() -split '\s+as\s+')[0].Trim()
                if ($builtIns.ContainsKey($imported)) {
                    [void]$kinds.Add($builtIns[$imported])
                    if ($known -cnotcontains $builtIns[$imported]) { $bad.Add("${relative}：内置校验器 $imported 的错误类型在 en.json 的 validation 段没有句子") }
                }
            }
        }
    if ($bad.Count -gt 0) {
        $script:problems.Add("校验提示（$($bad.Count) 处）：$([string]::Join('; ', ($bad | Sort-Object -Unique)))")
    }
    else {
        Write-Host "  OK  校验提示：表单用到的 $($kinds.Count) 种错误类型都有词条。" -ForegroundColor Green
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
# 前端 transloco.translate('key')、模板结构指令给出的 t('key')、translateSignal('key') 与 'key' | transloco（点号分段键，排除动态拼接）。
# 必须锚定在 transloco 上下文里：仅凭「带点号的字符串字面量」判定会把权限名等常量表误判成翻译键。
Test-KeyReferences "前端 translate/t/pipe" `
    @("template/frontend/src") `
    @(
        [regex]"transloco\.translate\(\s*'([a-zA-Z][a-zA-Z0-9_]*(?:\.[a-zA-Z0-9_]+)+)'",
        [regex]"\bt\(\s*'([a-zA-Z][a-zA-Z0-9_]*(?:\.[a-zA-Z0-9_]+)+)'",
        [regex]"\btranslateSignal\(\s*'([a-zA-Z][a-zA-Z0-9_]*(?:\.[a-zA-Z0-9_]+)+)'",
        [regex]"'([a-zA-Z][a-zA-Z0-9_]*(?:\.[a-zA-Z0-9_]+)+)'\s*\|\s*transloco"
    ) `
    (Join-Path $RepoRoot "template/frontend/public/i18n/en.json") { param($r) $r }

# translateObjectSignal('前缀') 取的是一整段词条：前缀下一个键都没有时它恒为空对象，界面只剩空白或回落值。
$frontendFlat = New-Object System.Collections.Generic.List[string]
Get-JsonFlatKeys (Get-Content -LiteralPath (Join-Path $RepoRoot "template/frontend/public/i18n/en.json") -Raw | ConvertFrom-Json) "" $frontendFlat
$objectPrefixes = [System.Collections.Generic.SortedSet[string]]::new()
Get-ChildItem -Path (Join-Path $RepoRoot "template/frontend/src") -Recurse -File -Filter '*.ts' | Where-Object { $_.Name -notlike '*.spec.ts' } | ForEach-Object {
    foreach ($m in [regex]::Matches((Get-Content -LiteralPath $_.FullName -Raw), "\btranslateObjectSignal\(\s*'([a-zA-Z][\w.]*)'")) { [void]$objectPrefixes.Add($m.Groups[1].Value) }
}
$emptyPrefixes = @($objectPrefixes | Where-Object { $prefix = "$_."; -not ($frontendFlat | Where-Object { $_.StartsWith($prefix) } | Select-Object -First 1) })
if ($emptyPrefixes.Count -gt 0) {
    $problems.Add("前端 translateObjectSignal：$($emptyPrefixes.Count) 个前缀下没有词条：$([string]::Join(', ', $emptyPrefixes))")
}
else {
    Write-Host "  OK  前端 translateObjectSignal：$($objectPrefixes.Count) 个前缀下都有词条。" -ForegroundColor Green
}

Write-Host ""
Write-Host "-- 写死的中文展示文案 --" -ForegroundColor Cyan
Test-NoHardcodedCjk "前端源码" "template/frontend/src" @('.ts', '.html') @(
    # 语言切换菜单：每种语言用它自己的文字显示，不随界面语言变化
    'template/frontend/src/app/core/services/language-service.ts'
)
Test-NoHardcodedCjk "后端源码" "template/backend/src" @('.cs') @()

Write-Host ""
Write-Host "-- 不含本地化形态的组件英文表 --" -ForegroundColor Cyan
Test-EnglishTables "template/frontend/src" (Join-Path $RepoRoot "template/frontend/public/i18n/en.json")

Write-Host ""
Write-Host "-- 表单校验提示 --" -ForegroundColor Cyan
Test-ValidationKinds "template/frontend/src" (Join-Path $RepoRoot "template/frontend/public/i18n/en.json")

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
