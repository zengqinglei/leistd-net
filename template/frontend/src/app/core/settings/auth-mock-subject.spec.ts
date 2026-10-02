//#if (LocalIdentity)
import { AUTH_API } from '../../../../_mock/api/auth';
import { MockException } from '../../../../_mock/core/models';
import { USERS } from '../../../../_mock/data/user';
import { setMockSessionUserId } from '../../../../_mock/utils/current-user';

type MockHandler = (req: { body: unknown }) => unknown;

/**
 * Mock 的受保护写端点必须与真后端同口径：无会话即 401。
 *
 * 这两个 handler 曾在匿名时回落到 `USERS[0]` 并"成功"改掉默认用户——真后端在这两个端点上
 * 都是 401。Mock 一旦证明了生产不存在的行为，脱离后端开发出来的前端就会在接真后端时才炸。
 */
describe('mock auth subject', () => {
  const profile = AUTH_API['PUT /api/v1/auth/me'] as MockHandler;
  const changePassword = AUTH_API['POST /api/v1/auth/change-password'] as MockHandler;

  beforeEach(() => setMockSessionUserId(null));

  function expect401(run: () => unknown): void {
    const error = (() => {
      try {
        run();
        return null;
      } catch (e) {
        return e;
      }
    })();

    expect(error).toBeInstanceOf(MockException);
    expect((error as MockException).status).toBe(401);
  }

  it('returns 401 for an anonymous profile update and changes no user', () => {
    const before = USERS.map((u) => ({ ...u }));

    expect401(() => profile({ body: { username: 'hacked', email: 'hacked@test.dev' } }));

    expect(USERS.map((u) => ({ ...u }))).toEqual(before);
  });

  it('returns 401 for an anonymous password change and keeps the old password valid', () => {
    const passwords = USERS.map((u) => u.password);

    expect401(() =>
      changePassword({ body: { currentPassword: 'whatever', newPassword: 'Passw0rd!x' } }),
    );

    expect(USERS.map((u) => u.password)).toEqual(passwords);
  });

  it('also returns 401 when the session user ID matches no mock user', () => {
    setMockSessionUserId('00000000-0000-0000-0000-000000000000');

    expect401(() => profile({ body: { username: 'x', email: 'x@test.dev' } }));
  });
});
//#endif
