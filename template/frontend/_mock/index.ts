//#if (LocalIdentity && IncludeMultiTenancy)
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
