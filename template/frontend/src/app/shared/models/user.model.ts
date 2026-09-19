export class User {
  id!: string;
  username!: string;
  email!: string;
  displayName?: string;
  avatar?: string;
  phoneNumber?: string;
  /** 当前邮箱是否已验证；只有本地身份的形态下有这项。 */
  isEmailVerified?: boolean;
  isTwoFactorEnabled?: boolean;
  /** 受限会话：所在租户要求两步验证而本人尚未启用。只有本地身份的形态下有这项。 */
  twoFactorSetupRequired?: boolean;
  roles!: string[];
  isSuperAdmin!: boolean;

  constructor(data: Partial<User>) {
    Object.assign(this, data);
  }
}
