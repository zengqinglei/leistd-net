import { environmentBase, Environment } from './environment.base';

/**
 * 本机开发环境（`npm start` 即 `ng serve`）。生产构建经 angular.json 的 fileReplacements 换成 `environment.prod.ts`。
 *
 * 网关沿用基础配置的空值：请求保持相对路径，由开发服务器按 proxy.conf.mjs 转发给本机后端，与后端同源，不需要跨域配置。
 */
export const environment: Environment = {
  ...environmentBase,
};
