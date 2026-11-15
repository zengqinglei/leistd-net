# 三层代码审查问题整改

状态：进行中 · 风险档：高（框架公共行为、DI 代理、OIDC 声明、跨框架/模板后端/模板前端） · 基线：develop `c4d0eeba`

## 1. 需求

外部静态审查报告（基线 `a93f219b`）列出 16 条问题和 1 条观察。经 Claude、Codex 两个会话独立复核、交叉对比并按"最佳实践终局、不过度设计、不过度考虑安全与竞态边界"裁定，确定以下 18 项整改，一次修完，不留遗留项。

非目标：不做批量目录或命名整理；不引入全局状态库、通用查询层、新 DI 容器、外部人机验证服务或额外并发防护；不处理已裁定不修的 F-DC-C1（工作单元外连接串比较：宿主回调拿到的是连接串，自建 DbConnection 偏离契约，且失败为显式异常）。

### 已确认的决策（用户，2026-11-15）

- 18 项全部纳入本次整改，Codex 实施并完成端到端验证、提交、开 PR 与合并；Claude 做代码审查，审查通过才推送。
- F03 订阅筛选采用服务端搜索：订阅是规模不封顶的业务实体，"订阅通常很少"属于规模假设，不作为取舍依据。
- 后台任务锁检查只看已启用的集群任务：停用名单启动时一次读定，检查已尊重总开关，应同样尊重逐任务停用。
- F-DC-02 补取消回调：拦截器暂存的是一次保存的状态，EF 的成功/失败/取消三个出口都要收口；失败与取消都丢弃，不改为成功后才清空实体事件。
- B02 验证码保留 SVG，改用字形轮廓；不引入栅格图像库（许可或原生依赖负担）。
- F-DC-01 兑现文档承诺的类代理，不改为只支持接口代理。

### 验收标准

框架：

- **F-RESP-01 MVC 包装保留结果语义**（P2）：Given 启用 `AddResponseWrapper()` 的 MVC 控制器返回 `CreatedAtAction`、`Created` 或 `Accepted`；When 请求；Then 状态码、`Location` 与原结果一致，响应体为包装信封，原结果的 `ContentTypes`/`Formatters` 仍生效；`ActionResult<T>` 按信封类型序列化。覆盖：`Leistd.Response.Tests` 新增 MVC TestServer 测试。
- **F-DC-01 类代理支持构造注入**（P2）：Given 以具体类注册、只有带参构造、方法为 virtual 的服务被附加拦截器；When 解析并调用；Then 构造依赖由容器注入、构造只执行一次、拦截生效、生命周期与释放正确。Given 以工厂或实例方式注册的类服务被附加拦截器；Then 启动或注册时抛出指明改用实现类型或接口注册的异常。接口代理与键控注册行为不变。覆盖：`Leistd.DependencyInjection.Tests` 新增类代理测试，回归键控与 UoW 织入测试。
- **F-DC-02 取消保存丢弃已收集事件**（P3）：Given 实体登记本地事件后保存被取消；When 同一上下文之后成功保存其他变更；Then 被取消那次收集的事件不发布，新登记的事件正常发布。覆盖：`LocalEventInterceptorTests` 新增取消用例（异步路径）。
- **BG-LOCK 锁检查只看已启用集群任务**（P3）：Given 登记的集群任务全部在 `DisabledJobs` 中且未注册 `IDistributedLock`；Then 宿主正常启动。Given 存在未停用的集群任务且无锁；Then 启动失败（现有行为）。Given `Enabled=false`；Then 跳过检查（现有行为）。覆盖：`RecurringJobExecutionTests` 或同家族开关测试。
- **F-E1 Webhook 文档写明租户过滤前提**（P2）：`framework/docs/components/webhooks.md` 普通 DbContext 示例附近说明同库多租户须由宿主安装租户过滤（继承 `BaseDbContext` 即有），链接 `multi-tenancy.md#隔离业务实体`；"只投给当前租户"处加限定。过滤代码只在多租户文档维护。覆盖：文档闸门；租户隔离已由 `WebhookPublisherTests` A12 覆盖，不新增测试。
- **F-E2 实时/通知示例可直接运行**（P3）：`Leistd.RealTime.AspNetCore.SignalR/DependencyInjection.cs` 两段 XML 示例与 `notifications.md` 共用 Hub 代码块包含订阅授权器注册（或 `AddAllowAllRealTimeSubscriptions()` 并注明适用范围）与 `AddNotificationsSignalR<RealTimeHub>()`。覆盖：关键 XML 示例在临时最小宿主单独编译一次（人工验收，不新增测试资产）。
- **F-DC-03 对象映射文档与实现一致**（P3）：删除不存在的 `Map<TDestination>(object)`；校验时机写为首次解析 `IMapper`；校验能力写为"编译已登记映射，显式映射与目标成员完整性由 Configurators 中的 Mapster 严格模式决定"。覆盖：文档闸门。

