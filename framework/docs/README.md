# Leistd 框架文档

Leistd 是面向 .NET 10 的 DDD 应用框架基座。本套文档面向「人 + AI 协作」编写：既便于开发者按任务查阅，也便于 AI 助手作为上下文检索引用。

> 🧭 **不知道从哪看起？** 看下面的「我想……」表，按你的目标直接跳转。

## 我想……

| 我想…… | 看这里 |
| --- | --- |
| 了解某个能力怎么用（锁、事件、响应、追踪……） | [组件总览](./components/README.md) → 对应组件 |
| 在业务项目里引用框架 / 切换本地源码调试 | 下方[「在项目中使用框架」](#在项目中使用框架) |
| 给框架新增或修改一个组件 | [开发规范](./development-guide.md) |
| 发布框架新版本 / 调整版本号 | [版本与发布](./versioning.md) |
| 编译、打包、推送框架本身 | [framework/README.md](../README.md) |
| 了解 DDD 四层基础类型 | [DDD 四层基础类型](./ddd-struct/README.md) |

## 文档地图

```
docs/
├── README.md              # 你在这里——文档首页与导航
├── components/            # 各组件用法文档（按功能分组）
│   ├── README.md          #   组件总览 + 依赖关系图
│   └── <分组>.md          #   lock / security / tracing / …
├── ddd-struct/README.md   # DDD 四层基础类型
├── development-guide.md   # 新增/修改组件的规范（人 + AI 均按此开发）
├── versioning.md          # 版本机制与发布检查清单
└── _doc-template.md       # 组件文档模板规范（写文档时遵循，内部）
```

## 组件文档

按功能分组，每篇遵循统一结构（概念 → 何时使用 → 安装 → 配置 → 使用 → 接口参考 → 注意事项）。完整索引与依赖图见 **[组件总览](./components/README.md)**。

- [动态代理拦截器基类](./components/aop.md)
- [核心原语：时钟与通用异常](./components/core.md)
- [服务注册回调与拦截器织入](./components/dependency-injection.md)
- [事件总线](./components/event-bus.md)
- [业务异常与全局异常处理](./components/exception.md)
- [分布式锁与本地锁](./components/lock.md)
- [对象映射](./components/object-mapping.md)
- [统一 API 响应](./components/response.md)
- [当前用户与身份信息](./components/security.md)
- [链路追踪](./components/tracing.md)
- [工作单元与事务](./components/unit-of-work.md)
- [DDD 四层基础类型](./ddd-struct/README.md)

## 在项目中使用框架

模板生成的后端项目通过 MSBuild 属性 `LeistdUseLocalFramework` 在两种模式间切换：

| 模式 | 取值 | 引用方式 | 适用 |
| --- | --- | --- | --- |
| NuGet（默认） | `false` | `PackageReference Include="Leistd.*"` | 发布、CI、日常开发 |
| 本地源码 | `true` | `ProjectReference` 指向 `framework/` 源码 | 联调、断点步进框架 |

```bash
pwsh scripts/switch-framework.ps1 local     # 切到本地源码模式（可断点调试框架）
pwsh scripts/switch-framework.ps1 nuget     # 切回 NuGet 模式
pwsh scripts/switch-framework.ps1 status    # 查看当前模式
```

各组件包名见对应组件文档的「安装」一节；框架包版本由 `template/backend/Directory.Build.props` 的 `LeistdFrameworkVersion` 统一指定。

---

> 📌 组件文档由源码生成。**新增或修改组件后请同步更新对应文档**，流程见[开发规范](./development-guide.md)。
