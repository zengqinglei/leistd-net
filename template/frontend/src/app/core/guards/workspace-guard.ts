//#if (TenantWorkspace)
import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { AuthorizationService } from '../services/authorization-service';
import { StartupService } from '../services/startup-service';
import { WorkspaceContextService } from '../services/workspace-context-service';

export const workspaceGuard: CanActivateFn = async (_route, state) => {
  const startup = inject(StartupService);
  const authorization = inject(AuthorizationService);
  const workspace = inject(WorkspaceContextService);
  const router = inject(Router);
  if (!(await firstValueFrom(startup.settled(state.url)))) return false;
  await workspace.refresh();
  const name = workspace.nameFromUrl(state.url);
  if (!name && workspace.auth.currentUser()?.tenantId) {
    const own = workspace.workspaces()[0];
    return own ? router.parseUrl(`/w/${encodeURIComponent(own.name)}${state.url}`) : false;
  }
  const selected = workspace
    .workspaces()
    .find((item) => item.name.toLowerCase() === name?.toLowerCase());
  if (name && !selected) return router.parseUrl('/platform');
  const established = await workspace.establish(selected?.name ?? null);
  const root = selected ? `/w/${encodeURIComponent(selected.name)}/platform` : null;
  if (
    established &&
    root &&
    authorization.permissions().length === 0 &&
    state.url.split(/[?#]/)[0] !== root
  )
    return router.parseUrl(root);
  return established;
};
//#endif
