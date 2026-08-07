import { Routes } from '@angular/router';

//#if (IncludeIdentity)
import { authGuard } from './core/guards/auth-guard';
//#endif
//#if (IncludeRoles)
import { permissionGuard } from './core/guards/permission-guard';
//#endif
// 布局组件导入
import { DefaultLayout } from './layout/default/default-layout';
//#if (IncludeIdentity)
import { EmptyLayout } from './layout/empty/empty-layout';
//#endif
//#if (IncludeRoles)
import { PERMISSIONS } from './shared/models/permission';
//#endif

export const routes: Routes = [
  // 公开页面
  {
    path: '',
    loadChildren: () => import('./features/public/public.routes').then((r) => r.PUBLIC_ROUTES),
  },

  //#if (IncludeIdentity)
  // Empty Layout - 认证相关页面（登录、注册等）
  {
    path: 'auth',
    component: EmptyLayout,
    loadChildren: () => import('./features/account/account.routes').then((r) => r.AUTH_ROUTES),
  },
  //#endif
  // Default Layout - 用户工作区
  {
    path: 'workspace',
    component: DefaultLayout,
    //#if (IncludeIdentity)
    canActivate: [authGuard],
    //#endif
    loadChildren: () =>
      import('./features/workspace/workspace.routes').then((r) => r.WORKSPACE_ROUTES),
  },

  //#if (IncludeRoles)
  // 已登录但无权限：与 401 的登录跳转区分开，避免"登录成功又被弹回登录页"的循环。
  {
    path: '403-forbidden',
    loadComponent: () =>
      import('./features/public/components/forbidden/forbidden').then((m) => m.Forbidden),
  },
  //#endif

  // Default Layout - 平台管理
  {
    path: 'platform',
    component: DefaultLayout,
    //#if (IncludeIdentity)
    //#if (IncludeRoles)
    // 按权限放行，不按角色名。各子路由再声明各自所需的权限。
    canActivate: [authGuard, permissionGuard],
    data: {
      permissions: [
        PERMISSIONS.users.default,
        PERMISSIONS.roles.default,
        PERMISSIONS.permissions.default,
      ],
    },
    //#else
    canActivate: [authGuard],
    //#endif
    //#endif
    loadChildren: () =>
      import('./features/platform/platform.routes').then((r) => r.PLATFORM_ROUTES),
  },

  // 兜底路由
  { path: '**', redirectTo: '' },
];
