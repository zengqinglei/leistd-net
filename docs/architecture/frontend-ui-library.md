# 前端组件库选型

> 架构决策记录。只记「决定 + 依据 + 影响 + 备选」这类长期约束，不记执行细节。

## 决定

模板前端（`template/frontend`）采用 **Spartan UI（spartan.ng）** 作为 UI 组件库。

- 组件消费模式：headless 逻辑层 `@spartan-ng/brain`（npm 依赖）+ 样式层 helm（经 CLI 复制进本仓库 `libs/ui/`，**属自有代码**）。
- 样式基座：Tailwind CSS v4，组件主题走 CSS 变量（oklch token）。
- 图标：`@ng-icons` + Lucide。
- 字体：正文默认 **Geist**，经 `@fontsource/geist` 自托管（npm 依赖打包字体文件，离线可用、无 CDN），回退 `system-ui, sans-serif`；属模板设计系统的一部分，业务项目可替换为品牌字体或改回系统字体栈。
- 表单：Angular Signal Forms（`@angular/forms/signals`）；不使用 `FormsModule`/`ReactiveFormsModule`/`ngModel`（eslint 静态禁止）。
- 数据表格：`@tanstack/angular-table`（headless 表格引擎，服务端 `manualPagination`/`manualSorting`/`rowCount`）；展示层复用自有 `TablePaginator`/`FacetedFilter`，列可见性按优先级（primary/secondary/tertiary）响应式裁剪。
- 列表查询状态：分页/排序/筛选以 **URL query params 为唯一状态源**，刷新、分享链接、浏览器前进后退均可恢复；非法参数回退到默认值。
- HTTP 错误契约：拦截器只负责 401 认证跳转与把错误归一化为类型化 `ApplicationHttpError`（解析 RFC 9457/7807 Problem Details），**不发全局 toast**；具体反馈（字段错误 / Toast / 空状态 / 静默）由发起操作的 feature 决定；全局 Toast 直接用 Spartan Sonner（`@spartan-ng/brain/sonner`），无自建封装。
- 支持范围：紧跟 Angular 最近两个大版本（当前 21/22）。

## 依据

1. helm 代码归仓库所有，可直接审查、定制并避免运行时主题封装。
2. Brain 与 Tailwind CSS v4、`.dark` 深色模式和 zoneless Angular 直接组合。
3. Spartan、Tailwind、TanStack Table、Lucide 和 Geist 均满足当前许可要求。

## 维护约束

- 主题保留亮、暗、系统三态和一套品牌 token；业务项目通过 CSS 变量调整品牌。
- Angular 与 Spartan 版本必须位于双方支持范围内。
- helm 组件升级走官方 `ng g @spartan-ng/cli:healthcheck`；**改动过的组件禁用 `migrate-helm-libraries`（会覆盖自定义）**，需对照上游手动合入。
- 修改组件约定时同步 `template/docs/standards/` 与生成项目 Skill。

## 备选（未采纳）

| 候选 | 未采纳原因 |
| --- | --- |
| PrimeNG v22 | 商业许可 + 供应商锁定，未解决根本问题 |
| PrimeNG v21 停留 | 仓库已归档不再维护，且锁死 Angular 21 |
| Angular Material | 非 Tailwind-first，主题/深色需桥接；仍是第三方 npm 依赖 |
| ng-zorro / Taiga | 同上锁定问题；ng-zorro 深色模式机制与现有 class 切换差距大 |
| HyperUI / DaisyUI / Flowbite / Preline | 静态片段/纯 CSS/停更封装/非 MIT，均非合格的 Angular 组件库 |
| PrimeNG 社区 fork（Optimus UI） | 早期、无企业赞助，存续性不可依赖 |
