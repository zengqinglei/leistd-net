import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture } from '@angular/core/testing';

import { ApplicationHttpError } from '../errors/application-http-error';

/** 提交的错误先挂载，afterNextRender 再移焦点；等待真实渲染阶段，不轮询或放宽断言。 */
export async function renderSubmission<T>(
  fixture: ComponentFixture<T>,
  action: () => unknown,
): Promise<void> {
  await action();
  await fixture.whenStable();
  fixture.detectChanges();
  await new Promise<void>((resolve) => requestAnimationFrame(() => resolve()));
  await fixture.whenStable();
}

/** 与 HTTP 拦截器相同的字段拒绝载荷，用于验证每个表单自身的接线。 */
export function rejectedField(
  field: string,
  detail = 'Server rejected this field',
): ApplicationHttpError {
  return ApplicationHttpError.from(
    new HttpErrorResponse({
      status: 400,
      error: { errors: [{ field, detail }] },
    }),
  );
}
