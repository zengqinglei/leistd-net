import { afterNextRender, inject, Injector } from '@angular/core';
import { FieldTree, submit, TreeValidationResult, ValidationError } from '@angular/forms/signals';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif
import { toast } from '@spartan-ng/brain/sonner';

import { focusFirstInvalid } from '../../shared/utils/form-focus';
import { mapFormErrors } from '../errors/form-errors';

/** 保持 Angular 提交状态覆盖真实异步动作，服务端错误挂载并渲染后再移焦点。 */
export function injectFormSubmission() {
  const injector = inject(Injector);
  //#if (IncludeLocalization)
  const transloco = inject(TranslocoService);
  //#endif
  return async <T>(
    fields: FieldTree<T>,
    root: HTMLElement,
    action: () => Promise<TreeValidationResult>,
    report: (message: string, error?: unknown) => void = (message) => {
      //#if (IncludeLocalization)
      toast.error(transloco.translate('common.requestError'), { description: message });
      //#else
      toast.error('Request error', { description: message });
      //#endif
    },
    aliases: Readonly<Record<string, ValidationError.WithFieldTree['fieldTree']>> = {},
  ): Promise<boolean> => {
    let localInvalid = false;
    let requestFailed = false;
    const focus = () => afterNextRender(() => focusFirstInvalid(root), { injector });
    const success = await submit(fields, {
      onInvalid: () => {
        localInvalid = true;
        focus();
      },
      action: async () => {
        try {
          return await action();
        } catch (error) {
          // HTTP 错误在持有表单的一层回填；其余请求失败维持既有反馈通道。
          requestFailed = true;
          //#if (IncludeLocalization)
          const fallback = transloco.translate('common.requestFailed');
          //#else
          const fallback = 'The request could not be completed.';
          //#endif
          const result = mapFormErrors(error, fields, aliases, fallback);
          if (result.message) report(result.message, error);
          return result.errors;
        }
      },
    });
    if (!success && !localInvalid) focus();
    return success && !requestFailed;
  };
}
