import { Routes } from '@angular/router';

/**
 * 公共页面路由配置
 * Landing Layout 的子路由
 */
export const PUBLIC_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () => import('./components/landing/landing').then(m => m.Landing)
  }
];
