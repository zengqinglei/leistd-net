import { inject } from '@angular/core';
import { toObservable } from '@angular/core/rxjs-interop';
import { CanActivateFn, Router } from '@angular/router';
import { filter, map, take } from 'rxjs/operators';

import { AuthorizationService } from '../services/authorization-service';
import { StartupService } from '../services/startup-service';

/**
 * 按功能权限放行路由：`data.permission`（单个）或 `data.permissions`（任一即可），未声明时只要求已认证；
 * 不满足时跳 403 页。只读路由自身的 data，不用合并了父路由的 `ActivatedRouteSnapshot.data`，否则
 * `/platform` 的宽松声明会盖住子路由更严格的要求。
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
