import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { lastValueFrom } from 'rxjs';

import { AuthService } from '../../../core/services/auth-service';

/**
 * 强制两步验证设置页只给受限会话用。
 *
 * 这个页面挂在 `/auth` 下，启动时不会预先取当前用户（那只对受保护区域做），所以这里自己取一次：
 * 取不到（未登录）回登录页；不是受限会话（已设置过、或组织没要求）直接进工作区。
 */
export const twoFactorSetupGuard: CanActivateFn = async () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (!authService.isAuthenticated()) {
    try {
      await lastValueFrom(authService.loadUser());
    } catch {
      return router.createUrlTree(['/auth/login']);
    }
  }

  return authService.currentUser()?.twoFactorSetupRequired
    ? true
    : router.createUrlTree(['/workspace']);
};
