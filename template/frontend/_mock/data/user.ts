import { ROLES } from './authorization';
import { UserManagementOutputDto } from '../../src/app/features/platform/models/user-management.dto';
//#if (LocalIdentity)
import { UserOutputDto } from '../../src/app/shared/dtos/auth.dto';
//#endif

export interface MockUser {
  id: string;
  username: string;
  email: string;
  password: string;
  displayName?: string;
  avatar?: string;
  phoneNumber?: string;
  isActive: boolean;
  isSuperAdmin: boolean;
  isEmailVerified: boolean;
  creationTime: string;
  lastLoginTime?: string;
  roles: string[];
}

export const USERS: MockUser[] = [
  {
    id: 'user_admin',
    username: 'admin',
    email: 'admin@example.com',
    password: 'Admin@123456',
    displayName: 'Administrator',
    avatar: 'https://api.dicebear.com/7.x/avataaars/svg?seed=admin',
    isActive: true,
    isSuperAdmin: true,
    isEmailVerified: true,
    creationTime: '2025-01-01T00:00:00Z',
    lastLoginTime: '2026-06-10T08:00:00Z',
    roles: ['Admin'],
  },
  {
    id: 'user_demo',
    username: 'demo',
    email: 'demo@example.com',
    password: 'Demo@123456',
    displayName: 'Demo User',
    avatar: 'https://api.dicebear.com/7.x/avataaars/svg?seed=demo',
    isActive: true,
    isSuperAdmin: false,
    isEmailVerified: true,
    creationTime: '2025-06-01T00:00:00Z',
    lastLoginTime: '2026-06-09T12:00:00Z',
    roles: ['Member'],
  },
];
//#if (LocalIdentity)

export function toUserOutput(user: MockUser): UserOutputDto {
  return {
    id: user.id,
    username: user.username,
    email: user.email,
    displayName: user.displayName,
    avatar: user.avatar,
    phoneNumber: user.phoneNumber,
    isActive: user.isActive,
    isSuperAdmin: user.isSuperAdmin,
    creationTime: user.creationTime,
    // 当前用户模型只需要角色名（用于展示徽章），不需要 Id。
    roles: user.roles,
  };
}
//#endif

export function toUserManagementOutput(user: MockUser): UserManagementOutputDto {
  return {
    id: user.id,
    username: user.username,
    email: user.email,
    displayName: user.displayName,
    avatar: user.avatar,
    isActive: user.isActive,
    //#if (LocalIdentity)
    isEmailVerified: user.isEmailVerified,
    //#endif
    // 角色以 Id + 名称的结构返回：Id 用于提交，名称仅用于展示与筛选。
    roles: ROLES.filter((role) => user.roles.includes(role.name)).map((role) => ({
      id: role.id,
      name: role.name,
      displayName: role.displayName,
    })),
    isSuperAdmin: user.isSuperAdmin,
    creationTime: user.creationTime,
    //#if (LocalIdentity)
    lastLoginTime: user.lastLoginTime,
    //#endif
  };
}
