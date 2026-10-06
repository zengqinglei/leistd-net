# 前端开发规范

本项目 Angular、Spartan UI、Tailwind CSS 前端的编码规范，同时遵循 [项目通用约定](./coding-common.md)。界面、样式与导航见 [前端界面规范](./frontend-ui.md)。
<!--#if (IncludeLocalization)-->
多语言见 [前端多语言规范](./frontend-i18n.md)，词条、引用与 scope 登记由 `scripts/check-i18n.py` 检查。
<!--#endif-->

## 1. 技术栈

Angular（Signals、zoneless、Angular CLI）、TypeScript、Spartan UI（`@spartan-ng/brain` 无头基元 + 项目持有的 helm 样式层）、Tailwind CSS 4、Angular Signal Forms（`@angular/forms/signals`）、TanStack Table。项目采用的精确版本以 `package.json` 与锁文件为准。禁止 `FormsModule`/`ReactiveFormsModule`/`ngModel`（eslint 拦截）。

## 2. 目录与依赖方向

目录按职责划分，按需新增子目录，不预建空层级。

```text
frontend/
├── _mock/                 # 独立开发用的 API 处理器与数据
├── libs/ui/               # 项目持有的 Spartan Helm 组件
├── src/app/
│   ├── core/              # 应用级服务、认证、拦截器、启动、错误处理、应用级组件
│   ├── features/<x>/      # 业务功能：components/<page>/widgets、services、dtos、models、guards、<x>.routes.ts
│   ├── layout/            # 布局、导航与布局内组件
│   ├── shared/            # 无状态的展示组件、指令、管道、工具与跨功能契约（dtos、models、components、directives、pipes、utils）
│   ├── app.config.ts、app.interceptors.ts、app.routes.ts、app.ts
├── src/environments/      # 公共配置与各部署环境的覆盖
└── public/                # 构建后映射到站点根的静态资源
```

依赖方向（行依赖列，✓ 允许）：

| 依赖方 \ 被依赖 | `core` | `layout` | 同一 `features/<x>` | 其他 `features/<y>` | `shared` | `_mock` |
| --- | --- | --- | --- | --- | --- | --- |
| `core` | ✓ | | | | ✓ | |
| `layout` | ✓ | ✓ | | | ✓ | |
| `features/<x>` | ✓ | | ✓ | | ✓ | |
| `shared` | | | | | ✓ | |
| `_mock` | ✓ | | ✓ | ✓ | ✓ | ✓ |

- 应用装配点（`app.config.ts`、`app.interceptors.ts`、`app.routes.ts`、根组件 `app.ts`）与 `src/environments/` 可依赖全部目录，是业务代码之外唯一引入 `_mock` 的位置。各目录可读取 `environment`，不反向依赖应用装配点，需要装配结果的用例写成 `app.*.spec.ts`。测试文件可引用 `_mock`，不放开其他方向。
- 功能路由文件（`features/<x>/<x>.routes.ts`）可以经 `loadComponent`/`loadChildren` 懒加载其他功能的页面，其他引用仍不允许。
- 多个功能共用的服务或契约下沉：有状态或应用级的放 `core`，无状态的展示组件与契约放 `shared`；依赖 `core` 服务的组件不放 `shared`。功能内复用的组件放该页面的 `widgets/`。
- 业务代码判断请求是否走 Mock 时注入 `core/mock/mocked-url.ts` 的 `MOCKED_URL`（默认 `false`，Mock 构建由 `provideMock` 提供），不调用 `_mock` 中的函数。
- 以上方向由 `eslint.config.mjs` 按解析出的真实文件检查（`import-x/no-restricted-paths`、项目内规则 `local/feature-boundaries`、`import-x/no-unresolved`），因此导入路径一律写成普通字符串字面量。

## 3. 命名

文件名 kebab-case，遵循 Angular 简化风格：