模板后端：

- **B01 Resource 正确显示邮箱确认状态**（P2）：Given 申请 email scope；When Identity 签发访问令牌与身份令牌；Then 含布尔 `email_verified`，值等于用户 `EmailConfirmed`；未申请 email scope 时不含。刷新令牌按库重建后值随之更新。Resource 侧按 `bool.TryParse` 读取（true/True 为真，缺失与非法为假）。覆盖：`OpenIddictLifecycleTests`、`ResourceUserProjectionTests`；`scripts/test-template-oidc-e2e.ps1` 的 `resource-browser-me` 断言 `isEmailVerified`。
- **B02 验证码响应不含答案**（P2）：Given 获取验证码；Then 解码后的 SVG 不含 `<text`、答案字符串、字符名或固定字形标识，字形以 `<path>` 绘制且随机变换已计入坐标，`viewBox` 保留，人眼可读；消费、一次性、缓存契约不变。覆盖：验证码单元测试；登录页人工查看一次。
- **B03 开放应用可在 public 与 confidential 间切换**（P2）：Given public 应用改为 confidential；When 保存；Then 生成新 secret，仅本次响应返回明文，前端用现有一次性展示对话框显示，新 secret 能走 client_credentials。Given confidential 改为 public；Then 清除 secret。Given 类型不变；Then 不返回 secret、凭据不变。更新时 OpenIddict 校验失败返回业务错误码而非 500。Mock 行为一致。覆盖：`OpenIddictLifecycleTests`、`open-applications.spec.ts`、Mock spec。
- **B04 Passkey 展示查询回到应用层**（P3）：删除 `PasskeyDomainService.GetListAsync`，调用方直接用仓储查询（展示处排序）；`PasskeyAppService.ToOutput` 改为 `IObjectMapper` 约定映射。覆盖：现有 Passkey、两步验证测试回归。

模板前端：

- **F01 列表搜索与 URL 一致**（P2）：Given URL 的 keyword 被视图切换、无参导航或后退清除或改变；When 输入与上一次搜索相同的词；Then 发起对应查询。比较依据是当前 URL keyword（trim 后空值归一），首尾空格不重置页码。六个页面：webhooks、roles、users、tenants、open-applications、operation-records。覆盖：`webhooks.spec.ts` 跨视图同词；`tenants.spec.ts` 同一组件实例收到无 keyword 的 query 后再输入旧词。
- **F02 加载规范使用的全部字重**（P3）：`styles.css` 导入 Geist 400/500/600。覆盖：断言 FontFace 登记含 500、600；生成项目截图对比。
- **F03a 筛选器按值判断选中**（P2）：Given faceted-filter 的值不在当前选项中；Then 触发器显示该值的后备标签，可清除；`false` 等合法值视为已选。覆盖：`faceted-filter.spec.ts`。
- **F03b Webhook 订阅筛选服务端搜索**（P2）：Given 投递视图打开订阅筛选；When 输入关键词；Then 防抖后按 keyword 查询订阅（每次最多 20 条），总数大于返回数时提示输入关键词缩小范围，有加载中、失败可重试、无结果状态；URL 中的订阅 ID 不在结果中时按 `getSubscription(id)` 补名称，查不到显示 ID，均可清除；"查看投递"入口保留。触发器视觉与工具栏其他筛选器一致。覆盖：`webhooks.spec.ts`、Mock 订阅数据超过 100 条、`webhooks.e2e.ts` 扩展。
- **F04 清除按钮焦点可见**（P3）：faceted-filter 清除按钮使用 `hlmBtn variant="ghost"`，键盘聚焦可见且不被浮层裁切。覆盖：`faceted-filter.spec.ts`；明暗主题人工查看。
- **F05 账户面板刷新失败保留内容**（P3）：login-devices、external-logins、two-factor-passkey-settings、two-factor-settings、two-factor-email-settings 五个面板：未加载过（值为 null）失败时显示错误与重试；已有数据时刷新失败保留内容并 toast；首次加载用等高骨架。覆盖：各面板 spec（login-devices、external-logins 必补，其余挑一个）。

