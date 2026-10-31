import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { form } from '@angular/forms/signals';

import { ApplicationHttpError } from './application-http-error';
import { mapFormErrors } from './form-errors';

describe('mapFormErrors', () => {
  const fields = () =>
    TestBed.runInInjectionContext(() =>
      form(signal({ email: '', name: '', nested: { code: '' } })),
    );
  const failure = (errors: unknown) =>
    ApplicationHttpError.from(
      new HttpErrorResponse({ status: 400, error: { title: 'Validation failed', errors } }),
    );

  it('matches declared top-level fields without case sensitivity', () => {
    const model = fields();
    const result = mapFormErrors(failure({ Email: ['Already used'] }), model);
    expect(result.errors).toEqual([
      { kind: 'server', message: 'Already used', fieldTree: model.email },
    ]);
    expect(result.message).toBeNull();
  });

  it('uses explicit aliases for DTO names and indexed paths', () => {
    const model = fields();
    const result = mapFormErrors(
      failure([{ field: 'codes[0].value', detail: 'Code rejected' }]),
      model,
      { 'codes[0].value': model.nested.code },
    );
    expect(result.errors[0].fieldTree).toBe(model.nested.code);
    expect(result.message).toBeNull();
  });

  it('keeps only unmapped messages in the general feedback', () => {
    const model = fields();
    const result = mapFormErrors(
      failure([
        { field: 'email', detail: 'Email rejected' },
        { field: 'unknown', detail: 'Refresh and try again' },
      ]),
      model,
    );
    expect(result.errors).toHaveLength(1);
    expect(result.message).toBe('Refresh and try again');
  });

  it('does not guess nested paths or map errors without a display message', () => {
    const model = fields();
    const result = mapFormErrors(
      failure([
        { field: 'nested.code', detail: 'Retry' },
        { field: 'email', code: 'Validation:Rejected' },
      ]),
      model,
    );
    expect(result.errors).toEqual([]);
    expect(result.message).toBeTruthy();
  });

  it('preserves non-field request errors', () => {
    expect(mapFormErrors(new Error('Offline'), fields())).toEqual({
      errors: [],
      message: 'Offline',
    });
  });
  it('does not repeat a mapped message for a remaining error code without text', () => {
    const model = fields();
    const result = mapFormErrors(
      failure([
        { field: 'email', detail: 'Already used' },
        { field: 'other', code: 'Validation:Rejected' },
      ]),
      model,
      {},
      'Please retry',
    );
    expect(result.errors[0].message).toBe('Already used');
    expect(result.message).toBe('Please retry');
  });
});
