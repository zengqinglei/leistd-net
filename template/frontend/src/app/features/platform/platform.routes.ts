import { Routes } from '@angular/router';

import { permissionGuard } from '../../core/guards/permission-guard';
import { PERMISSIONS } from '../../shared/models/permission';

/**
 * 平台管理模块路由配置
 * 用于 Default Layout 的子路由
 */
export const PLATFORM_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () => import('./components/dashboard/dashboard').then((m) => m.Dashboard),
  },
  {
    path: 'users',
    loadComponent: () => import('./components/users/users').then((m) => m.Users),
    canActivate: [permissionGuard],
    data: { permission: PERMISSIONS.users.default },
  },
  {
    path: 'roles',
    loadComponent: () => import('./components/roles/roles').then((m) => m.Roles),
    canActivate: [permissionGuard],
    data: { permission: PERMISSIONS.roles.default },
  },
  {
    // 系统设置在平台侧：读者是管理员，影响本租户（或宿主）下所有人。
    // 一个后端设置分组就是一个面板（:group 是分组标识的短横线写法），面板清单由后端决定；
    // 空路径由外壳在设置取回后导向第一个面板，见 SystemSettings。
    path: 'settings',
    loadComponent: () =>
      import('../settings/system-settings/system-settings').then((m) => m.SystemSettings),
    canActivate: [permissionGuard],
    data: { permission: PERMISSIONS.settings.default },
    children: [
      { path: '', children: [] },
      {
        path: ':group',
        loadComponent: () =>
          import('../settings/setting-section/setting-section').then((m) => m.SettingSection),
        data: { scope: 'system' },
      },
    ],
  },
  {
    // 审计：谁在什么时候做了什么。只读，无写端点。
    path: 'operation-records',
    loadComponent: () =>
      import('./components/operation-records/operation-records').then((m) => m.OperationRecords),
    canActivate: [permissionGuard],
    data: { permission: PERMISSIONS.operationRecords.default },
  },
  //#if (LocalIdentity)
  {
    path: 'tenants',
    loadComponent: () => import('./components/tenants/tenants').then((m) => m.Tenants),
    canActivate: [permissionGuard],
    data: { permission: PERMISSIONS.tenants.default },
  },
  //#endif
  //#if (OpenIddictServer)
  {
    path: 'open-applications',
    loadComponent: () =>
      import('./components/open-applications/open-applications').then((m) => m.OpenApplications),
    canActivate: [permissionGuard],
    data: { permission: PERMISSIONS.openApplications.default },
  },
  //#endif
];
