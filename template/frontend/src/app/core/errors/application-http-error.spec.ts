import { HttpErrorResponse } from '@angular/common/http';

import { ApplicationHttpError } from './application-http-error';

function errorOf(body: unknown, status = 400): ApplicationHttpError {
  return ApplicationHttpError.from(
    new HttpErrorResponse({ error: body, status, statusText: 'Bad Request', url: '/api/v1/roles' }),
  );
}

describe('ApplicationHttpError', () => {
  it('校验失败时用字段错误组成消息，而不是概括性的 title', () => {
    const error = errorOf({
      type: 'urn:leistd:problem:validation-error',
      title: 'One or more validation errors occurred.',
      errors: [{ field: 'name', detail: '角色名称只能包含字母、数字和下划线' }],
    });

    expect(error.message).toBe('角色名称只能包含字母、数字和下划线');
  });

  it('业务 422 的 detail 也是概括，同样让位给字段错误', () => {
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

  it('每个字段只取第一条，多个字段各占一行', () => {
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

  it('没有收到响应时不展示浏览器的原始异常文本', () => {
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

  it('没有字段错误时沿用 detail', () => {
    const error = errorOf(
      { title: '冲突', detail: '角色名称已存在', code: 'Role:NameExists' },
      409,
    );

    expect(error.message).toBe('角色名称已存在');
    expect(error.code).toBe('Role:NameExists');
  });
});
