import { Routes } from '@angular/router';

/**
 * 平台管理模块路由配置
 * 用于 Default Layout 的子路由
 * 需要超级管理员权限
 */
export const PLATFORM_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () => import('./components/dashboard/dashboard').then(m => m.Dashboard)
  }
//#if (IncludeIdentity)
  ,
  {
    path: 'users',
    loadComponent: () => import('./components/users/users').then(m => m.UsersPage)
  }
//#endif
//#if (IncludeOpenIddict)
  ,
  {
    path: 'open-applications',
    loadComponent: () => import('./components/open-applications/open-applications').then(m => m.OpenApplicationsPage)
  }
//#endif
];
