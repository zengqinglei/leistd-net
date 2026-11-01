import { firstValueFrom } from 'rxjs';

import { ConfirmOptions } from './confirm-service';

/** 页面测试接受确认并执行交出的请求；真实框内错误/重试由 ConfirmService spec 覆盖。 */
export async function acceptConfirmAction(options: ConfirmOptions): Promise<boolean> {
  try {
    if (options.action) await firstValueFrom(options.action());
    return true;
  } catch {
    return false;
  }
}
