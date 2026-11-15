//#if (TenantWorkspace)
import { MockRequest } from './models';

export let requestWorkspace: string | null = null;

/** Mock 处理器同步读取请求范围，不把当前工作区写进浏览器会话。 */
export function withinWorkspace<T>(request: MockRequest, action: () => T): T {
  const previous = requestWorkspace;
  requestWorkspace = /\/(?:auth|external-auth)(?:\/|$)|\/connect\//.test(request.url)
    ? null
    : request.headers.get('X-Workspace');
  try {
    return action();
  } finally {
    requestWorkspace = previous;
  }
}
//#endif
