import { APP_BASE_HREF } from '@angular/common';
import { MOCK_PLATFORM_LOCATION_CONFIG } from '@angular/common/testing';
import { inject, provideAppInitializer, provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
//#if (LocalIdentity)
import { Router, provideRouter, withHashLocation } from '@angular/router';
//#else
import { Router, provideRouter } from '@angular/router';
//#endif

import { EntryRouteService } from './entry-route-service';

/**
 * 「当前在哪条路由上」的读法。
 *
 * 这个服务存在的全部理由就是 `Router.url` 不总是准：启动流跑在初始导航之前，那时它一律是 `/`。
 * 所以这里断言的重点是地址栏（TestBed 的地址栏由 `MOCK_PLATFORM_LOCATION_CONFIG` 给出）——
 * 路径与哈希两种路由策略都要覆盖，并且要在应用初始化器里读一次，
 * 否则换回 `Router.url` 或只认其中一种策略都不会有人发现。
 */
describe('EntryRouteService', () => {
  function configure(
    url: string,
    options: { hash?: boolean; baseHref?: string; onInit?: () => void } = {},
  ): void {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        {
          provide: MOCK_PLATFORM_LOCATION_CONFIG,
          useValue: { startUrl: `http://localhost${url}` },
        },
        { provide: APP_BASE_HREF, useValue: options.baseHref ?? '/' },
        //#if (LocalIdentity)
        provideRouter([], ...(options.hash ? [withHashLocation()] : [])),
        //#else
        provideRouter([]),
        //#endif
        ...(options.onInit ? [provideAppInitializer(options.onInit)] : []),
      ],
    });
  }

  function openAt(url: string, options: { hash?: boolean; baseHref?: string } = {}) {
    configure(url, options);
    return TestBed.inject(EntryRouteService);
  }

  describe('with path routing', () => {
    it('reads a deep link inside an app initializer, before the initial navigation', () => {
      const seen: { entry?: string; router?: string } = {};
      configure('/platform/users?page=2', {
        onInit: () => {
          seen.entry = inject(EntryRouteService).url();
          seen.router = inject(Router).url;
        },
      });
      TestBed.inject(EntryRouteService);

      // Router.url 这时还是 '/'：拿它记落地地址，重新登录后回到的是首页而不是这一页。
      expect(seen).toEqual({ entry: '/platform/users?page=2', router: '/' });
    });

    it('keeps the query string in the url and drops it from the path', () => {
      const entry = openAt('/platform/users?page=2');

      expect(entry.url()).toBe('/platform/users?page=2');
      expect(entry.path()).toBe('/platform/users');
    });

    it('strips the base href', () => {
      const entry = openAt('/app/platform/users?page=2', { baseHref: '/app/' });

      expect(entry.url()).toBe('/platform/users?page=2');
    });

    it('reports the site root as a rooted url', () => {
      expect(openAt('/').url()).toBe('/');
    });

    it('keeps the query string on the site root', () => {
      expect(openAt('/?page=2').url()).toBe('/?page=2');
    });

    it('sees the OIDC callback before the router has navigated', () => {
      // 启动阶段 Router.url 就是 '/'：只看它的话，回调页上的 401 会清掉刚建立的主体。
      expect(openAt('/auth/callback?code=abc&state=xyz').isOnAuthRoute()).toBe(true);
    });

    it('reports an ordinary route as not authenticating', () => {
      expect(openAt('/platform/users').isOnAuthRoute()).toBe(false);
    });

    it('ignores a plain anchor', () => {
      const entry = openAt('/platform/users#/auth/callback');

      expect(entry.url()).toBe('/platform/users');
      expect(entry.isOnAuthRoute()).toBe(false);
    });
  });
  //#if (LocalIdentity)

  // 哈希路由只在本地身份形态下可选
  describe('with hash routing', () => {
    it('reads a deep link inside an app initializer, before the initial navigation', () => {
      let seen: string | undefined;
      configure('/#/platform/users?page=2', {
        hash: true,
        onInit: () => {
          seen = inject(EntryRouteService).url();
        },
      });
      TestBed.inject(EntryRouteService);

      expect(seen).toBe('/platform/users?page=2');
    });

    it('sees the OIDC callback', () => {
      expect(openAt('/#/auth/callback?code=abc', { hash: true }).isOnAuthRoute()).toBe(true);
    });

    // 查询串或锚点里出现认证路径，人并不在那条路由上。按整条 URL 做 includes 会误判，
    // 而误判的代价是普通会话过期走进认证流程专用的处置分支。
    it('does not mistake an auth path inside the query string for an auth route', () => {
      const entry = openAt('/#/workspace?returnUrl=/auth/callback', { hash: true });

      expect(entry.path()).toBe('/workspace');
      expect(entry.isOnAuthRoute()).toBe(false);
    });

    it('does not mistake a hash without a leading slash for an auth route', () => {
      expect(openAt('/#section/auth/callback', { hash: true }).isOnAuthRoute()).toBe(false);
    });
  });
  //#endif
});
