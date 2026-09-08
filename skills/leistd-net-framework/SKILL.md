---
name: leistd-net-framework
description: 在下游 .NET 项目中使用、配置、排查或解释 Leistd NuGet 组件与 DDD 基座时使用，例如查询 AddXxx 注册、Options、权限、审计、通知、实时、仓储或应用服务 API。应通过已安装包内文档和 XML 核对当前版本；不用于维护 leistd-net 框架源码或回答无关的通用工程问题。
---

# 使用 Leistd .NET Framework

不要根据记忆猜测 `Leistd.*` API。项目实际安装版本的包内文档、XML 和程序集是权威来源；Skill 只定义通用消费流程，不维护组件 API 映射。

先读家族文档确认安装、注册和主路径，再用 XML 核对签名与调用契约；不要求每个成员都有示例。

## 建立项目基线

1. 确认宿主类型、`TargetFramework`、包管理方式、组合根和项目规范。
2. 检查项目是否使用 Central Package Management、统一的 `LeistdFrameworkVersion` 或其他版本属性。
3. 从项目文件和 `obj/project.assets.json` 确认目标包是否已经安装。

用户要求实际接入且目标包未安装时，先按项目现有方式添加并还原：优先使用项目声明的统一 Leistd 版本，其次参考已直接引用的同系列包版本。项目没有可确认的版本策略时先询问，不默认安装 latest，也不在启用 CPM 的项目中给单个 `PackageReference` 私自写版本。

## 查找当前版本文档

1. 从项目文件、`obj/project.assets.json` 或以下命令确认还原后的实际包和版本：

   ```bash
   dotnet list package --include-transitive
   ```

2. 获取当前生效的 NuGet 全局缓存根目录：

   ```bash
   dotnet nuget locals global-packages --list
   ```

3. 在已安装包目录中枚举文档，不要根据包名猜测文件名：

   ```text
   {global-packages}/{lowercase-package-id}/{version}/docs/*.md
   {global-packages}/{lowercase-package-id}/{version}/lib/*/{package-id}.xml
   ```

每个包的 `docs/` 只放该包所属家族的权威文档；例如 `Leistd.EventBus.*` 中是 `event-bus.md`，`Leistd.ObjectMapping.*` 中是 `object-mapping.md`。先枚举再读取，不维护包名到文件名的推导表。

优先使用包内文档，因为在线仓库可能对应更新版本。XML 用于核对精确签名；存在多个目标框架时，根据项目的 `TargetFramework` 选择对应 `lib/{tfm}`。包内文档缺失时先使用 XML、程序集和包元数据核对能力，并明确报告文档缺口；只有能够定位同一版本源码时才将其作为补充，不用其他版本在线文档推断当前 API。

用户只要求查询或解释时，到完成事实和签名核对为止，不修改项目或启动宿主；用户要求接入、修复或验证时再执行以下完整工作流。

## 工作流

1. 核对能力、宿主与相邻组件边界，按文档和 XML 完成实现。
2. 按实际集成检查 DI/Map、Options、身份授权、Store/DbContext 或迁移，不要求无关能力。
3. 构建并运行相关测试；注册、端点或持久化行为变化时，启动真实宿主验证主路径与必要失败路径。
4. 核对启动与运行结果，不以编译成功代替集成验证。仅将需长期复用的项目特有决策写入项目文档。

找不到 API 时应明确说明该版本未提供，不能臆造 `Leistd.*` 类型或把相近组件当成替代实现。
