# Leistd 框架文档

> 提示：组件文档由源码生成，新增或修改组件后应同步更新文档，以保证文档与代码一致。具体流程见 [开发规范](./development-guide.md)。

本套文档面向「人 + AI 协作」编写：既便于开发者快速查阅，也便于 AI 助手作为上下文检索与引用。每篇文档遵循统一的[模板规范](./_doc-template.md)，结构稳定、信息密度可控，方便人机双方按需取用。

## 文档结构

- **快速导航**：开发规范与版本发布等总体性约定。
- **组件分组**：按功能分组的组件文档，每组对应一个包集合，由源码生成。
- **DDD 四层**：领域驱动设计的分层结构说明。
- **规范模板**：文档编写所遵循的内部模板。

## 快速导航

- [开发规范](./development-guide.md)
- [版本与发布](./versioning.md)
- [组件总览](./components/README.md)
- [DDD 四层结构](./ddd-struct/README.md)
- [文档模板规范（内部）](./_doc-template.md)

## 组件分组

详见[组件总览](./components/README.md)，下含各分组文档：

- [AOP / 动态代理](./components/aop.md)
- [核心原语](./components/core.md)
- [依赖注入](./components/dependency-injection.md)
- [事件总线](./components/event-bus.md)
- [异常处理](./components/exception.md)
- [分布式/本地锁](./components/lock.md)
- [对象映射](./components/object-mapping.md)
- [统一响应](./components/response.md)
- [安全 / 当前用户](./components/security.md)
- [链路追踪](./components/tracing.md)
- [工作单元](./components/unit-of-work.md)

## DDD

- [DDD 四层结构](./ddd-struct/README.md)

## 规范

- [开发规范](./development-guide.md)
- [版本与发布](./versioning.md)
- [文档模板规范（内部）](./_doc-template.md)
