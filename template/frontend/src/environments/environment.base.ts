import { MockConfig } from '../../_mock/core/models';

export interface Environment {
  production: boolean;
  //#if (LocalIdentity)
  /**
   * 是否启用哈希路由（`#/path` 形态）。
   *
   * 只在本地身份形态下提供：OIDC 回调地址 `/auth/callback` 是无 fragment 的普通路径，
   * 而哈希路由只从 fragment 读路由，回调组件因此不会被渲染——那个组合运行期不成立，
   * 所以远端令牌形态直接不生成这个开关。
   */
  useHash: boolean;
  //#endif
  /**
   * 是否启用 Mock 服务，仅在非生产环境有效。
   * - `false`: 关闭 Mock
   * - `true`: 开启所有 Mock
   * - `object`: 按模块开启 Mock (特性开关)
   */
  useMock: boolean | MockConfig;
  //#if (RemoteTokenAuth)
  oidc: {
    authority: string;
    clientId: string;
    scope: string;
  };
  //#endif
  api: {
    gateway: string; // 网关地址, 为空则不使用（服务地址为完整地址）
    authService: {
      url: string;
      refreshTokenEnabled?: boolean;
      refreshTokenType?: string;
    };
    appService: {
      url: string;
    };
    envService: {
      url: string;
    };
  };
}

// 这是所有环境共享的基础配置
export const environmentBase: Environment = {
  production: false,
  //#if (LocalIdentity)
  useHash: false,
  //#endif
  useMock: false, // 默认关闭
  //#if (RemoteTokenAuth)
  oidc: {
    authority: 'https://identity.example.com',
    clientId: 'companyname-projectname-web',
    scope: 'openid profile email roles companyname-projectname-api',
  },
  //#endif
  api: {
    gateway: 'https://example.com', // 本地开发的网关地址
    authService: {
      url: '/auth-service',
      refreshTokenEnabled: true,
      refreshTokenType: 'auth-refresh',
    },
    appService: {
      url: '/app-service',
    },
    envService: {
      url: '/env-service',
    },
  },
};
