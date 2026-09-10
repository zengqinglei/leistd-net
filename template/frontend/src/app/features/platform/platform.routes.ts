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
    // 系统默认值（租户级）在平台侧：读者是管理员，影响整租户。
    // 与账户偏好（/workspace/settings）同一个组件，作用域由 data.scope 决定。
    path: 'settings',
    loadComponent: () => import('../settings/settings').then((m) => m.Settings),
    canActivate: [permissionGuard],
    data: { permission: PERMISSIONS.settings.default, scope: 'system' },
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
