import { createServer, type IncomingHttpHeaders, type Server } from 'node:http';

import { expect, test } from '@playwright/test';

import { admin, authStatePath } from './support/accounts';

import type { AddressInfo } from 'node:net';

test.use({ storageState: authStatePath(admin()) });

interface ReceivedRequest {
  headers: IncomingHttpHeaders;
  body: string;
}

/** 本机接收端：开发环境配置放行回环地址，投递经真实的签名、出站请求与结果判定到达这里。 */
async function startReceiver(): Promise<{
  url: string;
  received: ReceivedRequest[];
  server: Server;
}> {
  const received: ReceivedRequest[] = [];
  const server = createServer((request, response) => {
    let body = '';
    request.on('data', (chunk: Buffer) => (body += chunk.toString('utf8')));
    request.on('end', () => {
      received.push({ headers: request.headers, body });
      response.writeHead(200, { 'content-type': 'application/json' });
      response.end('{"received":true}');
    });
  });
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  const { port } = server.address() as AddressInfo;
  return { url: `http://127.0.0.1:${port}/webhooks`, received, server };
}

test(
  'creates a subscription, sends a test event and shows the successful delivery',
  { tag: '@crud' },
  async ({ page }) => {
    const receiver = await startReceiver();
    const name = `E2E webhook ${Date.now()}`;
    try {
      await page.goto('/platform/webhooks');

      await page.getByRole('button', { name: 'New subscription' }).click();
      const dialog = page.getByRole('dialog');
      await dialog.getByLabel('Name', { exact: true }).fill(name);
      await dialog.getByLabel('Endpoint URL').fill(receiver.url);
      // 不用 check()：它在点击后立即核对状态，而 aria-checked 要等下一轮变更检测才写回
      const allEvents = dialog.getByLabel('All events');
      await allEvents.click();
      await expect(allEvents).toBeChecked();
      await dialog.getByRole('button', { name: 'Create' }).click();

      // 一次性密钥对话框：只出现这一次，关闭后回到列表
      const secretDialog = page.getByRole('dialog');
      await expect(secretDialog.getByTestId('webhook-secret-value')).toHaveText(/^whsec_/);
      await secretDialog.getByRole('button', { name: "I've saved it" }).click();

      const row = page.getByRole('row').filter({ hasText: name });
      await expect(row).toBeVisible();
      await row.getByRole('button', { name: 'Send test event' }).click();

      // 提交后立即入队发送：接收端收到带 Standard Webhooks 头的测试事件
      await expect.poll(() => receiver.received.length, { timeout: 15_000 }).toBeGreaterThan(0);
      const [delivered] = receiver.received;
      expect(delivered.headers['webhook-id']).toBeTruthy();
      expect(delivered.headers['webhook-signature']).toMatch(/^v1,/);
      expect(JSON.parse(delivered.body).type).toBe('webhook.test');

      // 最近投递徽章跳到投递日志并按本订阅筛选；结果写回需要一点时间，刷新直到出现成功
      await page.getByRole('button', { name: `View deliveries of ${name}` }).click();
      const deliveryRow = page
        .getByTestId('webhook-delivery-row')
        .filter({ hasText: 'webhook.test' });
      await expect(async () => {
        await page.getByRole('button', { name: 'Refresh' }).click();
        await expect(deliveryRow.getByText('Succeeded')).toBeVisible({ timeout: 1_000 });
      }).toPass({ timeout: 15_000 });

      await deliveryRow.getByRole('button', { name: 'View details' }).click();
      const sheet = page.getByTestId('webhook-delivery-sheet');
      await expect(sheet.getByTestId('webhook-delivery-sheet-status')).toHaveText('Succeeded');
      await expect(sheet.getByTestId('webhook-delivery-payload')).toContainText('"webhook.test"');
      await expect(sheet.getByTestId('webhook-attempts')).toContainText('200');
    } finally {
      await new Promise((resolve) => receiver.server.close(resolve));
    }
  },
);
