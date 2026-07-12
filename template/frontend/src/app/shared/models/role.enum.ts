export enum Role {
  Admin = 'Admin',
  Operator = 'Operator',
  Member = 'Member'
}

export const ROLE_LABEL_MAP: Record<Role, string> = {
  [Role.Admin]: $localize`管理员`,
  [Role.Operator]: $localize`运营人员`,
  [Role.Member]: $localize`普通成员`
};
