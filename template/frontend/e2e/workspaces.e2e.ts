//#if (TenantWorkspace)
import { expect, test } from '@playwright/test';

import { admin, authStatePath } from './support/accounts';
import { json } from './support/http';

test('signs in from the public root to the default workspace', async ({ page }) => {
  const documents: string[] = [];
  page.on('request', (request) => {
    if (request.isNavigationRequest() && request.frame() === page.mainFrame())
      documents.push(new URL(request.url()).pathname);
  });
  await page.goto('/');
  await expect(page).toHaveURL('/');
  await page.getByRole('link', { name: 'Sign in', exact: true }).first().click();
  await page.getByLabel('Username or Email').fill(admin().username);
  await page.getByLabel('Password', { exact: true }).fill(admin().password);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page).toHaveURL(/\/w\/default-e2e\/platform/);
  await expect(page.getByTestId('area-switcher')).toContainText('default-e2e');
  expect(documents).toEqual(['/']);
});

test.describe('workspace requests and routes', () => {
  test.use({ storageState: authStatePath(admin()) });

  for (const path of ['/platform', '/w/default-e2e/platform']) {
    test(`cold starts and reloads ${path}`, async ({ page }) => {
      await page.goto(path);
      await expect(page.getByRole('heading', { name: 'Dashboard', exact: true })).toBeVisible();
      await page.reload();
      await expect(page.getByRole('heading', { name: 'Dashboard', exact: true })).toBeVisible();
      await expect(page.getByTestId('area-switcher')).toBeVisible();
      await expect(page).toHaveURL(path);
    });
  }

  test('switches within a tab, preserves the other tab, and keeps an empty workspace shell', async ({
    page,
    context,
  }) => {
    const home = await json<{ id: string }>(await page.request.get('/api/v1/auth/me'));
    const suffix = Date.now().toString(36);
    const name = `switch-${suffix}`;
    const tenant = await json<{ id: string }>(
      await page.request.post('/api/v1/tenants', {
        data: { name, adminEmail: 'admin@example.test', adminPassword: 'Tenant!Passw0rd' },
      }),
    );
    const roles = await json<{ id: string; name: string }[]>(
      await page.request.get(`/api/v1/tenants/${tenant.id}/staff/roles`),
    );
    const adminRole = roles.find((role) => role.name === 'Admin')!;
    await json(
      await page.request.post(`/api/v1/tenants/${tenant.id}/staff`, {
        data: { homeUserId: home.id, roleIds: [adminRole.id] },
      }),
    );
    await page.goto('/w/default-e2e/platform/users');
    await expect(page.getByRole('heading', { name: 'User management', exact: true })).toBeVisible();
    const other = await context.newPage();
    await other.goto('/w/default-e2e/platform/users');
    await expect(
      other.getByRole('heading', { name: 'User management', exact: true }),
    ).toBeVisible();
    await page.evaluate(() => {
      (window as unknown as { workspaceHeap: number }).workspaceHeap = 42;
    });
    await page.getByTestId('area-switcher').click();
    await page.getByRole('menuitem', { name: new RegExp(name) }).click();
    await expect(page).toHaveURL(new RegExp(`/w/${name}/platform/users`));
    await expect(page.getByRole('heading', { name: 'User management', exact: true })).toBeVisible();
    expect(
      await page.evaluate(() => (window as unknown as { workspaceHeap: number }).workspaceHeap),
    ).toBe(42);
    await expect(other).toHaveURL(/\/w\/default-e2e\/platform\/users/);
    await page.goBack();
    await expect(page).toHaveURL(/\/w\/default-e2e\/platform\/users/);
    await page.goForward();
    await expect(page).toHaveURL(new RegExp(`/w/${name}/platform/users`));
    const headers = { 'X-Workspace': name };
    const emptyRole = await json<{ id: string }>(
      await page.request.post('/api/v1/roles', {
        headers,
        data: { name: `empty_${suffix}`, displayName: 'No features' },
      }),
    );
    await json(
      await page.request.post(`/api/v1/tenants/${tenant.id}/staff`, {
        data: { homeUserId: home.id, roleIds: [emptyRole.id] },
      }),
    );
    await page.reload();
    await expect(
      page.getByText(`No features are available in ${name} yet`, { exact: true }),
    ).toBeVisible();
    await expect(page.getByTestId('area-switcher')).toBeVisible();
    await expect(
      page.getByRole('button', { name: 'Back to platform console', exact: true }),
    ).toBeVisible();
    await page.setViewportSize({ width: 390, height: 844 });
    await page.getByRole('button', { name: 'Switch workspace', exact: true }).click();
    await expect(
      page.getByRole('menuitem', { name: 'Platform console', exact: true }),
    ).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(
      other.getByRole('heading', { name: 'User management', exact: true }),
    ).toBeVisible();
    await other.close();
  });

  test('grants a home account access through the query-driven staff sheet', async ({ page }) => {
    const tenants = await json<{ items: { id: string; name: string }[] }>(
      await page.request.get('/api/v1/tenants'),
    );
    const tenant = tenants.items.find((item) => item.name === 'default-e2e')!;
    await page.goto(`/platform/tenants?staff=${tenant.id}`);
    await page.getByRole('button', { name: 'Grant access', exact: true }).click();
    const dialog = page.getByRole('dialog').last();
    await dialog.getByRole('combobox').fill('e2e_viewer');
    await dialog.getByRole('option', { name: /e2e_viewer/ }).click();
    await dialog.getByRole('checkbox', { name: 'Administrator', exact: true }).click();
    await dialog.getByRole('button', { name: 'Save', exact: true }).click();
    await expect(dialog).not.toBeVisible();
    await expect(page.getByRole('cell', { name: /e2e_viewer/ }).first()).toBeVisible();
  });
});
//#endif
