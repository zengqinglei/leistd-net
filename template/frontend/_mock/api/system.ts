//#if (OpenIddictServer || RemoteTokenAuth)
import { MockException, MockRequest } from '../core/models';
//#if (OpenIddictServer)
import { OPEN_APPLICATIONS } from '../data/open-applications';
//#else
import { RESOURCE_SYSTEMS } from '../data/systems';
//#endif
import { getCurrentUser } from '../utils/current-user';

export const SYSTEM_API = {
  'GET /api/v1/systems': (_req: MockRequest) => {
    if (!getCurrentUser()) throw new MockException(401);
    //#if (OpenIddictServer)
    return [
      {
        key: 'identity',
        displayName: 'Identity',
        homeUrl: `${window.location.origin}/`,
        isCurrent: true,
      },
      ...OPEN_APPLICATIONS.filter(
        (app) => app.applicationType === 'web' && app.showInSwitcher && app.homeUrl,
      )
        .map((app) => ({
          key: app.clientId,
          displayName:
            app.displayNames?.[_req.headers.get('Accept-Language')?.split(',')[0] ?? ''] ||
            app.displayName ||
            app.clientId,
          homeUrl: app.homeUrl!,
          isCurrent: false,
        }))
        .sort((a, b) => a.displayName.localeCompare(b.displayName)),
    ];
    //#else
    return RESOURCE_SYSTEMS;
    //#endif
  },
};
//#endif
