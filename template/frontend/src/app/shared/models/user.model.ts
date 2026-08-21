export class User {
  id!: string;
  username!: string;
  email!: string;
  displayName?: string;
  avatar?: string;
  phoneNumber?: string;
  //#if (LocalAuthorization)
  roles!: string[];
  //#endif
  isSuperAdmin!: boolean;

  constructor(data: Partial<User>) {
    Object.assign(this, data);
  }
}
