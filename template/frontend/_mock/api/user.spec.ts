import { USER_API } from './user';
import { MockException } from '../core/models';
import { MockUser, USERS } from '../data/user';
//#if (LocalIdentity)
import { setMockSessionUserId } from '../utils/current-user';
//#endif

type Handler = (req: { params: { id: string } }) => unknown;

function rejectionOf(run: () => unknown): MockException {
  try {
    run();
  } catch (error) {
    expect(error).toBeInstanceOf(MockException);
    return error as MockException;
  }
  throw new Error('expected the handler to reject');
}

/** 用户 Mock 的删除与两步验证重置，状态码与后端一致。 */
describe('user mock', () => {
  let snapshot: MockUser[];

  beforeEach(() => {
    snapshot = USERS.map((user) => ({ ...user }));
  });

  afterEach(() => {
    USERS.length = 0;
    USERS.push(...snapshot);
  });

  describe('delete', () => {
    const remove = USER_API['DELETE /api/v1/users/:id'] as Handler;

    // 删除幂等：重试或并发删除不应报错，前端也不该依赖 404 判断"已删除"
    it('succeeds for a missing user and leaves the others untouched', () => {
      expect(() => remove({ params: { id: 'user_missing' } })).not.toThrow();

      expect(USERS.map((user) => user.id)).toEqual(snapshot.map((user) => user.id));
    });

    it('removes the user, and a repeated delete still succeeds', () => {
      remove({ params: { id: 'user_demo' } });
      expect(USERS.some((user) => user.id === 'user_demo')).toBe(false);

      expect(() => remove({ params: { id: 'user_demo' } })).not.toThrow();
    });

    it('refuses to delete the built-in super administrator with 403', () => {
      const error = rejectionOf(() => remove({ params: { id: 'user_admin' } }));

      expect(error.status).toBe(403);
      expect(error.error.code).toBe('User:SuperAdminDeleteForbidden');
      expect(USERS.some((user) => user.id === 'user_admin')).toBe(true);
    });
  });
  //#if (LocalIdentity)

  describe('reset two-factor', () => {
    const reset = USER_API['POST /api/v1/users/:id/reset-two-factor'] as Handler;

    afterEach(() => setMockSessionUserId(null));

    function enableTwoFactor(id: string): MockUser {
      const user = USERS.find((candidate) => candidate.id === id)!;
      user.twoFactorEnabled = true;
      user.recoveryCodes = ['mock-recovery-1'];
      return user;
    }

    it('returns 404 for a missing user', () => {
      const error = rejectionOf(() => reset({ params: { id: 'user_missing' } }));

      expect(error.status).toBe(404);
      expect(error.error.code).toBe('User:NotFound');
    });

    it('refuses to reset the super administrator for another administrator with 403', () => {
      setMockSessionUserId('user_demo');
      const admin = enableTwoFactor('user_admin');

      const error = rejectionOf(() => reset({ params: { id: 'user_admin' } }));

      expect(error.status).toBe(403);
      expect(error.error.code).toBe('User:SuperAdminOperationForbidden');
      expect(admin.twoFactorEnabled).toBe(true);
    });

    it('turns two-factor off and drops the recovery codes', () => {
      setMockSessionUserId('user_admin');
      const demo = enableTwoFactor('user_demo');

      reset({ params: { id: 'user_demo' } });

      expect(demo.twoFactorEnabled).toBe(false);
      expect(demo.recoveryCodes).toEqual([]);
    });
  });
  //#endif
});
