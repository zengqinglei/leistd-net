# 前端开发规范

遵循[通用约定](./coding-common.md)；界面与样式见[前端界面规范](./frontend-ui.md)。
<!--#if (IncludeLocalization)-->
多语言见[前端多语言规范](./frontend-i18n.md)；`scripts/check-i18n.py` 检查词条、引用与 scope 登记。
<!--#endif-->

## 1. 技术栈

Angular（Signals、zoneless、Signal Forms `@angular/forms/signals`）、TypeScript、Spartan UI、Tailwind CSS 4、TanStack Table。版本以 `frontend/package.json` 与锁文件为准。表单写法与 ESLint 约束见[前端表单规范](./frontend-forms.md)。

## 2. 目录与依赖方向

按需建目录。`core/` 放应用级服务、守卫、拦截器与通用错误处理，`features/<x>/` 放业务路由、页面、该功能的服务和契约，`layout/` 放布局及导航，`shared/` 放无状态组件、工具和跨功能契约。`frontend/libs/ui/` 持有 Helm 组件，`frontend/public/` 映射到站点根。

功能目录按 `components/<page>/widgets`、`services`、`dtos`、`models`、`guards` 组织，页面内复用的组件放在该页的 `widgets/`，多个功能共用的有状态实现下沉到 `core`，无状态实现下沉到 `shared`。

`frontend/eslint.config.mjs` 校验依赖方向。`shared` 只依赖自身，`core` 另可依赖 `shared`，`layout` 与功能另可依赖 `core` 和自身。功能间仅允许路由文件通过 `loadComponent` 或 `loadChildren` 懒加载。应用装配文件（`app.*.ts`）与测试可以引用 `_mock`；各目录可读 `environment`。导入路径用字符串字面量。

- 业务代码判断请求是否走 Mock 时注入 `frontend/src/app/core/mock/mocked-url.ts` 的 `MOCKED_URL`（默认 `false`，Mock 构建由 `provideMock` 提供），不调用 `_mock` 中的函数。

## 3. 命名

文件名 kebab-case，遵循 Angular 简化风格：

| 类型 | 文件 | 类名或导出 |
| --- | --- | --- |
| 组件 | `{name}.ts`（模板 `.html`） | 不带后缀 |
| 服务 | `{name}-service.ts` | 带 `Service`，如 `UserService` |
| 指令 | `{name}.ts` | 不带后缀；selector 为 `app` + camelCase 属性 |
| 管道 | `{name}-pipe.ts` | 如 `AppDate`；名称见 `@Pipe` |
| 守卫 | `{name}-guard.ts` | 函数式守卫，如 `authGuard` |
| 拦截器 | `{name}-interceptor.ts` | 函数式拦截器，如 `httpErrorInterceptor` |
| 错误处理器等类 | `{name}-handler.ts` | 类名带 `Handler` |
| 布局 | `{name}-layout.ts` | 如 `DefaultLayout` |
| API 契约 | `{name}.dto.ts` | `*Dto` 接口 |
| 前端模型 | `{name}.model.ts` | 需要行为或派生字段的类型 |
| 常量 | `{name}.constants.ts` | 跨功能放 `shared/constants` |
| 路由 | `{name}.routes.ts` | |
| 测试装配 | `{name}.testing.ts` | |

selector 用 `app-` + kebab-case（eslint 检查）；API 请求/响应须有类型。

- JSDoc 按需补服务边界、状态归属和协议契约，不逐成员翻译名称；`@param`、`@returns` 只补单位、空值、失败等非显然信息，不重复 TypeScript 类型。
- 实现原因用独占行 `//`，保留订阅清理、竞态、安全与可访问性约束；模板/样式不写显然标签。
- 上游 Skill 与 helm 原样内容遵循上游风格，不为注释精简制造定制差异；本地改动见[Spartan 维护约定](./frontend-spartan.md)。

