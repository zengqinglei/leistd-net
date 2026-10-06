// 刻意不 Mock 的端点：浏览器整页跳转或经 <img> 加载、不走 HttpClient 的入口——
// 头像图片 `GET /api/v1/users/{id}/avatar`。
//#if (ExternalLogin)
// 外部登录与绑定的 challenge（`/api/v1/external-auth/{provider}/challenge`、`.../link/challenge`）同理。
//#endif
//#if (RemoteTokenAuth)
// `GET /api/v1/auth/login` 同理（302 到身份服务）；Mock 下改走下面的 `POST` 模拟登录。
//#endif
//#if (LocalIdentity)
export * from './api/auth';
//#endif
export * from './api/user';
export * from './api/authorization';
export * from './api/setting';
//#if (IncludeOperationRecords)
export * from './api/operation-record';
//#endif
//#if (LocalIdentity && IncludeMultiTenancy)
export * from './api/tenant';
//#endif
//#if (OpenIddictServer)
export * from './api/open-application';
//#endif
//#if (IncludeNotifications)
export * from './api/notification';
//#endif
// 确保本文件在任意条件下都是一个有效模块（无认证模块时无 mock API 导出）。
export {};
//#if (RemoteTokenAuth)
export { RESOURCE_AUTH_API } from './api/resource-auth';
//#endif
