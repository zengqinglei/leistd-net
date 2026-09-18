//#if (LocalIdentity)
export * from './api/auth';
//#endif
export * from './api/user';
export * from './api/authorization';
export * from './api/setting';
// 无条件导出：操作记录组件在所有形态下都装配（授权拒绝在任何形态下都要留痕）。
export * from './api/operation-record';
//#if (LocalIdentity)
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
