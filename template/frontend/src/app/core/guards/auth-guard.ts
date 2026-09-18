import { inject } from '@angular/core';
//#if (LocalIdentity)
import { CanActivateFn, Router } from '@angular/router';
//#else
import { CanActivateFn } from '@angular/router';
//#endif

import { AuthService } from '../services/auth-service';

export const authGuard: CanActivateFn = (_route, state) => {
  const authService = inject(AuthService);
  //#if (LocalIdentity)
  const router = inject(Router);
  //#endif

  if (authService.isAuthenticated()) {
    //#if (LocalIdentity)
    // 受限会话（组织要求两步验证而本人尚未启用）只能去设置页；服务端同样只放行设置所需的接口
    if (authService.currentUser()?.twoFactorSetupRequired) {
      return router.createUrlTree(['/auth/two-factor-setup']);
    }
    //#endif
    return true;
  }

  //#if (!LocalIdentity)
  authService.login(state.url);
  return false;
  //#else
  return router.createUrlTree(['/auth/login'], {
    queryParams: { returnUrl: state.url },
  });
  //#endif
};
