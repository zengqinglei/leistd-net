import { inject } from '@angular/core';
import { CanActivateFn, RedirectCommand, Router } from '@angular/router';
import { map } from 'rxjs/operators';

import { AuthorizationService } from '../services/authorization-service';
import { StartupService } from '../services/startup-service';

/**
 * 按功能权限放行路由：`data.permission`（单个）或 `data.permissions`（任一即可），未声明时只要求已认证；
 * 不满足时显示无权限页，地址栏显示被拒的目标 URL（刷新即重新判定）。启动失败时先由
 * {@link StartupService.settled} 拦下，不拿空权限下结论。只读路由自身的 data，不用合并了父路由的
 * `ActivatedRouteSnapshot.data`，否则 `/platform` 的宽松声明会盖住子路由更严格的要求。
 */
export const permissionGuard: CanActivateFn = (route, state) => {
  const authorizationService = inject(AuthorizationService);
  const router = inject(Router);

  return inject(StartupService)
    .settled(state.url)
    .pipe(
      map((started) => {
        if (!started) {
          return false;
        }

        const ownData = route.routeConfig?.data ?? {};
        const required: string[] = ownData['permissions'] ?? [];
        const single: string | undefined = ownData['permission'];
        const permissions = single ? [...required, single] : required;

        if (permissions.length === 0 || authorizationService.hasAny(...permissions)) {
          return true;
        }

        // browserUrl 让地址栏显示被拒的目标：冷启动深链与应用内跳转都一样，刷新即重新判定
        return new RedirectCommand(router.parseUrl('/forbidden'), { browserUrl: state.url });
      }),
    );
};
