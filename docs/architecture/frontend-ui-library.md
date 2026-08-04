# 前端组件库选型

> 架构决策记录。本文只记「决定 + 依据 + 影响 + 备选」这类长期约束；迁移的执行细节与任务分解在 [`docs/plans/2026-07-24-primeng-to-spartan-migration.md`](../plans/2026-07-24-primeng-to-spartan-migration.md)。

## 决定

模板前端（`template/frontend`）采用 **Spartan UI（spartan.ng）** 作为 UI 组件库，取代原 PrimeNG。

- 组件消费模式：headless 逻辑层 `@spartan-ng/brain`（npm 依赖）+ 样式层 helm（经 CLI 复制进本仓库 `libs/ui/`，**属自有代码**）。
- 样式基座：Tailwind CSS v4，组件主题走 CSS 变量（oklch token）。
- 图标：`@ng-icons` + Lucide。
- 表单：Angular Signal Forms（`@angular/forms/signals`）；不使用 `FormsModule`/`ReactiveFormsModule`/`ngModel`（eslint 静态禁止）。
- 数据表格：`@tanstack/angular-table`（headless 表格引擎，服务端 `manualPagination`/`manualSorting`/`rowCount`）；展示层复用自有 `TablePaginator`/`FacetedFilter`，列可见性按优先级（primary/secondary/tertiary）响应式裁剪。
- 列表查询状态：分页/排序/筛选以 **URL query params 为唯一状态源**，刷新、分享链接、浏览器前进后退均可恢复；非法参数回退到默认值。
- HTTP 错误契约：拦截器只负责 401 认证跳转与把错误归一化为类型化 `ApplicationHttpError`（解析 RFC 9457/7807 Problem Details），**不发全局 toast**；具体反馈（字段错误 / Toast / 空状态 / 静默）由发起操作的 feature 决定；全局 Toast 直接用 Spartan Sonner（`@spartan-ng/brain/sonner`），无自建封装。
- 支持范围：紧跟 Angular 最近两个大版本（当前 21/22）。

## 依据

1. **PrimeNG 不再可持续**：PrimeNG ≥22 转 PrimeUI 商业双许可，MIT 版仓库 2026-06-29 归档；本项目所属组织不满足 Community 免费资格，继续使用意味着商业许可成本或停留在已归档、不再维护且锁定 Angular 21 的旧版。
2. **架构免疫供应商锁定**：helm 样式层复制进本仓库成为自有代码，上游停摆也不失能——直接对冲 PrimeNG 事件暴露的第三方断供风险。
3. **技术栈零摩擦**：与既有 Tailwind 4 一等公民集成，`.dark` class 深色模式逐字同构，zoneless-ready。
4. **顺应主流**：命中 2025–2026 headless/shadcn 趋势；Angular 官方亦在 v21 推出 `@angular/aria` headless 基座，方向一致。
5. 许可为 MIT。

## 影响

- 组件层需整体迁移（约 26 类组件、自有拦截器/异常处理/主题服务/共享组件，见 plan）。
- 主题能力收敛：删除 PrimeNG 运行时换色（preset/主色/表面色），保留亮/暗/系统三态 + 单一品牌主题；业务项目按品牌改 CSS 变量。
- 前置依赖：必须先升级到 Angular 22（Spartan 只支持最近两个大版本）。
- helm 组件升级走官方 `ng g @spartan-ng/cli:healthcheck`；**改动过的组件禁用 `migrate-helm-libraries`（会覆盖自定义）**，需对照上游手动合入。
- 生成项目继承此选型；`template/docs/standards/` 与生成项目 Skill 中的 UI 约定随之更新。

## 备选（未采纳）

| 候选 | 未采纳原因 |
| --- | --- |
| PrimeNG v22 | 商业许可 + 供应商锁定，未解决根本问题 |
| PrimeNG v21 停留 | 仓库已归档不再维护，且锁死 Angular 21 |
| Angular Material | 非 Tailwind-first，主题/深色需桥接；仍是第三方 npm 依赖 |
| ng-zorro / Taiga | 同上锁定问题；ng-zorro 深色模式机制与现有 class 切换差距大 |
| HyperUI / DaisyUI / Flowbite / Preline | 静态片段/纯 CSS/停更封装/非 MIT，均非合格的 Angular 组件库 |
| PrimeNG 社区 fork（Optimus UI） | 早期、无企业赞助，存续性不可依赖 |
