import { environmentBase, Environment } from './environment.base';

export const environment: Environment = {
  ...environmentBase,
  useMock: {
    enable: true,
    delay: 300,
    log: true
  },
  api: {
    ...environmentBase.api,
    gateway: ''
  }
};
