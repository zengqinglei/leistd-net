# 两步验证与通行密钥复查反馈

状态：已完成 · 风险档：中

## 1. 需求

依据 2026-10-31 的 #183/#184 复查回执实施；本轮只改模板。用户当日裁决：AC7 改为通行密钥校验失败不计入账号锁定、保留失败审计与日志；按 DomainFormat 动态补允许来源；登记和删除通行密钥保持近期认证门槛。

| 项 | 决定与理由 | 验收标准 |
| --- | --- | --- |
| A | 成功消费登录邮箱码后，仅释放属于该挑战的发送间隔；次数耗尽保留限速，防止换挑战绕过间隔 | Given 间隔内的登录码，When 成功消费/次数耗尽，Then 可立即发新码/仍被限频（EmailChallengeStoreTests） |
| B、C、自查 1 | 不清按邮箱自动关联的连接标记，不改找回资格；说明重新绑定与部署撤配置的出路，拒绝提示补通行密钥 | 人工核对 auth.md 与三份词条 |
| D | 补方式解析、找回标记清除、找回后免口令登录覆盖，不为主动绑定的默认参数增加低价值测试 | Given 部署/因子/恢复码组合，Then 默认方式或拒绝符合解析规则（TwoFactorMethodResolverTests、IndependentFactorTests、PasskeySignInTests） |
| 1 | 登记、删除仍只需近期认证；单独收紧删除不能保护同门槛的登记，已控新会话是强前提 | 人工核对入口清单与理由；既有登记管理测试 |
| 2 | 静态 Origins 与当前租户 DomainFormat 来源取并集；协议、端口取静态来源，RP ID 不变，支持内建子域名部署 | Given acme，Then 登记与登录接受 acme.example.com，拒绝 other.example.com、evil.com（PasskeySignInTests） |
| 3、自查 2 | 匿名认证节流交网关；发送间隔不长于有效期写部署文档，不另建应用限流或跨字段配置校验 | 人工核对部署文档 |
| 4 | 第二步失败清 PasskeyState，统一仪式只认一次的语义 | Given 错误断言，When 同选项重交正确断言，Then 仪式过期；重新取选项可通过（PasskeySignInTests） |
| 5 | 不增加 BE 一致性校验：备份状态未参与业务策略，库已校验非法标志组合 | 保持现有协议适配 |
| 6 | 规格以替换 IPasskeyProtocol 适配器作为证明校验扩展点，不加零调用委托 | 人工核对原规格 §4 偏离记录 |
| 7 | 新登记的 Name/DisplayName 加租户限定，沿用身份验证器的账号命名方式 | Given acme 租户，Then 新登记选项带 @acme（PasskeySignInTests） |
| AC7 | 三处通行密钥失败不累加账号登录失败，保留记录与日志；签名不可猜，计数只会误锁本人；第二步挑战仍限 5 次 | Given 阈值 N，When 免口令/第二步/确认身份失败 N 次，Then 不锁定且有失败记录，口令仍可用（PasskeySignInTests、PasskeyReauthenticationTests） |
| 条件式请求 | 4.5 分钟中止并重挂，避免服务端 5 分钟仪式到期后本人再验证一次；不自动弹模态 | 假时钟推进 270 秒后旧 signal 中止、重新取选项；离开后不再刷新（login.spec.ts） |

方案：在现有 Store、应用服务与 Fido2 适配器内就近修改；使用现有租户上下文与部署配置，不新增产品类型、配置项、异常捕获或重试机制。测试先记录基线失败，再同步实现到生成工作区；只跑受影响测试与生成/静态闸门，完整矩阵留给 CI。

## 4. 结果

回执中的后端、前端、词条与文档项已落实；B、C、删除门槛、网关限流和配置间隔仍按裁决只说明取舍，没有增加运行期门槛。证明校验扩展点通过替换适配器表达，原规格 AC7 已同步。

缺陷回归在修复前记录失败，修复后受影响单测、真实 PostgreSQL 集成测试和浏览器模式组件测试通过；四个代表场景生成、单租户后端构建与定向测试、相关静态检查和读取成本检查通过。完整矩阵、完整应用 E2E 与真实认证器验证留待 CI 或后续验收。

常驻文档已同步 `template/docs/standards/auth.md`、`template/docs/deploy/configuration.md`、`template/docs/README.md` 与 `template/backend/README.md`；compose 来源注释同步。本轮没有新增生产类型、配置项或异常捕获。

用户要求保留未提交改动：当前没有 PR、提交或推送，等待 Claude 审查；下游项目及回执输入保持只读。
