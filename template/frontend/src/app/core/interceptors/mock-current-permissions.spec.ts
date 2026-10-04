import { AUTHORIZATION_API } from '../../../../_mock/api/authorization';
import { PERMISSION_DEFINITIONS } from '../../../../_mock/data/authorization';
import { setMockSessionUserId } from '../../../../_mock/utils/current-user';
import { PERMISSIONS } from '../../shared/models/permission';

/**
 * 超管在 Mock 下拿到定义树里的全部权限，与真实后端的超管旁路一致。
 * 少一项，对应菜单就不出现、页面进 403，Mock 模式下那一页等于没法用。
 */
describe('current permissions mock', () => {
  afterEach(() => setMockSessionUserId(null));

  function currentPermissions(): string[] {
    setMockSessionUserId('user_admin');
    const result = AUTHORIZATION_API['GET /api/v1/permissions/current']() as {
      permissions: string[];
    };
    return result.permissions;
  }

  it('grants a super admin every permission in the definition tree', () => {
    const defined = PERMISSION_DEFINITIONS.flatMap((group) =>
      group.permissions.flatMap((permission) => [
        permission.name,
        ...permission.children.map((child) => child.name),
      ]),
    );

    expect(currentPermissions().sort()).toEqual(defined.sort());
  });

  it('includes every enabled platform entry', () => {
    const permissions = currentPermissions();
    expect(permissions).toContain(PERMISSIONS.users.default);
    //#if (IncludeOperationRecords)
    expect(permissions).toContain(PERMISSIONS.operationRecords.default);
    //#endif
    //#if (LocalIdentity && IncludeMultiTenancy)
    expect(permissions).toContain(PERMISSIONS.tenants.default);
    //#endif
  });
});
