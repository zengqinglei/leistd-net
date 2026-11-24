import { HttpHeaders } from '@angular/common/http';

import { MockException, MockRequest } from '../core/models';
import { NOTIFICATIONS } from '../data/notifications';
import { NOTIFICATION_API, NOTIFICATION_MAXIMUM_LIMIT } from './notification';

import type { NotificationOutputDto } from '../../src/app/shared/dtos/notification.dto';
import type { PagedResultDto } from '../../src/app/shared/dtos/paged-result.dto';

/** 通知 Mock：与后端同形分页，最新的在前；含本地化时按请求语言给文案。 */
describe('notification mock', () => {
  function list(
    queryParams: Record<string, string> = {},
    language?: string,
  ): PagedResultDto<NotificationOutputDto> {
    const headers = language ? new HttpHeaders({ 'Accept-Language': language }) : new HttpHeaders();
    return NOTIFICATION_API['GET /api/v1/notifications']({
      headers,
      queryParams,
    } as unknown as MockRequest);
  }

  function rejectedField(queryParams: Record<string, string>): string {
    let thrown: unknown;
    try {
      list(queryParams);
    } catch (error) {
      thrown = error;
    }
    expect(thrown).toBeInstanceOf(MockException);
    const exception = thrown as MockException;
    expect(exception.status).toBe(400);
    expect(exception.error.code).toBeUndefined();
    return exception.error.errors[0].field;
  }

  it('lists the newest notification first', () => {
    const times = list().items.map((notification) => Date.parse(notification.creationTime));

    expect(times).toEqual([...times].sort((a, b) => b - a));
  });

  it('pages by offset and limit and reports the total count', () => {
    const all = list().items;

    const page = list({ offset: '1', limit: '1' });

    expect(page.totalCount).toBe(NOTIFICATIONS.length);
    expect(page.items.map((item) => item.id)).toEqual([all[1].id]);
  });

  it('rejects an out-of-range offset or limit with a 400 field error', () => {
    expect(rejectedField({ offset: '-1' })).toBe('offset');
    expect(rejectedField({ limit: '0' })).toBe('limit');
    expect(rejectedField({ limit: String(NOTIFICATION_MAXIMUM_LIMIT + 1) })).toBe('limit');
    expect(list({ limit: String(NOTIFICATION_MAXIMUM_LIMIT) }).items).toHaveLength(
      NOTIFICATIONS.length,
    );
  });

  // 端点参数是 int：非十进制整数字面量或超出 Int32 在绑定阶段就是 400，没有字段错误；越界的整数才是字段错误
  it('rejects a malformed or out-of-Int32 offset or limit as a bad request without field errors', () => {
    const malformed: Record<string, string>[] = [
      { offset: '1e1' },
      { offset: '1.5' },
      { offset: '' },
      { offset: '2147483648' },
      { limit: '1e1' },
      { limit: '10.0' },
      { limit: '' },
      { limit: '-2147483649' },
    ];
    for (const queryParams of malformed) {
      let thrown: unknown;
      try {
        list(queryParams);
      } catch (error) {
        thrown = error;
      }
      expect(thrown, JSON.stringify(queryParams)).toBeInstanceOf(MockException);
      expect((thrown as MockException).status).toBe(400);
      expect((thrown as MockException).error).toBeUndefined();
    }
    expect(list({ offset: '2147483647' }).items).toEqual([]);
    expect(rejectedField({ limit: '-2147483648' })).toBe('limit');
  });
  //#if (IncludeLocalization)

  it('answers in Chinese for a Chinese interface and in English otherwise', () => {
    const chinese = list({}, 'zh-CN').items[0];
    const english = list({}, 'en').items[0];

    expect(chinese.id).toBe(english.id);
    expect(chinese.title).toMatch(/[一-鿿]/);
    expect(english.title).not.toMatch(/[一-鿿]/);
    expect(list({}, 'fr').items[0].title).toBe(english.title);
  });
  //#endif
});
