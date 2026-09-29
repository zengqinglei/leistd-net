import { isMockedUrl, shouldProvideMock } from '../../../../_mock/core/matching';
import * as deployed from '../../../../_mock/core/providers';
import * as development from '../../../../_mock/core/providers.mock';

/**
 * Mock 的两条判定规则只有一份：是否安装 Mock、某个请求是否走 Mock。
 *
 * 登录页的演示账号、实时连接、URL 格式化都按"这个请求是否走 Mock"判定——
 * 各自用总开关近似时，只 Mock 了别的模块也会显示演示账号、断开真实的实时连接。
 */
describe('Mock 判定规则', () => {
  it('总开关与 include 决定是否安装 Mock', () => {
    expect(shouldProvideMock(false)).toBeFalse();
    expect(shouldProvideMock(true)).toBeTrue();
    expect(shouldProvideMock({ enable: false })).toBeFalse();
    expect(shouldProvideMock({ enable: false, include: '^/api/v1/users' })).toBeTrue();
    expect(shouldProvideMock({ enable: false, include: [] })).toBeFalse();
  });

  it('列了 include 时以它为准，总开关不再放行其余请求', () => {
    const config = { enable: true, include: ['^/api/v1/users'] };

    expect(isMockedUrl(config, '/api/v1/users?offset=0')).toBeTrue();
    expect(isMockedUrl(config, '/api/v1/roles')).toBeFalse();
    expect(isMockedUrl({ enable: false, include: '^/api/v1/users' }, '/api/v1/users')).toBeTrue();
  });

  it('没有 include 时按总开关，命中 exclude 的一律走真实后端', () => {
    const config = { enable: true, exclude: ['^/api/v1/settings'] };

    expect(isMockedUrl(config, '/api/v1/users')).toBeTrue();
    expect(isMockedUrl(config, '/api/v1/settings')).toBeFalse();
    expect(isMockedUrl({ enable: false }, '/api/v1/users')).toBeFalse();
    expect(isMockedUrl(true, 'http://localhost:4200/api/v1/users')).toBeTrue();
  });

  it('只 Mock 别的模块时，登录接口与实时连接仍走真实后端', () => {
    const config = { enable: false, include: ['^/api/v1/users', '^/api/v1/notifications'] };

    expect(isMockedUrl(config, '/api/v1/auth/session-login')).toBeFalse();
    expect(isMockedUrl(config, '/hubs/realtime')).toBeFalse();
    expect(isMockedUrl({ enable: false, include: '^/hubs/' }, '/hubs/realtime')).toBeTrue();
  });

  it('部署构建的提供器不含 Mock：不安装，也不把任何请求判为 Mock', () => {
    expect(deployed.provideMock(true)).toEqual([]);
    expect(deployed.mockInterceptors(true)).toEqual([]);
    expect(deployed.isMockedUrl(true, '/api/v1/users')).toBeFalse();
  });

  it('开发构建的提供器按同一规则安装并判定', () => {
    expect(development.mockInterceptors(true).length).toBe(1);
    expect(development.mockInterceptors(false)).toEqual([]);
    expect(development.provideMock({ enable: false })).toEqual([]);
    expect(
      development.isMockedUrl({ enable: false, include: '^/api/v1/users' }, '/api/v1/users'),
    ).toBeTrue();
  });
});
