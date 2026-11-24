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
