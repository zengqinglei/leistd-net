import { UserOutputDto } from '../../src/app/features/account/models/account.dto';
import { UserManagementOutputDto } from '../../src/app/features/platform/models/user-management.dto';

export interface MockUser {
  id: string;
  username: string;
  email: string;
  password: string;
  nickname?: string;
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
    nickname: '系统管理员',
    avatar: 'https://api.dicebear.com/7.x/avataaars/svg?seed=admin',
    isActive: true,
    isSuperAdmin: true,
    isEmailVerified: true,
    creationTime: '2025-01-01T00:00:00Z',
    lastLoginTime: '2026-06-10T08:00:00Z',
    roles: ['Admin']
  },
  {
    id: 'user_demo',
    username: 'demo',
    email: 'demo@example.com',
    password: 'Demo@123456',
    nickname: '演示用户',
    avatar: 'https://api.dicebear.com/7.x/avataaars/svg?seed=demo',
    isActive: true,
    isSuperAdmin: false,
    isEmailVerified: true,
    creationTime: '2025-06-01T00:00:00Z',
    lastLoginTime: '2026-06-09T12:00:00Z',
    roles: ['Member']
  }
];

export function toUserOutput(user: MockUser): UserOutputDto {
  return {
    id: user.id,
    username: user.username,
    email: user.email,
    nickname: user.nickname,
    avatar: user.avatar,
    phoneNumber: user.phoneNumber,
    isActive: user.isActive,
    isSuperAdmin: user.isSuperAdmin,
    creationTime: user.creationTime,
    roles: user.roles
  };
}

export function toUserManagementOutput(user: MockUser): UserManagementOutputDto {
  return {
    id: user.id,
    username: user.username,
    email: user.email,
    displayName: user.nickname,
    avatar: user.avatar,
    isActive: user.isActive,
    isEmailVerified: user.isEmailVerified,
    roles: user.roles,
    isSuperAdmin: user.isSuperAdmin,
    creationTime: user.creationTime,
    lastLoginTime: user.lastLoginTime
  };
}
