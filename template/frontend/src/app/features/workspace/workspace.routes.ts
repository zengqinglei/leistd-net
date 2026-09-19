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
    // 个人设置（当前用户自己）挂在 workspace：platform 的父路由要求管理类权限
    // （见 PLATFORM_ENTRY_PERMISSIONS），普通登录用户进不去，而个人设置是每个人的
    // 个人数据，只要求认证即可。系统默认值与策略是另一件事，在 /platform/settings。
    // 面板是子路由：面板名进 URL，刷新、分享与头像菜单直达都落在同一面板。
    path: 'settings',
    loadComponent: () =>
      import('../settings/personal-settings/personal-settings').then((m) => m.PersonalSettings),
    children: [
      //#if (LocalIdentity)
      { path: '', pathMatch: 'full', redirectTo: 'profile' },
      {
        path: 'profile',
        loadComponent: () =>
          import('../account/components/profile-panel/profile-panel').then((m) => m.ProfilePanel),
      },
      {
        path: 'security',
        loadComponent: () =>
          import('../account/components/security-panel/security-panel').then(
            (m) => m.SecurityPanel,
          ),
      },
      //#if (IncludeNotifications)
      {
        // 通知偏好是"类别 × 渠道"的一组开关，单独一个面板，不混进通用偏好
        path: 'notifications',
        loadComponent: () =>
          import('../settings/setting-section/setting-section').then((m) => m.SettingSection),
        data: { scope: 'account', group: 'notifications' },
      },
      //#endif
      //#else
      { path: '', pathMatch: 'full', redirectTo: 'preferences' },
      //#endif
      {
        // 所有允许用户覆盖的设置分组都在这里，新增一项用户级设置会自动出现
        path: 'preferences',
        loadComponent: () =>
          import('../settings/setting-section/setting-section').then((m) => m.SettingSection),
        data: { scope: 'account', exclude: ['Notifications'] },
      },
    ],
  },
  {
    path: 'placeholder',
    loadComponent: () =>
      import('./components/placeholder/workspace-placeholder').then((m) => m.WorkspacePlaceholder),
  },
];
