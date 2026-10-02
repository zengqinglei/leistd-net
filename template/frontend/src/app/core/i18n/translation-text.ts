import { Translation } from '@jsverse/transloco';

/**
 * 从 `translateObjectSignal` 取到的词条对象里按点号路径取一句文案；不存在或不是文案时为 `undefined`。
 *
 * 词条对象里带点号的键会被 Transloco 展开成嵌套对象（`actions['user.created']` 取不到，
 * 要走 `actions.user.created`），所以按段逐级取。词条尚未到达时对象为空，同样得到 `undefined`，
 * 调用方据此退回原始标识；词条到达后信号更新，读它的 computed 随之重算。
 */
export function textAt(texts: Translation, path: string): string | undefined {
  let node: unknown = texts;
  for (const segment of path.split('.')) {
    if (node === null || typeof node !== 'object') {
      return undefined;
    }
    node = (node as Record<string, unknown>)[segment];
  }
  return typeof node === 'string' ? node : undefined;
}