## 2. 方案

逐项的现状证据、官方参照与备选取舍见复核记录（会话产物，不提交）。只列会改变边界的取舍：

| 项 | 采用 | 不采用及原因 |
| --- | --- | --- |
| F-RESP-01 | 原结果对象上改 `Value` 并重设 `DeclaredType`，与主流 DDD 框架的结果包装做法一致 | 逐个复制 MVC 子类的 `OnFormatting`：平行实现 |
| F-DC-01 | 参照 Autofac 类拦截：`ProxyBuilder.CreateClassProxyType` 生成代理类型，`ActivatorUtilities` 激活并传入拦截器，代理即实例 | 先建 target 再建代理：基类构造执行两次；只支持接口代理：违背文档承诺 |
| F-DC-02 | 取消回调复用 `Discard` | 推迟到成功后清空实体事件：偏离参照实现，无同上下文重试的调用方 |
| BG-LOCK | 启动检查按"登记且未停用"的集群任务判断 | 新增开关：无必要 |
| B02 | OFL 字体离线导出字符集字形轮廓为常量，运行时把随机位移/旋转/缩放计入路径坐标；随包附字体许可说明 | 栅格图：需图像库（许可或原生依赖），同样可被识别；外部验证服务：违背克隆即可运行 |
| B03 | 转换时生成 secret 并一次性返回 | 禁止改类型：收回现有能力 |
| F03b | webhooks 页面内用本地 Spartan Combobox 做远程搜索，后端已支持 keyword/offset/limit，无需改后端 | 共享远程筛选组件：只有一处使用；滚动加载更多：搜索缩小范围已足够 |
| F02 | 按字重导入静态文件 | variable 包：规范只用三档，需改依赖与两份锁文件 |
| F05 | 沿用 `settings-page-state.ts` 的"有快照只 toast" | 内联"可能过期"标注与新忙碌状态机制：规范未要求 |

兼容：F-DC-01 对工厂/实例式类服务加拦截从"运行时构造失败"变为"注册时明确报错"，属行为收紧；F-RESP-01 修正 MVC 响应头与格式化，不改信封结构；其余为缺陷修复或文档修正。无数据迁移。

## 3. 计划

按交付面分 3 个 PR，依次从最新 develop 建分支；等待 CI 时可开始下一个，后一个推送前 rebase develop。规格文档随 PR1 提交，§4 与状态随 PR3 提交。

| PR | 内容 | 产物 | 验证 |
| --- | --- | --- | --- |
| PR1 框架 | F-RESP-01、F-DC-01、F-DC-02、BG-LOCK、F-E1、F-E2、F-DC-03 | 组件源码、测试、`response.md`、`dependency-injection.md`、`aop.md`（如需）、`background-jobs.md`、`webhooks.md`、`notifications.md`、`object-mapping.md`、XML 示例 | 受影响测试项目（选择器给出的反向依赖闭包）；打包并检查随包 XML 与文档；关键 XML 示例单独编译；`check-all -Only` 文档与注释闸门；PR CI 全绿 |
| PR2 模板后端 | B01、B02、B03（含前端展示与 Mock）、B04；OIDC 端到端断言 | 模板后端源码与测试、前端 open-applications、Mock、`scripts/test-template-oidc-e2e.ps1` | 工作区 `-Sync` 加目标测试类与集成测试类；Identity、Resource、Standalone 生成场景构建；OIDC 端到端（worker-01）；Mock 与 i18n 闸门；PR CI 全绿 |
| PR3 模板前端 | F01、F02、F03a、F03b、F04、F05 | 模板前端源码、spec、Mock、`webhooks.e2e.ts` | 目标 spec 与 lint；开、关本地化两种生成场景；Mock 站点浏览器验证与改前改后截图；生成项目连真实后端跑 `webhooks.e2e.ts`（worker-01）；PR CI 全绿 |

每个 PR 推送前经 Claude 代码审查，结论为"通过"才推送；CI 全绿即合并。

不做的事：见 §1 非目标；不修改 F-DC-C1；不新增"示例启动到端点映射"类重复测试；不为 F01 在其余四个同构页面逐页复制 spec。
