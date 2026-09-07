import { entryRoutePath, entryRouteUrl, isOnAuthRoute } from './entry-route';

/**
 * 「当前在哪条路由上」的读法。
 *
 * 这组函数存在的全部理由就是 `Router.url` 不总是准：启动流跑在初始导航之前，
 * 那时它一律是 `/`；哈希路由下真实路径又落在 `location.hash` 里。所以这里断言的重点
 * 是地址栏——深链与哈希两种入口都要覆盖，否则把地址栏那半边整个删掉也不会有人发现。
 */
describe('entryRouteUrl', () => {
  afterEach(() => history.replaceState(null, '', '/context.html'));

  it('reads a deep link before the router has navigated', () => {
    history.replaceState(null, '', '/platform/users');

    expect(entryRouteUrl()).toBe('/platform/users');
  });

  it('keeps the query string', () => {
    history.replaceState(null, '', '/platform/users?page=2');

    // 落地地址丢了查询串，用户重新登录后回到的是列表首页而不是他原来那一页。
    expect(entryRouteUrl()).toBe('/platform/users?page=2');
  });

  it('reads the route from the hash under hash routing', () => {
    history.replaceState(null, '', '/#/platform/users?page=2');

    expect(entryRouteUrl()).toBe('/platform/users?page=2');
  });

  it('falls back to the pathname when the hash is a plain anchor', () => {
    history.replaceState(null, '', '/platform/users#section');

    // 锚点不是路由：只有 `#/` 开头才是哈希路由的路径。
    expect(entryRouteUrl()).toBe('/platform/users');
  });
});

describe('entryRoutePath', () => {
  afterEach(() => history.replaceState(null, '', '/context.html'));

  it('drops the query string', () => {
    history.replaceState(null, '', '/platform/users?page=2');

    expect(entryRoutePath()).toBe('/platform/users');
  });

  it('drops the query string under hash routing', () => {
    history.replaceState(null, '', '/#/platform/users?page=2');

    expect(entryRoutePath()).toBe('/platform/users');
  });
});

describe('isOnAuthRoute', () => {
  afterEach(() => history.replaceState(null, '', '/context.html'));

  it('sees the OIDC callback before the router has navigated', () => {
    history.replaceState(null, '', '/auth/callback?code=abc&state=xyz');

    // 启动阶段 Router.url 就是 '/'：只看它的话，回调页上的 401 会清掉刚建立的主体。
    expect(isOnAuthRoute()).toBeTrue();
  });

  it('sees the OIDC callback under hash routing', () => {
    history.replaceState(null, '', '/#/auth/callback?code=abc');

    expect(isOnAuthRoute()).toBeTrue();
  });

  it('reports an ordinary route as not authenticating', () => {
    history.replaceState(null, '', '/platform/users');

    expect(isOnAuthRoute()).toBeFalse();
  });

  // 查询串或锚点里出现认证路径，人并不在那条路由上。按整条 URL 做 includes 会误判，
  // 而误判的代价是普通会话过期走进认证流程专用的处置分支。
  it('does not mistake an auth path inside the query string for an auth route', () => {
    history.replaceState(null, '', '/#/workspace?returnUrl=/auth/callback');

    expect(isOnAuthRoute()).toBeFalse();
  });

  it('does not mistake an auth path inside a plain anchor for an auth route', () => {
    history.replaceState(null, '', '/#section/auth/callback');

    expect(isOnAuthRoute()).toBeFalse();
  });
});