| 类型 | 文件 | 类名或导出 |
| --- | --- | --- |
| 组件 | `{name}.ts`（模板 `.html`） | 不带后缀，如 `UserProfile` |
| 服务 | `{name}-service.ts` | 带 `Service`，如 `UserService` |
| 指令 | `{name}.ts` | 不带后缀；selector 为 `app` + camelCase 属性 |
| 管道 | `{name}-pipe.ts` | 如 `AppDate`；名称见 `@Pipe` |
| 守卫 | `{name}-guard.ts` | 函数式守卫，如 `authGuard` |
| 拦截器 | `{name}-interceptor.ts` | 函数式拦截器，如 `httpErrorInterceptor` |
| 错误处理器等类 | `{name}-handler.ts` | 类名带 `Handler` |
| 布局 | `{name}-layout.ts` | 如 `DefaultLayout` |
| API 契约 | `{name}.dto.ts` | `*Dto` 接口 |
| 前端模型 | `{name}.model.ts` | 需要行为或派生字段的类型 |
| 路由 | `{name}.routes.ts` | |
| 测试装配 | `{name}.testing.ts` | |

组件 selector 为 `app-` + kebab-case（eslint 检查）。API 的请求与响应必须有类型定义；命名表达意图，JSDoc 只补充不明显的契约。

## 4. 分层与依赖注入

- **API 数据访问服务**（功能的 `services/*-service.ts`）只封装 HTTP，返回 DTO 的 `Observable`，不持有页面状态。
- **应用级服务**（认证、主题、语言、设置上下文、启动）放 `core/services`，可持有自身状态与行为。
- **页面状态**优先放组件 signal；状态复杂且有复用或生命周期收益时，抽成组件级 `@Injectable()` 状态类并在组件 `providers` 提供。
- **DTO 与模型**：API 契约放 `dtos/`，只有需要行为或派生字段时才在 `models/` 建前端模型并显式转换；不在 `models/` 放 DTO。
- **作用域选择**：跨页面共享或应用级单例用 `providedIn: 'root'`；随页面销毁的状态用组件 `providers`；需要跨子路由保留的用路由 `providers`（如 `provideTranslocoScope`）。
- 依赖注入一律用 `inject()`。构造函数只做属性赋值、需要注入上下文的生命周期接线（`effect()`、`takeUntilDestroyed()`、随 `DestroyRef` 释放的订阅）与进入页面的首次只读查询；写请求、多步流程和清理会话放 `ngOnInit`（判据：构造不改变服务端或会话状态）；在注入上下文之外（如 `ngOnInit`）调用这些 API 时显式传入 `destroyRef` 或 `{ injector }`。

## 5. 状态

- 组件内部与简单父子组件间的状态用 Signals。
- 媒体查询（断点、系统暗色偏好）用 CDK `BreakpointObserver` 转成信号，不手写 `matchMedia`。
- 启动流程、拦截器判断当前路由用 `core/routing/entry-route-service.ts`，不读 `Router.url`（初始导航前恒为 `/`）。
- 列表的分页、排序、筛选以 URL query params 为唯一来源（`queryParamMap` 派生 + `router.navigate({ queryParams })` 回写），非法参数回退默认，不存 localStorage。

**显示偏好同源**：语言、主题、时区等显示偏好的切换器与偏好页必须写同一处存储。

| 形态 | 写入口径 | 例 |
| --- | --- | --- |
| 已做成 setting definition | 切换器走同一个 `setForCurrentUser`，不旁路写 `localStorage` | 语言（`Display.Language`） |
| 未做成 setting definition | 允许纯 `localStorage`，但不得同时出现在偏好页 | 主题 |
| 本服务不拥有该偏好（跨服务只读） | 切换器只作用于本会话；设置页只放去签发方的外链 | 资源服务里的账户偏好 |

给偏好补 setting definition 时同一提交改掉切换器。

## 6. 错误处理

