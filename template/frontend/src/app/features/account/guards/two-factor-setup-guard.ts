import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { lastValueFrom } from 'rxjs';

import { AuthService } from '../../../core/services/auth-service';

/**
 * 强制两步验证设置页只给受限会话用。`/auth` 下启动不预取当前用户，这里自己取一次：未登录回登录页，
 * 非受限会话直接进工作区。
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
