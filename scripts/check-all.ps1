#!/usr/bin/env pwsh

<#
.SYNOPSIS
    跑完本仓库的全部静态闸门，并汇总结果。

.DESCRIPTION
    **这里是闸门清单的唯一权威来源。** 新增闸门只需加进下面的 $gates，CI 与文档都不必再改——
    以前闸门散在三处（ci.yml 的独立步骤、test-template-matrix.ps1 内部、两份 development-guide
    各列一半），"本仓库有哪些检查、怎么一次跑完"没有任何一个地方能回答。

    只收静态闸门。需要构建产物或跑起来才能验的不在这里，各有自己的入口：
      - dotnet build / test                             框架源码
      - framework/build/pack-local-feed.ps1
        + framework/build/test-package-consumption.ps1  NuGet 隔离消费
      - scripts/test-template-matrix.ps1                场景生成 + 构建 + 前后端测试（-Tier pr 为 PR 档子集）
      - scripts/test-template-postgresql-e2e.ps1        真实 PostgreSQL 端到端
      - scripts/test-template-oidc-e2e.ps1              真实 OIDC 跨服务 HTTP 端到端

    模板源码的 symbols / using-guards / async-boundaries 由这里完整执行。
    独立矩阵入口默认也在生成前预检；同候选 CI 显式跳过重复扫描，必过汇总核对本作业成功。

.PARAMETER List
    只打印闸门清单，不执行。

.PARAMETER StopOnFirstFailure
    首个失败即停。默认跑完全部再汇总——一次看清所有问题，比修一个跑一轮快。
#>

