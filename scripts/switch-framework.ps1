#!/usr/bin/env pwsh
<#
.SYNOPSIS
    在「NuGet 引用」与「本地框架源码引用」之间切换后端项目的框架依赖。

.DESCRIPTION
    切换由 MSBuild 属性 LeistdUseLocalFramework 驱动（见 template/backend/Directory.Build.props），
    本脚本只负责设置/清除环境变量并给出提示，不改动任何文件（git 保持干净）。

    - local : 设置 LeistdUseLocalFramework=true，src 走 ProjectReference 指向 ../framework 源码，可断点调试。
    - nuget : 清除该变量（恢复默认 false），src 走 PackageReference 从 NuGet 还原 Leistd.* 包。
    - status: 显示当前取值。

    注意：环境变量仅对「当前 PowerShell 会话及其后续启动的进程」生效。
          IDE（VS / Rider）需在设置该变量后的会话中启动，或改用持久化（-Persist）。

.PARAMETER Mode
    local | nuget | status

.PARAMETER Persist
    持久化到用户级环境变量（User scope），对新开的进程/IDE 永久生效，直到再次切换。

.EXAMPLE
    pwsh scripts/switch-framework.ps1 local
.EXAMPLE
    pwsh scripts/switch-framework.ps1 nuget -Persist
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet("local", "nuget", "status")]
    [string]$Mode = "status",

    [switch]$Persist
)

$VarName = "LeistdUseLocalFramework"

function Show-Status {
    $session = [Environment]::GetEnvironmentVariable($VarName, "Process")
    $user = [Environment]::GetEnvironmentVariable($VarName, "User")
    Write-Host "当前 $VarName：" -ForegroundColor Cyan
    Write-Host "  会话(Process): $(if ($session) { $session } else { '<未设置=默认 false, NuGet 模式>' })"
    Write-Host "  用户(User)   : $(if ($user) { $user } else { '<未设置>' })"
}

switch ($Mode) {
    "local" {
        $env:LeistdUseLocalFramework = "true"
        if ($Persist) { [Environment]::SetEnvironmentVariable($VarName, "true", "User") }
        Write-Host "✓ 已切换到【本地框架源码】模式 (ProjectReference)" -ForegroundColor Green
        Write-Host "  在本会话执行 dotnet build/IDE 即可断点步进 framework 源码。"
        if (-not $Persist) { Write-Host "  仅当前会话生效；如需对 IDE 永久生效请加 -Persist。" -ForegroundColor DarkYellow }
    }
    "nuget" {
        $env:LeistdUseLocalFramework = "false"
        if ($Persist) { [Environment]::SetEnvironmentVariable($VarName, $null, "User") }
        Write-Host "✓ 已切换到【NuGet 包】模式 (PackageReference)" -ForegroundColor Green
        Write-Host "  src 将从 NuGet 源还原 Leistd.* 包。"
    }
    "status" { Show-Status }
}
