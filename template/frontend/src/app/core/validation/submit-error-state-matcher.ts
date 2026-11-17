import { Injectable, linkedSignal, signal, Signal, WritableSignal } from '@angular/core';
import { AbstractControl } from '@angular/forms';
import { FieldTree, ValidationError } from '@angular/forms/signals';
import { ErrorStateMatcher } from '@spartan-ng/brain/forms';

const submissionVersion = signal(0);
const submissions = new WeakMap<object, Signal<boolean>>();
const attempts = new WeakMap<object, WritableSignal<boolean>>();

/** 按提交的字段树隔离状态；reset 将 touched 清零，同时重置提交标记。 */
export function markFormSubmitted<T>(fields: FieldTree<T>): void {
  let attempted = attempts.get(fields);
  if (!attempted) {
    attempted = linkedSignal(() => {
      fields().touched();
      return false;
    });
    attempts.set(fields, attempted);
  }
  fields().markAsTouched();
  attempted.set(true);
  const state = attempted;
  const register = (tree: ValidationError.WithFieldTree['fieldTree']) => {
    submissions.set(tree, state);
    for (const key of Object.keys(tree)) {
      register((tree as Record<string, ValidationError.WithFieldTree['fieldTree']>)[key]);
    }
  };
  register(fields);
  submissionVersion.update((version) => version + 1);
}

/** 组合控件与原生约束复用所在提交树的尝试状态。 */
export function wasFormSubmitted(fields: ValidationError.WithFieldTree['fieldTree']): boolean {
  submissionVersion();
  return !!submissions.get(fields)?.();
}

/** 无直接控件的组合字段也使用同一报错时机。 */
export function isSubmittedInvalid(fields: ValidationError.WithFieldTree['fieldTree']): boolean {
  return wasFormSubmitted(fields) && fields().invalid();
}

/** Signal Forms 的 interop errors 保留公开的 fieldTree，不依赖 interop 控件内部成员。 */
@Injectable()
export class SubmitErrorStateMatcher implements ErrorStateMatcher {
  isInvalid(control: AbstractControl | null): boolean {
    if (!control?.invalid) return false;
    return Object.values(control.errors ?? {}).some(
      (error: ValidationError.WithFieldTree) =>
        typeof error?.fieldTree === 'function' && isSubmittedInvalid(error.fieldTree),
    );
  }
}
