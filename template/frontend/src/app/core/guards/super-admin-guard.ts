import { inject } from '@angular/core';
import { toObservable } from '@angular/core/rxjs-interop';
import { CanActivateFn, Router } from '@angular/router';
import { filter, map, take } from 'rxjs/operators';

import { AuthService } from '../services/auth-service';
import { StartupService } from '../services/startup-service';

export const superAdminGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const startupService = inject(StartupService);
  const router = inject(Router);

  return toObservable(startupService.status).pipe(
    filter(status => status !== 'loading'),
    take(1),
    map(() => (authService.currentUser()?.isSuperAdmin ? true : router.parseUrl('/workspace')))
  );
};