注释原则参考 [Angular 框架规范](https://github.com/angular/angular/blob/main/contributing-docs/coding-standards.md#write-useful-comments)与 [TypeScript JSDoc](https://www.typescriptlang.org/docs/handbook/jsdoc-supported-types.html)。

## 4. 分层与依赖注入

- 功能服务封装 HTTP、返回 DTO `Observable`，页面状态由组件持有。应用级服务可持有会话状态。
- 组件与简单父子状态用 Signals，页面状态优先放组件；复杂且有复用或生命周期收益时，才抽成组件级 `@Injectable()` 状态类，由组件 `providers` 提供。
- **DTO 与模型**：API 契约放 `dtos/`，只有需要行为或派生字段时才在 `models/` 建前端模型并显式转换；不在 `models/` 放 DTO。
- 路由守卫与按钮显隐只改善交互体验；服务端必须对每次请求校验权限。
- **作用域选择**：跨页面共享或应用级单例用 `providedIn: 'root'`；随页面销毁的状态用组件 `providers`；需要跨子路由保留的用路由 `providers`（如 `provideTranslocoScope`）。
- 用 `inject()` 注入依赖。构造只做赋值、生命周期接线和页面首读。写请求、多步流程、清会话放 `ngOnInit`。在注入上下文外调用 `effect()` 或 `takeUntilDestroyed()` 时传 `{ injector }` 或 `destroyRef`。

## 5. 状态

- 确认写用 `ConfirmService` 的 `action`，只放一次写请求：可取消的前置验证开框前完成，需再认证重试、接弹窗或跳转的不用。成功关框，调用方提示/刷新；失败由框 toast 并留框；入口防重入、无行内转圈。
- 媒体查询（断点、系统暗色偏好）用 CDK `BreakpointObserver` 转成信号，不手写 `matchMedia`。
- 启动流程、拦截器判断当前路由用 `frontend/src/app/core/routing/entry-route-service.ts`，不读 `Router.url`（初始导航前恒为 `/`）。

### 会话级读取

会话服务持有快照与在途读取，同范围站内导航复用已有结果。统计时按实际主体、工作区和请求语言区分。

| 数据 | 启动及范围切换 | 前台及实时重连 | 写后及显式重读 |
| --- | --- | --- | --- |
| `/me` 与偏好 | 登录主体建立时取。工作区切换复用本人数据 | 回到前台、实时连接疑似 401 时重验，合并在途 | 资料和偏好用写响应。安全设置、再认证沿用既有重验 |
| 权限 | 进入时按范围取。宿主深链可另读本人平台入口权限 | 无 | 角色授权与用户角色变更后按现有入口重读 |
| 设置 | 进入时按范围预读。页面首读复用同主体、同范围、同语言的成功快照；缺失或失败由页面补读，本人范围独立 | 无直接读取。若重验改变界面语言，打开中的设置页随语言变化重读 | 设置写成与手动重试强制读取。偏好 PATCH 使用响应，语言变化按前列处理 |
| 通知历史 | 铃铛首次消费时读，同一 SignalR 代次复用。切换工作区或登录主体后目标代次重新首读 | 无历史重取 | 已读和删除更新本地列表，显式重试重读 |

启动失败重试重新执行启动流。目录在同一主体内复用，按工作区不可用恢复和授权变更入口刷新。

## 6. 错误处理

- `http-error-interceptor` 负责认证处置（401 跳转）并把 RFC 9457 Problem Details 归一化为 `ApplicationHttpError`（`errors` 兼容框架的数组与官方字典两种形状）；页面不自己猜测响应形状。
- 发起操作的 feature 决定反馈：字段错误回填表单，其余用归一化 `message` toast（`@spartan-ng/brain/sonner`），或显示空状态、静默；同一错误不重复提示。
- 公开文案只取 Problem Details 里校验过的字段错误/`detail`/`title`（5xx 限带码读请求），不回落 `statusText`/正文/原始异常；其余按 `category` 用本地化兜底，附 `traceId`。拦截器传入方法：写请求遇到无响应、5xx 或 2xx 解析失败归为 `uncertain`，提示可能已完成、先核实，不自动重放。
- `GlobalErrorHandler` 只兜底未处理的非 HTTP 错误，识别并忽略已归一化的 HTTP 错误。
- 认证与协议层处置（401 跳转、受限会话的 403、启动时的未登录判定）由 `http-error-interceptor` 与 `startup-service` 按状态码处理，eslint 只对它们放行 `local/no-status-code-branch`；feature 使用归一化后的错误反馈，按稳定 `code` 分支处理业务交互。业务错误缺少 `code` 不构成 feature 按状态码分支的理由，应在后端补码。
- 启动重试与错误页的地址栏行为见[前端路由规范 §1](./frontend-routing.md#1-路由守卫与地址栏)。
- `catchError` 只处理特定错误，不吞掉错误。
- 前端不兼容框架可选的响应信封（`AddResponseWrapper()`），开启须同时改拦截器。

## 7. Mock

- 每个后端新端点同步补 Mock（`_mock/data` + `_mock/api` + `frontend/_mock/index.ts` 注册），前端可脱离后端运行。
- Mock 代码只放 `_mock`，引入规则见第 2 节；Mock 单测在 `_mock/` 与被测文件相邻。
- 处理器直接返回正文（按 200 响应）；要给状态码、响应头或延迟时返回 `new MockResponse({...})`，不返回带 `status` 的对象字面量。
- Mock 与后端保持可观察行为一致：认证、权限、参数校验、过滤语义与失败响应；不复制后端内部实现。
- Mock 业务数据存本页内存，重载回初始数据；会话、设置覆盖与用户存 `sessionStorage`（设置用 `frontend/_mock/utils/stored-map.ts`，直读直写），刷新不丢。新页面初始存储由浏览器决定（保留 opener 打开、复制或恢复标签页会带入原存储），不保证新标签页未登录；之后各页会话与设置改动互不影响。

**跨层字段同步**：贯穿前后端字段的增改须逐环同步：后端入参 DTO → 应用层逻辑 → 前端 DTO → 前端服务传参 → Mock 处理器 → Mock 数据。

## 8. 测试

- 含参数映射、分支或状态的服务、`pipe` 与含复杂业务逻辑的函数必须有单元测试；只把参数原样转发给 `HttpClient` 的服务由使用它的页面测试覆盖。用例名规则见 [测试规范](./testing.md)。
- 单测跑在真实 Chromium 里，不得触发真实的下载、打印或页面跳转：对副作用那一步打桩（`saveBlob`，或 `vi.spyOn(HTMLAnchorElement.prototype, 'click').mockReturnValue(undefined)`——`vi.spyOn` 默认仍调用原实现），断言"发起了什么"，见 `frontend/src/app/shared/utils/download-file.spec.ts`。

## 9. 日期、时区与数字

见[前端时间、数字、图表与推送规范 §1](./frontend-data.md#1-日期时区与数字)。

## 10. 首屏性能

- 按真实访问的 P75、移动与桌面分开统计：LCP ≤ 2.5 s、INP ≤ 200 ms、CLS ≤ 0.1。
- 懒加载用于功能路由（`loadComponent`/`loadChildren`）；落地必经的布局外壳保持静态引用：拆出后要等守卫结束才沿导入链下载，字节只是挪后。首屏非必需的第三方脚本按需加载。
- 只给真实的 LCP 图片提优先级：`NgOptimizedImage` 的 `priority`；瀑布图显示它发现晚且地址已知时，再加同 URL 的 preload。
- 上调 `initial` 预算须附 `--stats-json` 构建统计的前后差异。
