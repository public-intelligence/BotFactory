import { expect, test } from '@playwright/test';

const e2eEnabled = !!process.env.E2E_BASE_URL;

test.describe('login-chat-close smoke', () => {
  test.skip(!e2eEnabled, 'E2E_BASE_URL is not set');

  test('home loads', async ({ page }) => {
    await page.goto('/');
    await expect(page).toHaveTitle(/.+/);
  });

  test('public chat slug route responds', async ({ page }) => {
    const slug = process.env.E2E_CHAT_SLUG ?? 'sample-bot';
    const response = await page.goto(`/c/${slug}`);
    expect(response?.status()).toBeLessThan(500);
  });
});
