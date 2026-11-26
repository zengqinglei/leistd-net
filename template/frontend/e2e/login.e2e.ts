import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

import { chromium, expect, test } from '@playwright/test';

import { admin } from './support/accounts';

for (const keepSignedIn of [false, true]) {
  test(
    `stays signed in after reopening the browser with keep-signed-in ${keepSignedIn ? 'on' : 'off'}`,
    { tag: ['@auth', '@proves:auth-session'] },
    async ({ baseURL }, testInfo) => {
      const account = admin();
      const profile = await mkdtemp(join(tmpdir(), 'e2e-session-profile-'));
      const launch = () =>
        chromium.launchPersistentContext(profile, {
          baseURL,
          locale: 'en-US',
          headless: testInfo.project.use.headless,
        });
      let browser = await launch();
      try {
        const page = browser.pages()[0]!;
        await page.goto('/auth/login');
        await page.getByLabel('Username or Email').fill(account.username);
        await page.getByLabel('Password', { exact: true }).fill(account.password);
        const choice = page.getByRole('checkbox', { name: 'Keep me signed in' });
        await expect(choice).not.toBeChecked();
        if (keepSignedIn) {
          await choice.click();
          await expect(choice).toBeChecked();
        }
        await page.getByRole('button', { name: 'Sign in', exact: true }).click();

        await expect(page).not.toHaveURL(/\/auth\/login/);
        const cookie = (await browser.cookies()).find((item) => item.name.endsWith('.Auth'));
        const seconds = (keepSignedIn ? 30 : 1) * 86_400;
        expect(cookie!.expires - Date.now() / 1_000).toBeGreaterThan(seconds - 60);
        expect(cookie!.expires - Date.now() / 1_000).toBeLessThanOrEqual(seconds + 5);

        await browser.close();
        browser = await launch();
        const reopened = browser.pages()[0]!;
        await reopened.goto('/platform/roles');
        await expect(reopened.getByRole('row').filter({ hasText: 'E2E viewer' })).toBeVisible();
      } finally {
        await browser.close();
        await rm(profile, { recursive: true, force: true });
      }
    },
  );
}
