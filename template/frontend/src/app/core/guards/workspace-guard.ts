//#if (TenantWorkspace)
import { inject } from '@angular/core';
import { CanActivateFn, RedirectCommand, Router } from '@angular/router';
import { toast } from '@spartan-ng/brain/sonner';
import { firstValueFrom } from 'rxjs';

import { applicationErrorMessage } from '../errors/application-http-error';
import { workspaceDenial } from '../errors/workspace-access-error';
import { AuthorizationService } from '../services/authorization-service';
import { StartupService } from '../services/startup-service';
import { WorkspaceContextService } from '../services/workspace-context-service';

const WORKSPACE_DENIED = Symbol('workspace-denied');

export const workspaceDeniedGuard: CanActivateFn = (route) => {
  const router = inject(Router);
  const target = route.queryParamMap.get('target');
  return target && router.currentNavigation()?.extras.info !== WORKSPACE_DENIED
    ? new RedirectCommand(router.parseUrl(target), { browserUrl: undefined, info: undefined })
    : true;
};

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
  let established: boolean;
  try {
    established = await workspace.establish(selected?.name ?? name);
  } catch (error) {
    const reason = workspaceDenial(error);
    if (!reason) throw error;
    if (router.navigated && /^\/(?:w\/|platform(?:\/|$)|workspace(?:\/|$))/.test(router.url)) {
      // 切换未提交时恢复原主体；当前工作区失效则刷新目录并按既有规则退出。
      await workspace.establish(workspace.current());
      toast.error(applicationErrorMessage(error));
      if (reason === 'unavailable') await workspace.recoverUnavailable();
      return false;
    }
    return new RedirectCommand(
      router.createUrlTree(['/workspace-denied'], { queryParams: { target: state.url, reason } }),
      { browserUrl: state.url, info: WORKSPACE_DENIED },
    );
  }
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
