import { PagedResultDto } from '../../src/app/shared/dtos/paged-result.dto';
import { MockException, MockRequest } from '../core/models';
import { parseMockSorting } from '../core/sorting';
//#if (LocalIdentity)
import { ROLES } from '../data/authorization';
import { ensureAcceptablePassword } from '../data/password-policy';
//#endif
// prettier-ignore
import {
  //#if (LocalIdentity)
  DELETED_USERS,
  //#endif
  USERS,
  //#if (LocalIdentity)
  isEmailTaken,
  isUsernameTaken,
  //#endif
  toUserManagementOutput,
} from '../data/user';
//#if (LocalIdentity)
import { getCurrentUser } from '../utils/current-user';
//#endif

function getQueryValue(value: unknown) {
  const normalized = Array.isArray(value) ? value[0] : value;
  return normalized === undefined || normalized === null || normalized === ''
    ? undefined
    : String(normalized);
}

/** 与后端 `UserAppService.ApplySorting` 同一份字段清单。 */
//#if (LocalIdentity)
const USER_SORT_FIELDS = ['username', 'email', 'lastLoginTime', 'creationTime'] as const;
//#else
const USER_SORT_FIELDS = ['username', 'email', 'creationTime'] as const;
//#endif

function sortUsers(users: typeof USERS, sorting?: string) {
  const { field, descending } = parseMockSorting(sorting, USER_SORT_FIELDS, 'username');
  const order = descending ? -1 : 1;

  return users.sort((a, b) => {
    const left = a[field as keyof typeof a];
    const right = b[field as keyof typeof b];
    const compared = String(left ?? '').localeCompare(String(right ?? '')) * order;
    // 与后端一样固定追加 id 作为稳定次序，且不随方向反转：否则排序键有并列值时，
    // 翻页会重复或漏掉同一行
    return compared !== 0 ? compared : a.id.localeCompare(b.id);
  });
}

export function getUsers(params: any): PagedResultDto<any> {
  let users = [...USERS];
  const offset = +(getQueryValue(params.offset) ?? 0);
  const limit = +(getQueryValue(params.limit) ?? 10);
  const keyword = getQueryValue(params.keyword)?.toLowerCase();
  const isActive = getQueryValue(params.isActive);
  const isEmailVerified = getQueryValue(params.isEmailVerified);
  const rolesParam = params.roles;
  const roles: string[] = Array.isArray(rolesParam) ? rolesParam : rolesParam ? [rolesParam] : [];
  const sorting = getQueryValue(params.sorting);

  if (keyword) {
    users = users.filter(
      (user) =>
        user.username.toLowerCase().includes(keyword) ||
        user.email.toLowerCase().includes(keyword) ||
        user.displayName?.toLowerCase().includes(keyword),
    );
  }

  if (isActive !== undefined) {
    users = users.filter((user) => user.isActive === (isActive === 'true'));
  }

  if (isEmailVerified !== undefined) {
    users = users.filter((user) => user.isEmailVerified === (isEmailVerified === 'true'));
  }

  if (roles.length) {
    users = users.filter((user) => user.roles.some((r) => roles.includes(r)));
  }

  users = sortUsers(users, sorting);

  return {
    totalCount: users.length,
    items: users.slice(offset, offset + limit).map(toUserManagementOutput),
  };
}

export function getUserById(id: string) {
  const user = USERS.find((w) => w.id === id);
  if (!user) {
    throw new MockException(404, { code: 'User:NotFound', message: 'User not found' });
  }
  return toUserManagementOutput(user);
}

//#if (LocalIdentity)
export function addUser(value: any) {
  const username = String(value.username ?? '').trim();
  const email = String(value.email ?? '').trim();
  // 与后端一致：已删除用户仍占着用户名与邮箱
  if (isUsernameTaken(username)) {
    throw new MockException(409, {
      code: 'User:UsernameTaken',
      message: `Username '${username}' already exists.`,
    });
  }
  if (isEmailTaken(email)) {
    throw new MockException(409, {
      code: 'User:EmailTaken',
      message: `Email '${email}' is already in use.`,
    });
  }
  // 复刻后端口令策略：创建用户必须显式给出合规口令，没有默认值。
  ensureAcceptablePassword(value.password, 'Password');

  const newUser = {
    id: crypto.randomUUID(),
    username,
    email,
    displayName: value.displayName,
    avatar:
      value.avatar ||
      `https://api.dicebear.com/7.x/avataaars/svg?seed=${encodeURIComponent(username)}`,
    phoneNumber: value.phoneNumber,
    isActive: value.isActive ?? true,
    isSuperAdmin: false,
    isEmailVerified: value.isEmailVerified ?? false,
    creationTime: new Date().toISOString(),
    // 创建时按 Id 提交角色；未指定则落到默认角色，与后端一致。
    roles: (value.roleIds?.length
      ? ROLES.filter((role: { id: string }) => value.roleIds.includes(role.id)).map(
          (role: { name: string }) => role.name,
        )
      : ROLES.filter((role: { isDefault: boolean }) => role.isDefault).map(
          (role: { name: string }) => role.name,
        )) as string[],
    password: value.password,
  };
  USERS.push(newUser);
  return toUserManagementOutput(newUser);
}

