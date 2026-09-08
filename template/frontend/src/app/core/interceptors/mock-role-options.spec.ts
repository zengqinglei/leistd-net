import { getRoleOptions } from '../../../../_mock/api/authorization';
import { ROLES } from '../../../../_mock/data/authorization';
import { setMockSessionUserId } from '../../../../_mock/utils/current-user';

/**
 * 角色选项 Mock 的排序契约。
 *
 * 不与被测的 `_mock/api/authorization.ts` 同目录：Angular 测试构建器只发现 `src/` 下的
 * spec，放在 `_mock/` 里既不会被执行，还会被算进应用构建。
 */
describe('getRoleOptions', () => {
  const originalOrder = ROLES.map((role) => role.id);
  const temporaryIds = ['role_tmp_zeta', 'role_tmp_alpha'];

  beforeEach(() => {
    setMockSessionUserId('user_admin');

    // 并列排序号不是理论场景：createRole 缺省 sort = 0。
    // 故意按名称倒序插入，插入顺序与期望顺序相反才测得出排序真的发生了
    ROLES.push(
      {
        id: 'role_tmp_zeta',
        name: 'Zeta',
        displayName: 'Zeta',
        description: '',
        isStatic: false,
        isDefault: false,
        sort: 4242,
        creationTime: '2025-01-01T00:00:00Z',
      },
      {
        id: 'role_tmp_alpha',
        name: 'Alpha',
        displayName: 'Alpha',
        description: '',
        isStatic: false,
        isDefault: false,
        sort: 4242,
        creationTime: '2025-01-01T00:00:00Z',
      },
    );
  });

  afterEach(() => {
    for (let index = ROLES.length - 1; index >= 0; index--) {
      if (temporaryIds.includes(ROLES[index].id)) {
        ROLES.splice(index, 1);
      }
    }
    setMockSessionUserId(null);
  });

  it('排序号相同时按名称升序，与后端 GetAllAsync 同口径', () => {
    const tied = getRoleOptions()
      .filter((option) => temporaryIds.includes(option.id))
      .map((option) => option.name);

    expect(tied).toEqual(['Alpha', 'Zeta']);
  });

  it('不改动全局 ROLES 的顺序', () => {
    getRoleOptions();

    expect(ROLES.map((role) => role.id)).toEqual([...originalOrder, ...temporaryIds]);
  });
});
