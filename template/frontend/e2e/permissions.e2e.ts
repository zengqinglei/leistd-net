import { expect, test } from '@playwright/test';

import { authStatePath, restricted } from './support/accounts';

test.use({ storageState: authStatePath(restricted()) });

test(
  'opens the protected role list with a restricted session',
  { tag: ['@auth', '@proves:auth-session'] },
  async ({ page }) => {
    await page.goto('/platform/roles');
    await expect(page.getByRole('row').filter({ hasText: 'E2E viewer' })).toBeVisible();
  },
);
