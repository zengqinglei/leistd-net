import { requirePermission } from './authorization';
import {
  CreateOpenApplicationInputDto,
  OpenApplicationOutputDto,
  UpdateOpenApplicationInputDto,
} from '../../src/app/features/platform/models/open-application.dto';
import { PagedResultDto } from '../../src/app/shared/models/paged-result.dto';
import { PERMISSIONS } from '../../src/app/shared/models/permission';
import { MockException, MockRequest } from '../core/models';
import { parseMockSorting } from '../core/sorting';
import { MockOpenApplication, OPEN_APPLICATIONS } from '../data/open-applications';

const applications = OPEN_APPLICATIONS;

function toOutput(item: MockOpenApplication): OpenApplicationOutputDto {
  const { clientSecret: _clientSecret, ...output } = item;
  return output;
}

function getQueryValue(value: unknown) {
  const normalized = Array.isArray(value) ? value[0] : value;
  return normalized === undefined || normalized === null || normalized === ''
    ? undefined
    : String(normalized);
}

/** 与后端 `OpenApplicationAppService.ApplySorting` 同一份字段清单。 */
const APPLICATION_SORT_FIELDS = ['clientId', 'displayName', 'creationTime'] as const;

function sortApplications(items: MockOpenApplication[], sorting?: unknown) {
  const { field, descending } = parseMockSorting(sorting, APPLICATION_SORT_FIELDS, 'clientId');
  const multiplier = descending ? -1 : 1;

  return [...items].sort((a, b) => {
    const left = String(a[field as keyof MockOpenApplication] ?? '').toLowerCase();
    const right = String(b[field as keyof MockOpenApplication] ?? '').toLowerCase();
    const compared = left.localeCompare(right) * multiplier;
    // 与后端一样固定追加 clientId 作为稳定次序，且不随方向反转
    return compared !== 0 ? compared : a.clientId.localeCompare(b.clientId);
  });
}

function getOpenApplications(req: MockRequest) {
  requirePermission(PERMISSIONS.openApplications.default);
  const keyword = getQueryValue(req.queryParams['keyword']);
  const applicationType = getQueryValue(req.queryParams['applicationType']);
  const clientType = getQueryValue(req.queryParams['clientType']);
  const offset = getQueryValue(req.queryParams['offset']) ?? '0';
  const limit = getQueryValue(req.queryParams['limit']) ?? '10';
  const sorting = getQueryValue(req.queryParams['sorting']);
  let items = [...applications];

  if (keyword) {
    const value = keyword.trim().toLowerCase();
    items = items.filter(
      (item) =>
        item.clientId.toLowerCase().includes(value) ||
        (item.displayName ?? '').toLowerCase().includes(value),
    );
  }

  if (applicationType) {
    items = items.filter((item) => item.applicationType === applicationType);
  }

  if (clientType) {
    items = items.filter((item) => item.clientType === clientType);
  }

  items = sortApplications(items, sorting);

  const totalCount = items.length;
  const start = +offset;
  const end = start + +limit;

  return {
    totalCount,
    items: items.slice(start, end).map(toOutput),
  } as PagedResultDto<OpenApplicationOutputDto>;
}

function getOpenApplication(req: MockRequest) {
  requirePermission(PERMISSIONS.openApplications.default);
  const id = req.params['id'];
  const application = applications.find((item: MockOpenApplication) => item.id === id);
  if (!application) {
    throw new MockException(404, 'Open application not found');
  }
  return toOutput(application);
}

function validateApplication(
  input: CreateOpenApplicationInputDto | UpdateOpenApplicationInputDto,
  id?: string,
) {
  if ('clientId' in input) {
    const clientId = input.clientId.trim();
    if (!clientId) {
      throw new MockException(400, { message: 'Client ID is required' });
    }
    if (
      applications.some((item: MockOpenApplication) => item.clientId === clientId && item.id !== id)
    ) {
      throw new MockException(400, { message: `Client ID already exists: ${clientId}` });
    }
  }

  if (input.clientType === 'public' && 'clientSecret' in input && input.clientSecret) {
    throw new MockException(400, { message: 'Public clients cannot configure a client secret' });
  }

  if (
    (input.applicationType === 'native' || input.clientType === 'public') &&
    !input.requirements.includes('ft:pkce')
  ) {
    throw new MockException(400, { message: 'Native/Public clients must enable PKCE' });
  }
}

