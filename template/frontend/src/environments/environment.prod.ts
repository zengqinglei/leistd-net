import { environmentBase, Environment } from './environment.base';

// 支持构建时环境变量注入 API Gateway 地址
// 占位符会在构建时被替换为实际值
// 使用方式: API_GATEWAY=https://api.example.com npm run build
const apiGateway = '__API_GATEWAY__';

export const environment: Environment = {
  ...environmentBase,
  production: true,
  // 生产构建另经 fileReplacements 移除 Mock 代码与数据（见 angular.json）
  useMock: {
    enable: false,
    delay: 0,
    log: false,
  },
  api: {
    ...environmentBase.api,
    gateway: apiGateway === '__API_GATEWAY__' ? '' : apiGateway,
  },
};
