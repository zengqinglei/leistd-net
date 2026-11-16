//#if (RemoteTokenAuth)
import { SystemOutputDto } from '../../src/app/shared/dtos/system.dto';

/** Resource 的上游目录样本；真实运行时由 Identity 的开放应用提供。 */
export const RESOURCE_SYSTEMS: SystemOutputDto[] = [
  {
    key: 'identity',
    displayName: 'Identity',
    homeUrl: 'https://identity.example.com/',
    isCurrent: false,
  },
  {
    key: 'resource',
    displayName: 'Business system',
    homeUrl: `${window.location.origin}/`,
    isCurrent: true,
  },
];
//#endif
