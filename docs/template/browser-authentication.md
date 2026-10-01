# 模板浏览器认证维护规则

模板组合根直接组合 ASP.NET Core 官方 Cookie、AddOpenIdConnect、AddGoogle/AddOAuth；框架不新增 Web/BFF 抽象，不引入反向代理。OpenIddict.Client 继续承担框架的机器认证。前端保留 Angular 页面、开发代理与同源 Cookie 会话认证。

浏览器 OAuth access/refresh/id token 不得出现在 URL、JavaScript 存储、API JSON、SignalR 参数或保护后的 Cookie 载荷中。SaveTokens 与 ITicketStore 必须一起配置；保护后的 Cookie 仅含会话引用与引用版本，完整票据留缓存；删除缓存后复制 Cookie 返回 401。显式再登录、MFA 会话升级与冒用切换要保持旧 Cookie 失效；滑动续期不能复活撤销票据，旧请求退出或刷新失败不能删除再次登录的新版本。

应用 SameSite 与协议 correlation/nonce Cookie 分开。官方默认协议 Cookie 的 None/Secure Always 保持不变，OIDC code 显式指定，GitHub OAuthOptions.UsePkce 显式 true。回调须在 /api/** 下以覆盖开发代理。Items 提供完整性，业务 complete 的一次消费由服务端外部票据、锁与先删除保证。

Resource 的 Smart policy scheme 以 Authorization 头是否存在确定 Bearer/Cookie，DefaultPolicy 与当前用户策略都使用 Smart；验证失败不尝试其他方案。模板的 IUserAccessTokenAccessor 读经过验证的 Bearer 或 Cookie 服务端票据，不改框架现有 Bearer-only 实现。刷新使用公开 OnValidatePrincipal、官方访问令牌验证服务与已有分布式锁。

OAuth:ApiResources 是对象目录，资源名、scope、OwnerClientId 单一来源，scope/owner 默认资源名。Scope 与 audience 可不同、同一客户端可拥有多个资源、同一资源只有一个归属；启动期拒绝重复资源/Scope。用户访问令牌的 Token Exchange 只允许单跳，目标为一个资源与 scope，发起方必须拥有来源资源，目标 audience/scope 与登记权限对应。OpenIddict 7.7 ValidateAuthorizedParty 直接看主体 aud，ValidAudiences 不改其逻辑；模板只对 access-token exchange 替换这一处理器，其余分支委托官方实现，不能全局关闭验证。

协议测试仅替换通信依赖，运行生产注册的官方处理器与失败事件；业务测试可直接输入规范化 ExternalUserInfo。提供商以 `AuthenticationSchemeNames.ExternalProviderPrefix` 登记官方远程 scheme，目录过滤此专用前缀与远程处理器类型；绑定入口执行自然人策略与受限会话规则。站内回跳地址由 challenge 校验后随受保护票据保存，并贯穿外部登录和第二步验证。

浏览器认证仅支持页面与所属 API 同源，服务可分进程、由部署代理统一外部源。TLS 终结后的来源检查依赖受信任转发头；来源拒绝输出诊断日志并由 API 状态码管道补写问题详情。有效票据远离刷新窗口时不获取刷新锁；临近过期才加锁、重读并复查。

生成项目的运行契约见 template/docs/standards/api.md#浏览器认证；仓库验收日志和过程放 .tmp，不分发进 template。
