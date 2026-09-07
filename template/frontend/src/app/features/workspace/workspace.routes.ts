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
    // 设置页挂在 workspace 而不是 platform：platform 的父路由要求管理类权限
    // （见 PLATFORM_ENTRY_PERMISSIONS），普通登录用户进不去，而账户偏好是每个人的
    // 个人数据。workspace 只要求认证，正合适。「系统」页签仍按 App.Settings 在页面内裁剪。
    path: 'settings',
    loadComponent: () => import('./components/settings/settings').then((m) => m.Settings),
  },
  {
    path: 'placeholder',
    loadComponent: () =>
      import('./components/placeholder/workspace-placeholder').then((m) => m.WorkspacePlaceholder),
  },
];
