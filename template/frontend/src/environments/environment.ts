import { environmentBase, Environment } from './environment.base';

/**
 * 本机开发环境（`npm start` 即 `ng serve`）。部署构建经 angular.json 的 fileReplacements 换成各自的环境文件。
 *
 * 网关留空：请求保持相对路径，由开发服务器按 proxy.conf.mjs 转发给本机后端，与后端同源，不需要跨域配置。
 */
export const environment: Environment = {
  ...environmentBase,
  api: {
    ...environmentBase.api,
    gateway: '',
  },
};
