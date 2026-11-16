//#if (TenantWorkspace)
import { API_ERROR_CODES } from './api-error-codes';
import { ApplicationHttpError } from './application-http-error';

export function workspaceDenial(error: unknown): 'forbidden' | 'unavailable' | null {
  if (!(error instanceof ApplicationHttpError)) return null;
  if (error.code === API_ERROR_CODES.workspaceForbidden) return 'forbidden';
  if (error.code === API_ERROR_CODES.workspaceUnavailable) return 'unavailable';
  return null;
}
//#endif
