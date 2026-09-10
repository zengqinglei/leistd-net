import { requirePermission } from './authorization';
import { PERMISSIONS } from '../../src/app/shared/models/permission';
import { MockException, MockRequest } from '../core/models';
import { SETTING_DEFINITIONS, TENANT_SETTING_VALUES, USER_SETTING_VALUES } from '../data/settings';
import {
  getCurrentUser,
  getMockSessionSubjectId,
  getMockSessionTenantKey,
} from '../utils/current-user';

//#if (IncludeLocalization)
const SUPPORTED_LANGUAGES = ['en', 'zh-CN'];

//#endif
// 租户取自会话，不看 X-Tenant-Id：真实后端的租户解析链首位是「已认证主体的租户声明」，
// 主体一经处理就终止解析，请求头改不了已登录用户的租户。照请求头取会让 Mock 锁定一个
// 生产环境不存在的行为。
//
// 键的形状对齐真实 Store 的 ScopeKey：`{tenant}:t` 与 `{tenant}:u:{userId}`——
// 用户级也带租户，因为设置行本身带租户归属。
// 用主体标识而不是 Mock persona 的 id 做隔离键：Resource 形态下 persona 是所有令牌
// 共用的那个测试用户，拿它做键会让同租户下的两个真实用户共用一份个人偏好。
function requireSubjectId(): string {
  const subjectId = getCurrentUser() && getMockSessionSubjectId();
  if (!subjectId) {
    throw new MockException(401, { code: 'Error:Unauthorized', message: 'Not authenticated' });
  }
  return subjectId;
}

// 顺序与后端 SettingAppService 一致：先确认设置存在且可见，再校验值域。
// 反过来的话，写一个不存在的设置名会先撞值域校验，返回 400 而不是 404。
function requireDefinition(name: string) {
  const definition = SETTING_DEFINITIONS.find((s) => s.name === name);
  if (!definition) {
    throw new MockException(404, {
      code: 'Error:NotFound',
      message: `Setting '${name}' is not available.`,
    });
  }
  return definition;
}

// 值域与后端保持一致：Mock 放行了真实后端会拒的值，只会把问题推迟到联调时才发现。
function assertValidValue(name: string, value: string | null): void {
  if (value === null) {
    return;
  }

  if (value.length === 0) {
    throw new MockException(400, {
      code: 'Error:BadRequest',
      message: `An empty value is not accepted for '${name}'. Send null to clear the override.`,
    });
  }

  //#if (IncludeLocalization)
  if (name === 'Display.Language' && !SUPPORTED_LANGUAGES.includes(value)) {
    throw new MockException(400, {
      code: 'Error:BadRequest',
      message: `'${value}' is not a supported language.`,
    });
  }

  //#endif
  if (name === 'Display.TimeZone') {
    try {
      // 与后端的 HasIanaId 判定对齐：Windows 时区 ID 后端认、浏览器不认，两端都要拒。
      new Intl.DateTimeFormat('en-CA', { timeZone: value });
    } catch {
      throw new MockException(400, {
        code: 'Error:BadRequest',
        message: `'${value}' is not a valid IANA time zone id.`,
      });
    }
  }
}

function readBody(req: MockRequest): { name: string; value: string | null } {
  const { name, value } = (req.body ?? {}) as { name?: string; value?: string | null };
  if (typeof name !== 'string') {
    throw new MockException(400, { code: 'Error:BadRequest', message: 'name is required.' });
  }
  return { name, value: value ?? null };
}

function write(map: Map<string, string>, key: string, value: string | null): null {
  // null 是唯一的清除语义：删除该层级的行，读取回落到下一层。
  if (value === null) {
    map.delete(key);
  } else {
    map.set(key, value);
  }
  return null;
}

/** 设置中心 Mock API（对应 SettingController）。 */
export const SETTING_API = {
  'GET /api/v1/settings': () => {
    const subjectId = requireSubjectId();
    const tenantKey = getMockSessionTenantKey();
    return SETTING_DEFINITIONS.map((definition) => ({
      name: definition.name,
      displayName: definition.displayName,
      group: definition.group,
      // 真实后端按 `SettingGroup:{group}` 查词条；Mock 不做本地化，回显标识本身
      groupDisplayName: definition.group,
      userValue: USER_SETTING_VALUES.get(`${tenantKey}:${subjectId}:${definition.name}`) ?? null,
      tenantValue: TENANT_SETTING_VALUES.get(`${tenantKey}:${definition.name}`) ?? null,
      defaultValue: definition.defaultValue,
      allowsTenantScope: definition.allowsTenantScope,
      allowsUserScope: definition.allowsUserScope,
      allowsHostScope: definition.allowsHostScope ?? false,
      minimum: definition.minimum ?? null,
      maximum: definition.maximum ?? null,
    }));
  },

  'PUT /api/v1/settings/current-user': (req: MockRequest) => {
    const subjectId = requireSubjectId();
    const { name, value } = readBody(req);
    requireDefinition(name);
    assertValidValue(name, value);
    return write(USER_SETTING_VALUES, `${getMockSessionTenantKey()}:${subjectId}:${name}`, value);
  },

  'PUT /api/v1/settings/current-tenant': (req: MockRequest) => {
    // 改系统默认值需要管理权限；改自己的偏好只要登录。这层差异是两个端点存在的理由。
    requirePermission(PERMISSIONS.settings.default);
    const { name, value } = readBody(req);
    requireDefinition(name);
    assertValidValue(name, value);
    return write(TENANT_SETTING_VALUES, `${getMockSessionTenantKey()}:${name}`, value);
  },
};