function createOpenApplication(req: MockRequest) {
  requirePermission(PERMISSIONS.openApplications.create);
  const body = req.body as CreateOpenApplicationInputDto;
  validateApplication(body);

  const newApplication: MockOpenApplication = {
    id: body.clientId,
    clientId: body.clientId,
    displayName: body.displayName,
    applicationType: body.applicationType,
    clientType: body.clientType,
    consentType: body.consentType,
    redirectUris: body.redirectUris || [],
    postLogoutRedirectUris: body.postLogoutRedirectUris || [],
    permissions: body.permissions || [],
    requirements: body.requirements || [],
    settings: {},
    properties: {},
    hasClientSecret: body.clientType === 'confidential',
    creationTime: new Date().toISOString(),
    clientSecret:
      body.clientType === 'confidential' ? `mock-secret-${crypto.randomUUID()}` : undefined,
  };

  applications.unshift(newApplication);
  // 创建响应一次性返回明文 Secret（列表/详情仍通过 toOutput 隐藏），供前端弹窗展示。
  return { ...toOutput(newApplication), clientSecret: newApplication.clientSecret };
}

function updateOpenApplication(req: MockRequest) {
  requirePermission(PERMISSIONS.openApplications.update);
  const id = req.params['id'];
  const body = req.body as UpdateOpenApplicationInputDto;
  const index = applications.findIndex((item: MockOpenApplication) => item.id === id);
  if (index === -1) {
    throw new MockException(404, 'Open application not found');
  }

  validateApplication(body, id);

  applications[index] = {
    ...applications[index],
    displayName: body.displayName,
    applicationType: body.applicationType,
    clientType: body.clientType,
    consentType: body.consentType,
    redirectUris: body.redirectUris || [],
    postLogoutRedirectUris: body.postLogoutRedirectUris || [],
    permissions: body.permissions || [],
    requirements: body.requirements || [],
    hasClientSecret: body.clientType === 'confidential' && applications[index].hasClientSecret,
    clientSecret: body.clientType === 'confidential' ? applications[index].clientSecret : undefined,
  };

  return toOutput(applications[index]);
}

function deleteOpenApplication(req: MockRequest) {
  requirePermission(PERMISSIONS.openApplications.delete);
  const id = req.params['id'];
  const index = applications.findIndex((item: MockOpenApplication) => item.id === id);
  if (index !== -1) {
    applications.splice(index, 1);
  }
  return { success: true };
}

function resetOpenApplicationSecret(req: MockRequest) {
  requirePermission(PERMISSIONS.openApplications.resetSecret);
  const id = req.params['id'];
  const application = applications.find((item: MockOpenApplication) => item.id === id);
  if (!application) {
    throw new MockException(404, 'Open application not found');
  }
  if (application.clientType !== 'confidential') {
    throw new MockException(400, { message: 'Only confidential clients can reset their secret' });
  }

  const clientSecret = `mock_secret_${Math.random().toString(36).slice(2, 14)}`;
  application.clientSecret = clientSecret;
  application.hasClientSecret = true;
  return { clientSecret };
}

export const OPEN_APPLICATION_API = {
  'GET /api/v1/open-applications': (req: MockRequest) => getOpenApplications(req),
  'GET /api/v1/open-applications/:id': (req: MockRequest) => getOpenApplication(req),
  'POST /api/v1/open-applications': (req: MockRequest) => createOpenApplication(req),
  'PUT /api/v1/open-applications/:id': (req: MockRequest) => updateOpenApplication(req),
  'DELETE /api/v1/open-applications/:id': (req: MockRequest) => deleteOpenApplication(req),
  'POST /api/v1/open-applications/:id/reset-secret': (req: MockRequest) =>
    resetOpenApplicationSecret(req),
};
