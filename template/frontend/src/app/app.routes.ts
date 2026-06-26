import { Routes } from '@angular/router';

//#if (IncludeIdentity)
import { authGuard } from './core/guards/auth-guard';
import { roleGuard } from './core/guards/role-guard';
//#endif
// 布局组件导入
import { DefaultLayout } from './layout/default/default-layout';
//#if (IncludeIdentity)
import { EmptyLayout } from './layout/empty/empty-layout';
//#endif

export const routes: Routes = [
  // 公开页面
  {
    path: '',
    loadChildren: () => import('./features/public/public.routes').then(r => r.PUBLIC_ROUTES)
  },

//#if (IncludeIdentity)
  // Empty Layout - 认证相关页面（登录、注册等）
  {
    path: 'auth',
    component: EmptyLayout,
    loadChildren: () => import('./features/account/account.routes').then(r => r.AUTH_ROUTES)
  },
//#endif

  // Default Layout - 用户工作区
  {
    path: 'workspace',
    component: DefaultLayout,
//#if (IncludeIdentity)
    canActivate: [authGuard],
//#endif
    loadChildren: () => import('./features/workspace/workspace.routes').then(r => r.WORKSPACE_ROUTES)
  },

  // Default Layout - 平台管理
  {
    path: 'platform',
    component: DefaultLayout,
//#if (IncludeIdentity)
    canActivate: [authGuard, roleGuard],
    data: { role: 'Admin' },
//#endif
    loadChildren: () => import('./features/platform/platform.routes').then(r => r.PLATFORM_ROUTES)
  },

  // 兜底路由
  { path: '**', redirectTo: '' }
];
