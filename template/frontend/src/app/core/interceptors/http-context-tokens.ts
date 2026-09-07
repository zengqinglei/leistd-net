import { HttpContextToken } from '@angular/common/http';

export const SILENT_AUTH = new HttpContextToken<boolean>(() => false);

/**
 * 标记该请求取的是**站点自身的静态资源**，不要加网关前缀。
 *
 * `urlFormatInterceptor` 默认把相对 URL 前缀成 `environment.api.gateway`，
 * 那是给业务接口用的；词条这类随前端一起发布的文件必须从站点本身取——
 * 前后端分域部署时前缀过去就是 404，且只在运行期才暴露。
 */
export const SKIP_GATEWAY = new HttpContextToken<boolean>(() => false);
