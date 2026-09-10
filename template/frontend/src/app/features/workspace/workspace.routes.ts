import { Routes } from '@angular/router';

export const WORKSPACE_ROUTES: Routes = [
  {
    path: '',
    pathMatch: 'full',
    redirectTo: 'dashboard',
  },
  {
    path: 'dashboard',
    loadComponent: () =>
      import('./components/dashboard/workspace-dashboard').then((m) => m.WorkspaceDashboard),
  },
  {
    // 账户偏好（当前用户自己）挂在 workspace：platform 的父路由要求管理类权限
    // （见 PLATFORM_ENTRY_PERMISSIONS），普通登录用户进不去，而个人偏好是每个人的
    // 个人数据，只要求认证即可。系统默认值是另一件事，在 /platform/settings。
    path: 'settings',
    loadComponent: () => import('../settings/settings').then((m) => m.Settings),
    data: { scope: 'account' },
  },
  {
    path: 'placeholder',
    loadComponent: () =>
      import('./components/placeholder/workspace-placeholder').then((m) => m.WorkspacePlaceholder),
  },
];
