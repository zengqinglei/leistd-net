import { inject } from '@angular/core';
import { toObservable } from '@angular/core/rxjs-interop';
import { CanActivateFn, Router } from '@angular/router';
import { filter, map, take } from 'rxjs/operators';

import { AuthorizationService } from '../services/authorization-service';
import { StartupService } from '../services/startup-service';

/**
 * 按功能权限放行路由。
 *
 * 路由通过 `data.permission`（单个）或 `data.permissions`（拥有任意一个即可）声明所需权限；
 * 未声明时只要求已认证。不满足时跳转 403 页面，与 401 的登录跳转区分开。
 *
 * 只读取路由**自身**声明的 data，不使用 `ActivatedRouteSnapshot.data`：后者会把父路由的
 * data 合并进来，于是 `/platform` 上"拥有任一平台权限即可"的宽松声明会顺着继承链
 * 覆盖子路由更严格的要求，让本该被拦下的页面通过。
 *
 * 前端拦截只影响体验：即使被绕过，服务端仍会对每个请求独立校验。
 */
export const permissionGuard: CanActivateFn = (route) => {
  const authorizationService = inject(AuthorizationService);
  const startupService = inject(StartupService);
  const router = inject(Router);

  return toObservable(startupService.status).pipe(
    filter((status) => status !== 'loading'),
    take(1),
    map(() => {
      const ownData = route.routeConfig?.data ?? {};
      const required: string[] = ownData['permissions'] ?? [];
      const single: string | undefined = ownData['permission'];
      const permissions = single ? [...required, single] : required;

      if (permissions.length === 0 || authorizationService.hasAny(...permissions)) {
        return true;
      }

      return router.parseUrl('/403-forbidden');
    }),
  );
};