- `http-error-interceptor` 负责认证处置（401 跳转）并把 RFC 9457 Problem Details 归一化为 `ApplicationHttpError`（`errors` 兼容框架的数组与官方字典两种形状）；页面不自己猜测响应形状。
- 发起操作的 feature 决定反馈：字段错误优先回填到表单，其余可展示的 4xx 按 `detail`、`title` 顺序取安全文案 toast（`@spartan-ng/brain/sonner`），或显示空状态、静默；同一错误不重复提示。
- 5xx 不展示技术细节，使用通用文案，响应含 `traceId` 时附上本地化的追踪 ID 标签。
- `GlobalErrorHandler` 只兜底未处理的非 HTTP 错误，识别并忽略已归一化的 HTTP 错误。
- 可以用 `catchError` 处理特定错误，但不吞掉错误；需要特定交互时按稳定 `code` 分支，不按单个状态码。
- 前端不兼容框架可选的响应信封（`AddResponseWrapper()`），开启须同时改拦截器。

## 7. Mock

- 每个后端新端点同步补 Mock（`_mock/data` + `_mock/api` + `_mock/index.ts` 注册），前端可脱离后端运行。
- Mock 代码只放 `_mock`，引入规则见第 2 节；Mock 自身的单测与被测文件相邻放在 `_mock/`。
- Mock 与后端保持可观察行为一致：认证、权限、参数校验、过滤语义与失败响应；不复制后端内部实现。

**跨层字段同步**：新增或修改贯穿前后端的字段时逐环改到：后端入参 DTO → 应用层逻辑 → 前端 DTO → 前端服务传参 → Mock 处理器 → Mock 数据。

## 8. 测试

- 含参数映射、分支或状态的服务、`pipe` 与含复杂业务逻辑的函数必须有单元测试；只把参数原样转发给 `HttpClient` 的服务由使用它的页面测试覆盖。用例名规则见 [测试规范](./testing.md)。
- 单测跑在真实 Chromium 里，不得触发真实的下载、打印或页面跳转：对副作用那一步打桩（`saveBlob`，或 `vi.spyOn(HTMLAnchorElement.prototype, 'click').mockReturnValue(undefined)`——`vi.spyOn` 默认仍调用原实现），断言"发起了什么"，见 `shared/utils/download-file.spec.ts`。

## 9. 日期与时区

时间以 UTC 传输与存储，只在展示时按会话时区换算。时区取自 `Display.TimeZone`（IANA 名），书写方式取自界面语言，两者由 `SettingContextService` 提供。

- 业务日期用 `appDate` 管道，不用 Angular 的 `date`：后者传 IANA 名会静默回落到浏览器时区，也表达不了夏令时。

  ```html
  {{ row.creationTime | appDate: 'full' : displayTimeZone() : displayLocale() }}
  ```

- 槽位（`full`、`short`、`date`、`time`、`monthDayTime`）由调用点按用途定；locale 取当前界面语言，不由时区推导。精度与格式串不做成设置项，语种写法差异加在管道的 `WRITING_OVERRIDES`。
- 时区候选项按"能否真的渲染"判定，不用 `Intl.supportedValuesOf('timeZone')` 过滤（它只列规范名，`UTC`、`Asia/Kolkata` 等可用值不在其中）。时区值只收 IANA 名，写入端已校验。
<!--#if (IncludeRealTime)-->

## 10. 资源订阅：推送只表示"该重新查询了"

业务推送不持久化，断线期间、以及首次查询完成到加入订阅之间的变更都不会补发。把推送当作刷新提示的页面按同一写法接入（角色列表是示例）：

- 先监听 `SignalRService.resourceSubscribed$`，按自己的资源键过滤，订阅被确认（首次、自动重连后、重建连接后）时重新查询一次；再用 `watchResource(key, destroyRef?)` 登记订阅，并调用一次 `connect()`。订阅随页面销毁自动撤销，与连接何时建立无关；不要写成 `connect().then(() => 订阅)`，页面在连上之前离开时连上后照样会订阅。多个页面持有同一个键时，最后一个离开才真正退订。
- 收到业务事件时同样走既有的查询入口，不从推送内容里取数据：列表数据仍经受权限保护的查询取得，资源订阅本身也经过作用域和权限校验。
- 不用 `isConnected` 代替订阅确认：连接恢复时订阅还没重新建立，那时查询仍会漏掉之后的变更。

聊天、追加流这类按推送内容增量更新的功能，恢复方式由功能自己决定，不套用这条。
<!--#endif-->
