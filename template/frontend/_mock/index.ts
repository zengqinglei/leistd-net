//#if (IncludeIdentity)
export * from './api/auth';
export * from './api/user';
//#endif
//#if (IncludeOpenIddict)
export * from './api/open-application';
//#endif
//#if (IncludeNotifications)
export * from './api/notification';
//#endif

// 确保本文件在任意条件下都是一个有效模块（无认证模块时无 mock API 导出）。
export {};
