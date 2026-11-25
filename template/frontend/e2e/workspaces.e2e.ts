//#if (TenantWorkspace)
import { expect, test } from '@playwright/test';

import { admin, authStatePath } from './support/accounts';
import { json } from './support/http';

test(
  'signs in from the public root to the default workspace',
  { tag: '@proves:auth-session' },
  async ({ page }) => {
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
    await expect(page.locator('app-system-switcher')).toBeVisible();
    await expect(page.getByTestId('tenant-selector')).not.toBeVisible();
    expect(documents).toEqual(['/']);
  },
);

test.describe('workspace requests and routes', () => {
  test.use({ storageState: authStatePath(admin()) });

  for (const path of ['/platform', '/w/default-e2e/platform']) {
    test(`cold starts and reloads ${path}`, { tag: '@proves:gateway-route' }, async ({ page }) => {
      await page.goto(path);
      await expect(page.getByRole('heading', { name: 'Dashboard', exact: true })).toBeVisible();
      await page.reload();
      await expect(page.getByRole('heading', { name: 'Dashboard', exact: true })).toBeVisible();
      await expect(page.locator('app-system-switcher')).toBeVisible();
      await expect(page).toHaveURL(path);
    });
  }

  test(
    'switches within a tab, preserves the other tab, and keeps an empty workspace shell',
    { tag: '@proves:main-flow' },
    async ({ page, context }) => {
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
      await expect(
        page.getByRole('heading', { name: 'User management', exact: true }),
      ).toBeVisible();
      const other = await context.newPage();
      await other.goto('/w/default-e2e/platform/users');
      await expect(
        other.getByRole('heading', { name: 'User management', exact: true }),
      ).toBeVisible();
      await page.evaluate(() => {
        (window as unknown as { workspaceHeap: number }).workspaceHeap = 42;
      });
      await page.getByTestId('tenant-selector').getByRole('combobox').fill(name);
      await page.getByRole('option', { name, exact: true }).click();
      await expect(page).toHaveURL(new RegExp(`/w/${name}/platform/users`));
      await expect(
        page.getByRole('heading', { name: 'User management', exact: true }),
      ).toBeVisible();
      expect(
        await page.evaluate(() => (window as unknown as { workspaceHeap: number }).workspaceHeap),
      ).toBe(42);
      await expect(other).toHaveURL(/\/w\/default-e2e\/platform\/users/);
      await page.goBack();
      await expect(page).toHaveURL(/\/w\/default-e2e\/platform\/users/);
      await page.goForward();
      await expect(page).toHaveURL(new RegExp(`/w/${name}/platform/users`));
      await page.getByTestId('platform-management').click();
      await expect(page).toHaveURL('/platform');
      await expect(page.getByTestId('tenant-selector')).not.toBeVisible();
      await expect(page.getByTestId('return-workspace')).toContainText(name);
      await page.getByTestId('return-workspace').click();
      await expect(page).toHaveURL(`/w/${name}/platform`);
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
      await expect(page.locator('app-system-switcher')).toBeVisible();
      await expect(
        page.getByRole('button', { name: 'Back to platform management', exact: true }),
      ).toBeVisible();
      await page.setViewportSize({ width: 390, height: 844 });
      await page.getByRole('button', { name: 'Switch workspace', exact: true }).click();
      await expect(page.getByRole('option', { name, exact: true })).toBeVisible();
      await page.keyboard.press('Escape');
      await expect(page.getByTestId('platform-management')).toBeVisible();
      await expect(
        other.getByRole('heading', { name: 'User management', exact: true }),
      ).toBeVisible();
      await other.close();
    },
  );
  //#if (OpenIddictServer || RemoteTokenAuth)

  test(
    'navigates to another origin with the current workspace path',
    { tag: '@proves:main-flow' },
    async ({ page }) => {
      await page.setViewportSize({ width: 1440, height: 900 });
      await page.route('**/api/v1/systems', (route) =>
        route.fulfill({
          json: [
            {
              key: 'identity',
              displayName: 'Identity',
              homeUrl: 'https://identity.example.test/',
              isCurrent: true,
            },
            {
              key: 'orders',
              displayName: 'Orders',
              homeUrl: 'https://orders.example.test/',
              isCurrent: false,
            },
          ],
        }),
      );
      await page.route('https://orders.example.test/**', (route) =>
        route.fulfill({ contentType: 'text/html', body: '<h1>Orders</h1>' }),
      );
      await page.goto('/w/default-e2e/platform');
      await page.getByTestId('system-switcher').click();
      await page.getByRole('menuitem', { name: 'Orders', exact: true }).click();
      await expect(page).toHaveURL('https://orders.example.test/w/default-e2e/platform');
    },
  );
  //#endif

  test(
    'opens a newly granted workspace in the same session',
    { tag: '@proves:main-flow' },
    async ({ page }) => {
      const name = `grant-${Date.now().toString(36)}`;
      const tenant = await json<{ id: string }>(
        await page.request.post('/api/v1/tenants', { data: { name } }),
      );
      const documents: string[] = [];
      page.on('request', (request) => {
        if (request.isNavigationRequest() && request.frame() === page.mainFrame()) {
          documents.push(new URL(request.url()).pathname);
        }
      });
      await page.goto(`/platform/tenants?staff=${tenant.id}`);
      // 没有平台人员时空态里还有一个同名按钮，取工具栏那个
      await page.getByRole('button', { name: 'Grant access', exact: true }).first().click();
      const dialog = page.getByRole('dialog').last();
      const person = dialog.getByRole('combobox', { name: 'Platform staff' });
      // 打开动画结束、首个字段获得焦点后再操作：刚打开的瞬间点击会被遮罩截获
      await expect(person).toBeFocused();
      // 未选人员和角色时提交：在字段下报错，不发请求
      await dialog.getByRole('button', { name: 'Grant access', exact: true }).click();
      await expect(dialog.getByText('This field is required.').first()).toBeVisible();
      await expect(person).toBeFocused();
      await person.click();
      // 选择框的弹出层挂在对话框之外，搜索框与选项要从页面上找
      const username = admin().username;
      await page.getByPlaceholder('Search by name or username').fill(username);
      await page.getByRole('option', { name: new RegExp(username, 'i') }).click();
      await expect(
        dialog.getByText(`A new linked account uses platform.${username}`),
      ).toBeVisible();
      await dialog.getByRole('checkbox', { name: /^Administrator\b/ }).click();
      const directory = page.waitForResponse(
        (response) =>
          new URL(response.url()).pathname === '/api/v1/auth/me/workspaces' && response.ok(),
      );
      await dialog.getByRole('button', { name: 'Grant access', exact: true }).click();
      await expect(dialog).not.toBeVisible();
      await expect(page.getByText(new RegExp(`Granted .* access to ${name}`))).toBeVisible();
      const workspaces = await (await directory).json();
      expect(workspaces).toContainEqual(expect.objectContaining({ name }));
      await page.keyboard.press('Escape');
      await expect(page).toHaveURL('/platform/tenants');
      await page.getByTestId('return-workspace').click();
      await expect(page).toHaveURL('/w/default-e2e/platform');
      await page.getByTestId('tenant-selector').getByRole('combobox').fill(name);
      await page.getByRole('option', { name, exact: true }).click();
      await expect(page).toHaveURL(`/w/${name}/platform`);
      expect(documents).toEqual(['/platform/tenants']);
    },
  );
});
//#endif
