import { expect, test } from '@playwright/test';

import { admin, authStatePath } from './support/accounts';

test.use({ storageState: authStatePath(admin()) });

/** 真实传输的最小回环：原始事件 ID 从 Sent 发件箱走到 Processed 收件箱。 */
test(
  'sends a distributed test event and processes it with the same event ID',
  { tag: '@crud' },
  async ({ page }) => {
    await page.goto('/platform/distributed-events');
    const response = page.waitForResponse(
      (response) =>
        response.url().endsWith('/distributed-events/test') ||
        response.url().includes('/distributed-events/test?'),
    );
    await page.getByTestId('event-send-test').click();
    const { eventId } = (await (await response).json()) as { eventId: string };
    await expect(page).toHaveURL(new RegExp(`keyword=${eventId}`));
    const row = page.getByRole('row').filter({ hasText: eventId });
    await expect(async () => {
      await page.getByRole('button', { name: 'Refresh', exact: true }).click();
      await expect(row.getByText('Sent', { exact: true })).toBeVisible({ timeout: 1000 });
    }).toPass({ timeout: 20000 });
    await page.getByRole('tab', { name: /^Inbox/ }).click();
    // 视图导航提交后再更新筛选，避免后一次导航覆盖仍在进行的切换。
    await expect(page).toHaveURL(/[?&]view=inbox(?:&|$)/);
    await page
      .getByRole('textbox', {
        name: 'Exact event ID / ordering key / trace ID / correlation ID',
        exact: true,
      })
      .fill(eventId);
    await expect(page).toHaveURL(new RegExp(`keyword=${eventId}`));
    await expect(async () => {
      await page.getByRole('button', { name: 'Refresh', exact: true }).click();
      await expect(row.getByText('Processed', { exact: true })).toBeVisible({ timeout: 1000 });
    }).toPass({ timeout: 20000 });
    await row.getByRole('button', { name: 'View details', exact: true }).click();
    await expect(page.getByTestId('event-detail')).toContainText(eventId);
    await expect(page.getByTestId('event-detail')).toContainText('Leistd.EventBus.Test');
  },
);
