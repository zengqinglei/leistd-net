import { OPEN_APPLICATION_API } from './open-application';
import { CreateOpenApplicationInputDto } from '../../src/app/features/platform/dtos/open-application.dto';
import { MockException } from '../core/models';
import { OPEN_APPLICATIONS } from '../data/open-applications';
import { setMockSessionUserId } from '../utils/current-user';

type Handler = (req: { body?: unknown; params?: Record<string, string> }) => unknown;

function rejectionOf(run: () => unknown): MockException {
  try {
    run();
  } catch (error) {
    expect(error).toBeInstanceOf(MockException);
    return error as MockException;
  }
  throw new Error('expected the handler to reject');
}

/**
 * 开放应用 Mock 的失败形态与后端一致：入参校验是 400 字段错误（不带业务码，一次报全），
 * 先于唯一性等业务规则；删除幂等。
 */
describe('open application mock', () => {
  const create = OPEN_APPLICATION_API['POST /api/v1/open-applications'] as Handler;
  const remove = OPEN_APPLICATION_API['DELETE /api/v1/open-applications/:id'] as Handler;
  let snapshot: typeof OPEN_APPLICATIONS;

  const valid: CreateOpenApplicationInputDto = {
    clientId: 'mock-spec-client',
    displayName: 'Spec client',
    applicationType: 'web',
    clientType: 'confidential',
    redirectUris: ['https://app.example.test/callback'],
    postLogoutRedirectUris: [],
    permissions: [],
    requirements: [],
    sessionBound: false,
  };

  beforeEach(() => {
    snapshot = [...OPEN_APPLICATIONS];
    setMockSessionUserId('user_admin');
  });

  afterEach(() => {
    OPEN_APPLICATIONS.length = 0;
    OPEN_APPLICATIONS.push(...snapshot);
    setMockSessionUserId(null);
  });

  function fieldsOf(error: MockException): string[] {
    return error.error.errors.map((item: { field: string }) => item.field);
  }

  it('reports every missing field at once as 400 field errors without a code', () => {
    const error = rejectionOf(() =>
      create({ body: { ...valid, clientId: '  ', sessionBound: null } }),
    );

    expect(error.status).toBe(400);
    expect(error.error.code).toBeUndefined();
    expect(fieldsOf(error)).toEqual(['clientId', 'sessionBound']);
    expect(OPEN_APPLICATIONS).toEqual(snapshot);
  });

  it.each([
    ['a relative URI', 'callback'],
    ['whitespace', 'https://app.example.test/call back'],
    ['a fragment', 'https://app.example.test/callback#done'],
  ])('rejects a callback URI with %s as a field error', (_, uri) => {
    const error = rejectionOf(() =>
      create({ body: { ...valid, redirectUris: [uri], postLogoutRedirectUris: [uri] } }),
    );

    expect(error.status).toBe(400);
    expect(fieldsOf(error)).toEqual(['redirectUris', 'postLogoutRedirectUris']);
  });

  it('checks the field errors before the client ID uniqueness', () => {
    const taken = snapshot[0].clientId;

    const invalid = rejectionOf(() =>
      create({ body: { ...valid, clientId: taken, sessionBound: null } }),
    );
    expect(invalid.status).toBe(400);

    const duplicate = rejectionOf(() => create({ body: { ...valid, clientId: taken } }));
    expect(duplicate.status).toBe(409);
    expect(duplicate.error.code).toBe('OpenApp:ClientIdTaken');
  });

  it('deletes idempotently: a missing application succeeds and changes nothing', () => {
    expect(() => remove({ params: { id: 'missing-client' } })).not.toThrow();

    expect(OPEN_APPLICATIONS).toEqual(snapshot);
  });
});