param(
    [switch]$List,
    [switch]$StopOnFirstFailure
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

# Python 闸门的解释器：CI/Unix 常为 python3，Windows 通常只有 python（约定见 docs/framework/development-guide.md §9）
$pythonCmd = $null
foreach ($candidate in @("python3", "python")) {
    $found = Get-Command $candidate -ErrorAction SilentlyContinue
    if ($found -and ((& $found.Name --version 2>&1) -match 'Python 3\.')) { $pythonCmd = $found.Name; break }
}
if (-not $pythonCmd) { throw "未找到 Python 3 解释器（python3/python），无法运行 Python 静态闸门。" }

$gates = @(
    @{ Name = "组件文档与索引一致";        Cmd = "pwsh"; Args = @("framework/build/check-docs-sync.ps1") }
    @{ Name = "文档 API 自检与引用不漂移";       Cmd = "pwsh"; Args = @("framework/build/check-docs-api-drift.ps1") }
    @{ Name = "Skill 与文档引用";          Cmd = "pwsh"; Args = @("scripts/validate-skills.ps1") }
    # 自检先跑：退役符号规则本身失效时，紧随其后的那次"通过"没有意义
    @{ Name = "退役符号规则自检";          Cmd = "pwsh"; Args = @("scripts/check-retired-terms.ps1", "-SelfTest") }
    @{ Name = "无已删除符号/旧表述残留";   Cmd = "pwsh"; Args = @("scripts/check-retired-terms.ps1") }
    # 项目判据随模板分发（template/scripts/check-i18n.py），本仓判据在 check-i18n-repo.py，入口统一跑两段
    @{ Name = "i18n 规则自检";             Cmd = "pwsh"; Args = @("scripts/check-i18n-keys.ps1", "-SelfTest") }
    @{ Name = "i18n 词条键一致";           Cmd = "pwsh"; Args = @("scripts/check-i18n-keys.ps1") }
    # 这道闸门随模板分发（template/scripts/），本仓直接跑那一份：
    # 实现只有一处，生成项目拿到的与这里跑的是同一个判据，不会各自漂移。
    @{ Name = "动作码词条规则自检";        Cmd = $pythonCmd; Args = @("template/scripts/check-operation-action-i18n.py", "--self-test") }
    @{ Name = "动作码有句子模板";          Cmd = $pythonCmd; Args = @("template/scripts/check-operation-action-i18n.py") }
    @{ Name = "模板条件符号";              Cmd = "pwsh"; Args = @("scripts/check-template-symbols.ps1") }
    @{ Name = "条件块规则自检";            Cmd = $pythonCmd; Args = @("scripts/check-template-conditional-blocks.py", "--self-test") }
    @{ Name = "模板条件块结构";            Cmd = $pythonCmd; Args = @("scripts/check-template-conditional-blocks.py") }
    # PR 档只跑场景子集；子集能否代表全集，由逐行求值判定，不靠人记
    @{ Name = "场景覆盖规则自检";          Cmd = $pythonCmd; Args = @("scripts/check-template-scenario-coverage.py", "--self-test") }
    @{ Name = "PR 档场景覆盖全部条件行";   Cmd = $pythonCmd; Args = @("scripts/check-template-scenario-coverage.py") }
    @{ Name = "模板 using/import 守卫";    Cmd = $pythonCmd; Args = @("scripts/check-using-guards.py") }
    @{ Name = "动态连接路径异步边界";      Cmd = $pythonCmd; Args = @("scripts/check-async-boundaries.py") }
    # 自检先跑：豁免机制本身失效时，紧随其后的那次"通过"没有意义
    @{ Name = "时间源规则自检";            Cmd = $pythonCmd; Args = @("scripts/check-clock-access.py", "--self-test") }
    @{ Name = "时间源可替换";              Cmd = $pythonCmd; Args = @("scripts/check-clock-access.py") }
    @{ Name = "DbContext 访问口径";        Cmd = $pythonCmd; Args = @("scripts/check-dbcontext-access.py") }
    @{ Name = "csproj 约定";               Cmd = $pythonCmd; Args = @("scripts/check-csproj-conventions.py") }
    # 日志调用点改回记原文不会让任何用例变红（调用点在要连 SMTP 的方法里），判据只能放在这里
    @{ Name = "联系方式日志规则自检";      Cmd = $pythonCmd; Args = @("scripts/check-contact-info-logging.py", "--self-test") }
    @{ Name = "联系方式不进日志";          Cmd = $pythonCmd; Args = @("scripts/check-contact-info-logging.py") }
    # 覆盖率报告发现不了"程序集从未被任何测试加载"——那种包根本不出现在报告里
    @{ Name = "测试布局规则自检";          Cmd = $pythonCmd; Args = @("scripts/check-test-layout.py", "--self-test") }
    @{ Name = "测试布局与家族对应";        Cmd = $pythonCmd; Args = @("scripts/check-test-layout.py") }
    # 测试报告、CI 日志与 IDE 测试树里的名字统一用英文；中文只在注释与测试数据里
    @{ Name = "测试名规则自检";            Cmd = $pythonCmd; Args = @("scripts/check-test-names.py", "--self-test") }
    @{ Name = "测试名用英文";              Cmd = $pythonCmd; Args = @("scripts/check-test-names.py") }
    @{ Name = "XML 注释形态规则自检";      Cmd = $pythonCmd; Args = @("scripts/check-doc-comment-shape.py", "--self-test") }
    @{ Name = "XML 注释形态";              Cmd = $pythonCmd; Args = @("scripts/check-doc-comment-shape.py") }
    @{ Name = "组件文档骨架规则自检";      Cmd = $pythonCmd; Args = @("scripts/check-docs-skeleton.py", "--self-test") }
    @{ Name = "组件文档骨架";              Cmd = $pythonCmd; Args = @("scripts/check-docs-skeleton.py") }
    # 矩阵在生成产物上做权威检查（含条件裁剪删掉被链接章节）；源码这道给模板文档改动即时反馈。
    # 源码含全部条件分支的标题（并集），条件标记不会造成误报，只可能漏掉裁剪类问题。
    @{ Name = "章节锚点规则自检";          Cmd = $pythonCmd; Args = @("scripts/check-markdown-anchors.py", "--self-test") }
    @{ Name = "模板文档章节锚点";          Cmd = $pythonCmd; Args = @("scripts/check-markdown-anchors.py", "template") }
)

if ($List) {
    Write-Host "本仓库静态闸门（$($gates.Count) 道）：" -ForegroundColor Cyan
    foreach ($g in $gates) {
        Write-Host ("  {0,-28} {1} {2}" -f $g.Name, $g.Cmd, ($g.Args -join ' '))
    }
    exit 0
}

$results = @()
foreach ($g in $gates) {
    Write-Host "▶ $($g.Name)" -ForegroundColor Cyan
    $started = Get-Date
    # 闸门自己的输出直通终端：失败时诊断信息就在眼前，不必再单独复跑一次
    & $g.Cmd @($g.Args | ForEach-Object { if ($_ -like '-*') { $_ } else { Join-Path $repoRoot $_ } })
    $ok = $LASTEXITCODE -eq 0
    $results += [PSCustomObject]@{
        Gate    = $g.Name
        Passed  = $ok
        Seconds = [math]::Round(((Get-Date) - $started).TotalSeconds, 1)
    }
    if (-not $ok -and $StopOnFirstFailure) { break }
}

Write-Host ""
Write-Host "═══ 汇总 ═══" -ForegroundColor Cyan
foreach ($r in $results) {
    $mark = if ($r.Passed) { "✅" } else { "❌" }
    $color = if ($r.Passed) { "Green" } else { "Red" }
    Write-Host ("  {0} {1,-28} {2,5}s" -f $mark, $r.Gate, $r.Seconds) -ForegroundColor $color
}

$failed = @($results | Where-Object { -not $_.Passed })
$skipped = $gates.Count - $results.Count
if ($skipped -gt 0) { Write-Host "  （因 -StopOnFirstFailure 未执行 $skipped 道）" -ForegroundColor Yellow }

if ($failed.Count -gt 0) {
    Write-Host ""
    throw "$($failed.Count)/$($gates.Count) 道闸门失败：$(($failed.Gate) -join '、')"
}

Write-Host ""
Write-Host "全部 $($gates.Count) 道静态闸门通过。" -ForegroundColor Green
