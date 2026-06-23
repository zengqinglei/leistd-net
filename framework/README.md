# Leistd Framework

Leistd 是面向 **.NET 10** 的 DDD 应用框架基座，从 `leistd-net` 模板中抽取，独立版本化、以 NuGet 包形式发布，供模板生成的业务项目（及其它项目）复用。

参考 [Volo.ABP](https://abp.io) 的工程化模式：单点版本、中央包管理（CPM）、PDB 内嵌 + Source Link 源码调试。

> 📖 **完整文档从这里开始 → [docs/README.md](docs/README.md)**（组件用法、开发规范、版本发布）。本文件仅作仓库门面与快速上手。

## 快速上手

```bash
# 编译整个框架
dotnet build framework/Leistd.Framework.slnx -c Release

# 打包（输出 framework/artifacts/，每个项目产出内嵌 PDB 的 .nupkg）
pwsh framework/build/pack.ps1

# 推送到 NuGet（API Key 从环境变量 NUGET_API_KEY 读取）
$env:NUGET_API_KEY = "<key>"
pwsh framework/build/push.ps1
```

## 目录结构

```
framework/
├── common.props              # 共享构建属性 + NuGet 元数据 + Source Link（单点 <Version>）
├── Directory.Build.props     # 自动导入 common.props 到所有框架项目
├── Directory.Packages.props  # CPM：第三方包版本集中声明
├── Leistd.Framework.slnx     # 框架解决方案（26 个项目）
├── NuGet.md                  # 各包的 NuGet 自述
├── build/                    # pack.ps1 / push.ps1 打包发布脚本
├── docs/                     # 📖 文档体系（入口见 docs/README.md）
├── components/               # 共享组件（aop / core / lock / security / tracing …）
└── ddd-struct/               # DDD 四层基础类型（Domain / Application(.Contracts) / Infrastructure）
```

## 关键概念（详见文档）

| 主题 | 一句话 | 详细 |
| --- | --- | --- |
| 组件用法 | 各组件的场景、安装、配置、API | [组件总览](docs/components/README.md) |
| 版本机制 | 版本单点维护于 `common.props` 的 `<Version>`，全包同步 | [版本与发布](docs/versioning.md) |
| 消费 / 调试切换 | 模板默认 NuGet 引用，`LeistdUseLocalFramework=true` 切本地源码调试 | [文档首页](docs/README.md) |
| 新增组件 | 命名、csproj、CPM、加入 slnx 的规范 | [开发规范](docs/development-guide.md) |