export function updateUser(id: string, value: any) {
  const user = USERS.find((w) => w.id === id);
  if (!user) {
    throw new MockException(404, {
      code: 'User:NotFound',
      message: 'User does not exist or has been deleted',
    });
  }
  // 与后端一致：邮箱先去掉首尾空白再判占用与保存；被其他用户占用时 409，不静默覆盖
  const email = String(value.email ?? '').trim();
  if (email && isEmailTaken(email, id)) {
    throw new MockException(409, {
      code: 'User:EmailTaken',
      message: `Email '${email}' is already in use.`,
    });
  }

  Object.assign(user, {
    email,
    displayName: value.displayName,
    avatar: value.avatar,
    isActive: value.isActive,
    isEmailVerified: value.isEmailVerified,
    // 普通更新不接受角色：角色分配是独立命令，走 PUT /api/v1/users/:id/roles。
  });
  return toUserManagementOutput(user);
}

//#endif
export function enableUser(id: string) {
  const user = USERS.find((w) => w.id === id);
  if (!user) {
    throw new MockException(404, {
      code: 'User:NotFound',
      message: 'User does not exist or has been deleted',
    });
  }
  user.isActive = true;
}

export function disableUser(id: string) {
  const user = USERS.find((w) => w.id === id);
  if (!user) {
    throw new MockException(404, {
      code: 'User:NotFound',
      message: 'User does not exist or has been deleted',
    });
  }
  user.isActive = false;
}

//#if (LocalIdentity)
export function resetPassword(id: string, value: any) {
  const user = USERS.find((w) => w.id === id);
  if (!user) {
    throw new MockException(404, {
      code: 'User:NotFound',
      message: 'User does not exist or has been deleted',
    });
  }
  if (user.isSuperAdmin) {
    throw new MockException(403, {
      code: 'User:SuperAdminResetPasswordForbidden',
      message:
        "The built-in super administrator's password cannot be reset by other administrators.",
    });
  }
  ensureAcceptablePassword(value.password, 'Password');
  user.password = value.password;
}

export function unlockUser(id: string) {
  const user = USERS.find((w) => w.id === id);
  if (!user) {
    throw new MockException(404, {
      code: 'User:NotFound',
      message: 'User does not exist or has been deleted',
    });
  }
  delete user.lockoutEnd;
}

/**
 * 管理员重置两步验证。超级管理员只能由本人重置（403）；未启用时什么也不做，与后端一致。
 * 真实后端还会撤销该用户的全部会话，Mock 的会话列表只有当前用户自己的，这里不复刻。
 */
export function resetTwoFactor(id: string) {
  const user = USERS.find((w) => w.id === id);
  if (!user) {
    throw new MockException(404, {
      code: 'User:NotFound',
      message: `User ${id} not found.`,
    });
  }
  if (user.isSuperAdmin && user.id !== getCurrentUser()?.id) {
    throw new MockException(403, {
      code: 'User:SuperAdminOperationForbidden',
      message: 'The built-in super administrator cannot be operated on by other administrators.',
    });
  }
  user.twoFactorEnabled = false;
  user.recoveryCodes = [];
}

/**
 * 与后端一致：删除幂等，不存在（含已删除）即成功；内置超级管理员不可删（403）。
 * 删除是软删除：移出列表但仍占着用户名与邮箱。
 */
export function deleteUser(id: string) {
  const index = USERS.findIndex((w) => w.id === id);
  if (index < 0) {
    return;
  }
  if (USERS[index].isSuperAdmin) {
    throw new MockException(403, {
      code: 'User:SuperAdminDeleteForbidden',
      message: 'The built-in super administrator cannot be deleted',
    });
  }
  DELETED_USERS.push(...USERS.splice(index, 1));
}

//#endif
export const USER_API = {
  'GET /api/v1/users': (req: MockRequest) => getUsers(req.queryParams),
  'GET /api/v1/users/:id': (req: MockRequest) => getUserById(req.params.id),
  //#if (LocalIdentity)
  'POST /api/v1/users': (req: MockRequest) => addUser(req.body),
  'PUT /api/v1/users/:id': (req: MockRequest) => updateUser(req.params.id, req.body),
  //#endif
  'PATCH /api/v1/users/:id/enable': (req: MockRequest) => enableUser(req.params.id),
  'PATCH /api/v1/users/:id/disable': (req: MockRequest) => disableUser(req.params.id),
  //#if (LocalIdentity)
  'POST /api/v1/users/:id/reset-password': (req: MockRequest) =>
    resetPassword(req.params.id, req.body),
  'POST /api/v1/users/:id/unlock': (req: MockRequest) => unlockUser(req.params.id),
  'POST /api/v1/users/:id/reset-two-factor': (req: MockRequest) => resetTwoFactor(req.params.id),
  'DELETE /api/v1/users/:id': (req: MockRequest) => deleteUser(req.params.id),
  //#endif
};
