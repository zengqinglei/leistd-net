import { Routes } from '@angular/router';

//#if (IncludeRoles)
import { permissionGuard } from '../../core/guards/permission-guard';
import { PERMISSIONS } from '../../shared/models/permission';
//#endif

/**
 * 平台管理模块路由配置
 * 用于 Default Layout 的子路由
 */
export const PLATFORM_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () => import('./components/dashboard/dashboard').then((m) => m.Dashboard),
  },
  //#if (IncludeIdentity)
  {
    path: 'users',
    loadComponent: () => import('./components/users/users').then((m) => m.Users),
    //#if (IncludeRoles)
    canActivate: [permissionGuard],
    data: { permission: PERMISSIONS.users.default },
    //#endif
  },
  //#endif
  //#if (IncludeRoles)
  {
    path: 'roles',
    loadComponent: () => import('./components/roles/roles').then((m) => m.Roles),
    canActivate: [permissionGuard],
    data: { permission: PERMISSIONS.roles.default },
  },
  //#endif
  //#if (IncludeOpenIddict)
  {
    path: 'open-applications',
    loadComponent: () =>
      import('./components/open-applications/open-applications').then((m) => m.OpenApplications),
  },
  //#endif
];
