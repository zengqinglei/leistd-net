import { HttpErrorResponse } from '@angular/common/http';

import { applicationErrorMessage, ApplicationHttpError } from './application-http-error';

function errorOf(body: unknown, status = 400): ApplicationHttpError {
  return ApplicationHttpError.from(
    new HttpErrorResponse({ error: body, status, statusText: 'Bad Request', url: '/api/v1/roles' }),
  );
}

describe('ApplicationHttpError', () => {
  it('builds the message from field errors, not the generic title, on validation failure', () => {
    const error = errorOf({
      type: 'urn:leistd:problem:validation-error',
      title: 'One or more validation errors occurred.',
      errors: [{ field: 'name', detail: '角色名称只能包含字母、数字和下划线' }],
    });

    expect(error.message).toBe('角色名称只能包含字母、数字和下划线');
  });

  // 显式 422 的 detail 同样只是概括
  it('prefers field errors over the detail of an explicit 422', () => {
    const error = errorOf(
      {
        title: '无法处理的实体',
        detail: '提交的信息有误。',
        errors: [{ field: 'phone', detail: '号码已被占用' }],
      },
      422,
    );

    expect(error.message).toBe('号码已被占用');
  });

  it('takes the first error per field and puts each field on its own line', () => {
    const error = errorOf({
      title: '错误请求',
      errors: [
        { field: 'name', detail: '角色名称不能为空' },
        { field: 'name', detail: '角色名称长度必须在 2-64 个字符之间' },
        { field: 'displayName', detail: '显示名称不能为空' },
      ],
    });

    expect(error.message).toBe('角色名称不能为空\n显示名称不能为空');
    expect(error.details.map((item) => item.field)).toEqual(['name', 'name', 'displayName']);
  });

  it('hides the raw browser exception text when no response was received', () => {
    const response = new HttpErrorResponse({
      error: new TypeError('Failed to fetch'),
      status: 0,
      url: '/api/v1/auth/end-impersonation',
    });

    expect(ApplicationHttpError.from(response, '无法连接到服务器').message).toBe(
      '无法连接到服务器',
    );
    expect(ApplicationHttpError.from(response).message).not.toContain('Failed to fetch');
  });

  it('falls back to detail when there are no field errors', () => {
    const error = errorOf(
      { title: '冲突', detail: '角色名称已存在', code: 'Role:NameExists' },
      409,
    );

    expect(error.message).toBe('角色名称已存在');
    expect(error.code).toBe('Role:NameExists');
  });

  // 协议层失败只有状态码语义：没有 detail 与业务码，文案取本地化标题
  it('keeps the traceId on 5xx and builds a reportable message', () => {
    const error = errorOf(
      {
        title: '服务器内部错误',
        status: 500,
        traceId: '4bf92f3577b34da6a3ce929d0e0e4736',
      },
      500,
    );

    expect(error.code).toBeUndefined();
    expect(error.traceId).toBe('4bf92f3577b34da6a3ce929d0e0e4736');
    expect(applicationErrorMessage(error)).toBe(
      '服务器内部错误 (Trace ID: 4bf92f3577b34da6a3ce929d0e0e4736)',
    );
  });

  it('splits dictionary-shaped errors per message and uses the first per field', () => {
    const error = errorOf(
      {
        title: 'One or more validation errors occurred.',
        errors: {
          Name: ['The Name field is required.', 'Name is too short.'],
          Quantity: ['Must be positive.'],
        },
        traceId: '00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01',
      },
      400,
    );

    expect(error.details.map((item) => [item.field, item.detail])).toEqual([
      ['Name', 'The Name field is required.'],
      ['Name', 'Name is too short.'],
      ['Quantity', 'Must be positive.'],
    ]);
    expect(error.message).toBe('The Name field is required.\nMust be positive.');
    expect(error.code).toBeUndefined();
  });

  it('ignores errorCode and message from the response envelope', () => {
    const error = errorOf(
      { code: 409, errorCode: 'Role:NameExists', message: '角色名称已存在' },
      409,
    );

    expect(error.code).toBeUndefined();
    expect(error.message).not.toBe('角色名称已存在');
  });
});
