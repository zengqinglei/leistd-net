import { WORKSPACE_ROUTES } from './workspace.routes';

/**
 * 设置页的可达性。
 *
 * 它曾挂在 `/platform` 下，而那个父路由要求 `PLATFORM_ENTRY_PERMISSIONS` 里的管理类权限，
 * 普通登录用户一个都没有——结果是「账户偏好」这类每个人的个人数据反而只有管理员能改，
 * 只授予 App.Settings 的设置管理员同样进不去。用管理账号跑的端到端不会暴露这一点。
 */
describe('workspace routes', () => {
  it('exposes settings without any permission requirement', () => {
    const settings = WORKSPACE_ROUTES.find((route) => route.path === 'settings');

    expect(settings).toBeDefined();
    // workspace 的父路由只有 authGuard；这里再挂权限就把普通用户挡回去了
    expect(settings?.canActivate).toBeUndefined();
    expect(settings?.data?.['permission']).toBeUndefined();
  });
});
