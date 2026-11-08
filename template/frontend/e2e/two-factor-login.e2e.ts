import { randomBytes } from 'node:crypto';

import { expect, request, test } from '@playwright/test';

import { admin, signIn } from './support/accounts';
import { json } from './support/http';
import { freshTotp } from './support/totp';

// 口令一步之后，第二步在独立页面上：待完成的登录由服务端的 HttpOnly 临时 Cookie 承载，
// 刷新页面照样接着做，完成后回到原目标。设置时刚用过的验证码同一步内不能再用，这里用恢复码完成
test(
  'resumes the second step after a page reload and returns to the original target',
  { tag: '@auth' },
  async ({ page, baseURL }) => {
    const username = `tfa_${randomBytes(4).toString('hex')}`;
    const password = `Tfa!${randomBytes(12).toString('hex')}Aa1`;
    let recoveryCode: string;

    const host = await request.newContext({ baseURL });
    const owner = await request.newContext({ baseURL });
    try {
      await signIn(host, admin());
      await json(
        await host.post('/api/v1/users', {
          data: { username, email: `${username}@example.com`, password, isEmailVerified: true },
        }),
      );
      await signIn(owner, { key: 'admin', username, password });
      const setup = await json<{ setupId: string; secret: string }>(
        await owner.post('/api/v1/auth/me/two-factor/setup', { data: {} }),
      );
      const enabled = await json<{ recoveryCodes: string[] }>(
        await owner.post('/api/v1/auth/me/two-factor/enable', {
          data: { setupId: setup.setupId, code: await freshTotp(setup.secret) },
        }),
      );
      recoveryCode = enabled.recoveryCodes[0]!;
    } finally {
      await host.dispose();
      await owner.dispose();
    }

    await page.goto('/auth/login?returnUrl=%2Fworkspace');
    await page.getByLabel('Username or Email').fill(username);
    await page.getByLabel('Password', { exact: true }).fill(password);
    await page.getByRole('button', { name: 'Sign in', exact: true }).click();

    await expect(page).toHaveURL(/\/auth\/two-factor\?returnUrl=%2Fworkspace$/);
    await expect(page.getByTestId('two-factor-challenge')).toBeVisible();

    await page.reload();
    await expect(page.getByTestId('two-factor-challenge')).toBeVisible();
    await page.getByTestId('two-factor-toggle-mode').click();
    await page.locator('#two-factor-recovery-code').fill(recoveryCode);
    await page.getByTestId('two-factor-submit').click();

    await expect(page).toHaveURL(/\/workspace/);
    const me = await json<{ username: string }>(await page.request.get('/api/v1/auth/me'));
    expect(me.username).toBe(username);
  },
);
