import { Routes } from '@angular/router';

import { authGuard } from './core/guards/auth-guard';
import { permissionGuard } from './core/guards/permission-guard';
// 布局组件导入
import { DefaultLayout } from './layout/default/default-layout';
//#if (LocalIdentity)
import { EmptyLayout } from './layout/empty/empty-layout';
//#endif
import { PLATFORM_ENTRY_PERMISSIONS } from './shared/models/permission';

export const routes: Routes = [
  // 公开页面
  {
    path: '',
    loadChildren: () => import('./features/public/public.routes').then((r) => r.PUBLIC_ROUTES),
  },

  //#if (LocalIdentity)
  // Empty Layout - 认证相关页面（登录、注册等）
  {
    path: 'auth',
    component: EmptyLayout,
    loadChildren: () => import('./features/account/account.routes').then((r) => r.AUTH_ROUTES),
  },
  //#endif
  //#if (!LocalIdentity)
  {
    path: 'auth/callback',
    loadComponent: () =>
      import('./core/components/oidc-callback/oidc-callback').then((m) => m.OidcCallback),
  },
  //#endif
  // Default Layout - 用户工作区
  {
    path: 'workspace',
    component: DefaultLayout,
    canActivate: [authGuard],
    loadChildren: () =>
      import('./features/workspace/workspace.routes').then((r) => r.WORKSPACE_ROUTES),
  },
  // 已登录但无权限：与 401 的登录跳转区分开，避免"登录成功又被弹回登录页"的循环。
  {
    path: '403-forbidden',
    loadComponent: () =>
      import('./features/public/components/forbidden/forbidden').then((m) => m.Forbidden),
  },

  // Default Layout - 平台管理
  {
    path: 'platform',
    component: DefaultLayout,
    // 按权限放行，不按角色名。各子路由再声明各自所需的权限。
    canActivate: [authGuard, permissionGuard],
    data: {
      // 引用单一来源，不要在这里手写清单；缘由见 PLATFORM_ENTRY_PERMISSIONS 的注释
      permissions: [...PLATFORM_ENTRY_PERMISSIONS],
    },
    loadChildren: () =>
      import('./features/platform/platform.routes').then((r) => r.PLATFORM_ROUTES),
  },

  // 兜底路由
  { path: '**', redirectTo: '' },
];
