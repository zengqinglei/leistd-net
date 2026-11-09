# 机密设置的专用 Data Protection

状态：已完成 · 风险档：中 · 基线：develop `c2be339f` · 关联：[#253](https://github.com/zengqinglei/leistd-net/pull/253)

## 1. 需求

下游项目有两个应用名不同的宿主：员工端写租户设置，客户端读。人机验证的 secret 作为租户级机密设置，客户端要能解密。两宿主的 Data Protection 密钥环有意隔离，以免 Cookie、防伪令牌等会话类载荷互相解得开。现状是机密设置只能用宿主容器里的 `IDataProtectionProvider` 加解密，因此只能被应用名与密钥环相同的进程读取。

目标：机密设置可以由多个应用名不同的宿主共用，宿主自己的会话类密钥环保持隔离。默认行为与已存密文不变。

非目标：
- 不改用途链。业务侧建议在用途里加租户，防止跨租户复制密文；见 §2 取舍。
- 不改多租户连接串、Webhook 签名密钥的加密方式。

验收：
- Given 写入宿主与读取宿主的密钥环不同，并都把 `SettingEncryptionOptions.DataProtectionProvider` 设为同一密钥目录、同一应用名的实例；When 一方写入租户级机密设置，另一方读取；Then 读到明文。没设专用实例的读取端解不开，抛 `InvalidOperationException`。由 `SettingEncryptionTests.Hosts_with_separate_key_rings_share_secrets_through_a_dedicated_data_protection` 覆盖。
- Given 经 `AddSettingsCore` 注册、用 `services.Configure<SettingEncryptionOptions>` 设了专用实例；When 从容器解析 `ISettingProvider` 读取；Then 用的是专用实例。由 `The_dedicated_data_protection_is_picked_up_from_the_container` 覆盖。
- 不设选项时行为不变：现有 `SettingEncryptionTests` 全部通过。

## 2. 方案

现状：
- `Leistd.Settings.Core/Resolution/DefaultSettingProvider.cs` 与 `Management/DefaultSettingManager.cs` 各自注入可空的 `IDataProtectionProvider`。保护器的创建方式是 `CreateProtector("Leistd.Settings.EncryptedValue.v1")`，再以设置名作子用途。
- 框架规范 `docs/framework/development-guide.md` §6.1 末条规定：需要可还原加密时直接用宿主的 Data Protection，不另立加密接口；换密钥设施在 Data Protection 这一层换。

设计：
- 新增 `Options/SettingEncryptionOptions`，只有一个属性 `IDataProtectionProvider? DataProtectionProvider`。设了就用它，不设时用容器里宿主的实例。
- 两个默认实现的构造函数末尾各追加可选参数 `IOptions<SettingEncryptionOptions>? encryptionOptions`。保护器统一由内部的 `DefaultSettingProvider.CreateProtector` 取得，读写两端判定一致。
- 用途字符串不变，已存密文照常可解。
- 专用实例用官方 `DataProtectionProvider.Create` 创建。多宿主要用同一密钥存储、同一应用名，并按官方建议持久化存储、证书加密、不删除密钥。

备选与取舍：
- **独立的"设置值保护器"接口（业务侧建议 1）**：不采纳。它违反 §6.1，也不是需求本身需要的：需求要换的是哪一个 Data Protection 实例，不是加密机制。官方对同类需求的做法正是给组件选项一个 `DataProtectionProvider` 属性，见 `CookieAuthenticationOptions.DataProtectionProvider` 与官方 Cookie 共享文档。
- **合并两宿主的应用名与密钥环**：业务侧已否决，因为这会扩大会话类载荷的互读面。
- **用途链加入租户（业务侧建议 2）**：不采纳。
  - 收益：防"有数据库写权限的人把 A 租户的密文复制到 B 租户的行"。复制后界面不回显明文，但 B 租户的业务逻辑会解出并使用这个值（例如换掉 B 的第三方验证凭据），属完整性风险。前提是攻击者已能直接写库，这时他也能直接改其他业务数据。
  - 代价：改用途会让 v1.2.3 起已发布项目的已存密文全部解不开，要么保留双用途回退，要么做数据迁移。
  - 结论：按"不过度考虑安全"的取舍原则，现在不做。
- **把各组件的长期密文（连接串、Webhook 密钥）统一改走一个"长期密文"实例**：需求只涉及设置，暂不做，留作候选。

兼容与回滚：
- 源码兼容：不设选项时行为不变，已存密文照常可解。
- 二进制不兼容：`DefaultSettingProvider`、`DefaultSettingManager` 的公开构造函数末尾追加了可选参数，CLR 签名随之改变，已编译的、直接调用旧构造函数的程序集要重新编译（否则报 `MissingMethodException`）。按仓库口径以 `BREAKING CHANGE:` 脚注说明，不保留兼容重载。
- 切换与回滚都要协调所有读写端，并重新写入已有机密：换了实例后，原实例加密的值解不开。

## 3. 计划

- 源码：新增 `SettingEncryptionOptions`；改两个默认实现的构造函数、`AddSettingsCore` 的选项登记，以及 XML 注释与错误消息。验证：单测。
- 测试：在 `SettingEncryptionTests` 增加两个用例，并做变异核对：忽略选项时两个用例都失败。
- 文档：
  - `framework/docs/components/settings.md`：在接口参考与注意事项里补专用实例的写法与密钥运维要求；第三方凭据的口径扩到租户级。
  - `template/docs/standards/settings.md` §1：同步租户级凭据的口径。
- 不做：用途链改动、其他组件的加密方式调整、模板代码改动（模板是单宿主，用宿主密钥环即可）。

## 4. 结果

- 交付：PR #253，合并提交 `9b19c508`，发布 `v2.0.0-beta.265`。
- 与计划的偏差：Codex 第 1 轮审查指出，公开构造函数追加参数会改变 CLR 签名。因此提交改为 `feat(settings)!:`，并加 `BREAKING CHANGE:` 脚注；同时修正了注册入口注释与跨租户复制的论证。第 2 轮审查通过。
- 验收：
  - 设置测试项目 178/178 通过；两个新用例经变异核对，忽略选项时会失败。
  - 仓库静态检查通过；打包后的 XML 与随包文档都含新选项。
  - PR CI 全绿。
- 遗留项：
  - 真实多进程部署与已有机密的切换迁移未做端到端验证，由下游接入时验证。
  - 把多租户连接串、Webhook 密钥统一改走长期密文实例，仍是候选，未立项。
- 上收到的常驻文档：`framework/docs/components/settings.md`（接口参考、注意事项）、`template/docs/standards/settings.md` §1。
