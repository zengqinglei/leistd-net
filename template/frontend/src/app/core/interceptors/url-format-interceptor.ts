import {
  HttpContextToken,
  HttpEvent,
  HttpHandlerFn,
  HttpInterceptorFn,
  HttpRequest,
} from '@angular/common/http';
import { Observable } from 'rxjs';

import { SKIP_GATEWAY } from './http-context-tokens';
import { isMockedUrl } from '../../../../_mock/core/providers';
import { environment } from '../../../environments/environment';

/**
 * 定义一个上下文令牌，用于在请求中标记是是否传递服务在网关中的名字。
 */
export const GATEWAY_SERVICE_NAME = new HttpContextToken<string>(() => '');

/**
 * URL格式化拦截器。
 * 为本服务的相对地址加上网关与微服务前缀，并携带凭据（Cookie 会话）。
 *
 * 调用方本来就给了绝对地址的请求（如 OIDC 签发方的发现文档、JWKS、令牌端点）原样放行、不带凭据：
 * 它们在另一个源上，也不需要 Cookie；带凭据的跨源请求还要求对方显式放行凭据，否则会被浏览器拦下。
 */
export const urlFormatInterceptor: HttpInterceptorFn = (
  req: HttpRequest<unknown>,
  next: HttpHandlerFn,
): Observable<HttpEvent<unknown>> => {
  const url = req.url;

  if (req.context.get(SKIP_GATEWAY) || isAbsoluteUrl(url) || shouldSkipUrlFormat(url)) {
    return next(req);
  }

  const gateway = environment.api.gateway || '';
  const gatewayServiceName = req.context.get(GATEWAY_SERVICE_NAME) || '';

  const pathSegments = [];
  const gatewayPart = gateway.endsWith('/') ? gateway.slice(0, -1) : gateway;
  if (gatewayPart) {
    pathSegments.push(gatewayPart);
  }
  const servicePart = gatewayServiceName.startsWith('/')
    ? gatewayServiceName.slice(1)
    : gatewayServiceName;
  if (servicePart) {
    pathSegments.push(servicePart);
  }
  pathSegments.push(url.startsWith('/') ? url.slice(1) : url);

  return next(req.clone({ url: pathSegments.join('/'), withCredentials: true }));
};

function isAbsoluteUrl(url: string): boolean {
  return /^https?:\/\//i.test(url);
}

// 由 Mock 应答的请求保持原样：Mock 按路径匹配，不加网关与服务前缀
function shouldSkipUrlFormat(url: string): boolean {
  return isMockedUrl(environment.useMock, url);
}
