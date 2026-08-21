import { inject } from '@angular/core';
//#if (IdentityService)
import { CanActivateFn, Router } from '@angular/router';
//#else
import { CanActivateFn } from '@angular/router';
//#endif

import { AuthService } from '../services/auth-service';

export const authGuard: CanActivateFn = (_route, state) => {
  const authService = inject(AuthService);
  //#if (IdentityService)
  const router = inject(Router);
  //#endif

  if (authService.isAuthenticated()) {
    return true;
  }

  //#if (ResourceService)
  authService.login(state.url);
  return false;
  //#else
  return router.createUrlTree(['/auth/login'], {
    queryParams: { returnUrl: state.url },
  });
  //#endif
};
