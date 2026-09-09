// Responsive browser checks against a Release publish. Usage: node responsive-ui.cjs <publish-directory>
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { createAppFixture } = require('./browser-fixture.cjs');

const viewports = [
  { name: 'phone-320', width: 320, height: 568 },
  { name: 'phone-360', width: 360, height: 800 },
  { name: 'phone-390', width: 390, height: 844 },
  { name: 'landscape-568', width: 568, height: 320 },
  { name: 'landscape-844', width: 844, height: 390 },
  { name: 'small-640', width: 640, height: 800 },
  { name: 'small-641', width: 641, height: 800 },
  { name: 'tablet-768', width: 768, height: 1024 },
  { name: 'tablet-1024', width: 1024, height: 768 },
  { name: 'desktop-1280', width: 1280, height: 800 },
  { name: 'desktop-1440', width: 1440, height: 900 },
  { name: 'desktop-1920', width: 1920, height: 1080 }
];

const routes = [
  { name: 'lobby', path: '/', root: '.lobby-page', control: '.session-btn' },
  { name: 'dice', path: '/wuerfel', root: '.wuerfel-page-container', control: 'button' },
  { name: 'combat', path: '/kampf', root: '.combat-page', control: '.action-button' },
  { name: 'heroes', path: '/helden-verwaltung', root: '.helden-verwaltung-page', control: '.drop-zone' }
];

async function waitFor(test, label) {
  for (let i = 0; i < 100; i++) {
    if (await test()) return;
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  throw Error(label);
}

async function waitForStableLayout(page, rootSelector) {
  let previous = null;
  let stableSamples = 0;
  for (let i = 0; i < 25; i++) {
    const current = await page.locator(rootSelector).evaluate(root => {
      const rect = root.getBoundingClientRect();
      const round = value => Math.round(value * 100) / 100;
      return {
        x: round(rect.x),
        y: round(rect.y),
        width: round(rect.width),
        height: round(rect.height),
        documentHeight: round(document.documentElement.scrollHeight)
      };
    });
    if (current.width > 0 && previous && JSON.stringify(current) === JSON.stringify(previous)) stableSamples++;
    else stableSamples = 0;
    if (stableSamples >= 3) return;
    previous = current;
    await page.waitForTimeout(100);
  }
  throw Error(`${rootSelector} layout did not settle`);
}

async function assertNoHorizontalOverflow(page, label) {
  const geometry = await page.evaluate(() => ({
    clientWidth: document.documentElement.clientWidth,
    scrollWidth: document.documentElement.scrollWidth,
    bodyScrollWidth: document.body.scrollWidth
  }));
  assert.ok(geometry.scrollWidth <= geometry.clientWidth + 1, `${label}: document overflow ${JSON.stringify(geometry)}`);
  assert.ok(geometry.bodyScrollWidth <= geometry.clientWidth + 1, `${label}: body overflow ${JSON.stringify(geometry)}`);
}

async function assertReachableControl(page, selector, label) {
  const control = page.locator(selector).filter({ visible: true }).first();
  await control.waitFor({ state: 'attached' });
  await control.scrollIntoViewIfNeeded();
  const geometry = await control.evaluate(element => {
    const rect = element.getBoundingClientRect();
    return { left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom, width: rect.width, height: rect.height };
  });
  assert.ok(geometry.width > 0 && geometry.height > 0, `${label}: control has no size`);
  assert.ok(geometry.left >= -1 && geometry.right <= page.viewportSize().width + 1, `${label}: control is clipped ${JSON.stringify(geometry)}`);
  assert.ok(geometry.top >= -1 && geometry.bottom <= page.viewportSize().height + 1, `${label}: control is not reachable ${JSON.stringify(geometry)}`);
}

async function assertMobileMenu(page, label) {
  const menuButton = page.getByRole('button', { name: 'Menü', exact: true });
  await menuButton.waitFor({ state: 'visible', timeout: 1000 });
  const controlledId = await menuButton.getAttribute('aria-controls');
  assert.ok(controlledId, `${label}: menu has no aria-controls`);
  const menuContent = page.locator(`#${controlledId}`);
  assert.equal(await menuButton.getAttribute('aria-expanded'), 'false', `${label}: menu starts open`);
  assert.equal(await menuContent.isVisible(), false, `${label}: closed menu is visible`);

  await menuButton.click();
  assert.equal(await menuButton.getAttribute('aria-expanded'), 'true', `${label}: menu did not open`);
  await page.getByRole('link', { name: 'Würfeln', exact: true }).waitFor({ state: 'visible' });
  await assertNoHorizontalOverflow(page, `${label} open`);

  await menuButton.press('Escape');
  assert.equal(await menuButton.getAttribute('aria-expanded'), 'false', `${label}: Escape did not close menu`);
  assert.equal(await page.evaluate(() => document.activeElement?.getAttribute('aria-label')), 'Menü', `${label}: focus was not returned to menu button`);
}

async function setDesktopSidebar(page, expanded) {
  const checkbox = page.locator('#nav-expanded');
  if ((await checkbox.isChecked()) !== expanded) await page.locator('.nav-expand-button').click();
  await page.waitForTimeout(50);
}

async function saveBaselineScreenshot(page, screenshotDirectory, routeName, sidebarState) {
  if (!screenshotDirectory) return;
  const file = path.join(screenshotDirectory, `${routeName}-${sidebarState}.png`);
  try {
    await fs.access(file);
    throw Error(`baseline screenshot already exists: ${file}`);
  } catch (error) {
    if (error.code !== 'ENOENT') throw error;
  }
  await page.screenshot({ path: file, animations: 'disabled', fullPage: true });
}

(async () => {
  const fixture = await createAppFixture(process.argv[2], { userCount: 3 });
  let pages = [];
  try {
    const prepared = await fixture.prepare();
    pages = prepared.pages;
    const page = pages[0];
    const { origin, users, errors } = prepared;
    const session = {};

    await page.getByRole('button', { name: 'Erstellen', exact: true }).click();
    await page.getByPlaceholder('z.B. Borbarads Erben').fill('Responsive Browserrunde');
    await page.getByRole('button', { name: 'Session Erstellen', exact: true }).click();
    await page.locator('.wuerfel-page-container').waitFor();
    const sessions = await page.evaluate(async () => await (await fetch('/api/sessions/mine')).json());
    Object.assign(session, sessions[0]);
    assert.ok(session.sessionId);
    for (const otherPage of pages.slice(1)) {
      await otherPage.getByPlaceholder('z.B. X7K9').fill(session.joinCode);
      await otherPage.getByRole('button', { name: 'Sitzung Beitreten', exact: true }).click();
      await otherPage.locator('.wuerfel-page-container').waitFor();
    }
    const details = () => page.evaluate(async id => await (await fetch('/api/sessions/' + id)).json(), session.sessionId);
    await waitFor(async () => {
      const detail = await details();
      return detail.players.length === 3 && detail.players.every(player => player.activeHeroId);
    }, 'responsive fixture hero sync missing');

    const failures = [];
    const check = async (label, action) => {
      try {
        await action();
      } catch (error) {
        failures.push(`${label}: ${error.message}`);
        console.error(`FAIL ${label}: ${error.message}`);
      }
    };

    const screenshotDirectory = process.env.RESPONSIVE_SCREENSHOT_DIR ? path.resolve(process.env.RESPONSIVE_SCREENSHOT_DIR) : null;
    if (screenshotDirectory) await fs.mkdir(screenshotDirectory, { recursive: true });
    for (const route of routes) {
      await page.setViewportSize({ width: 1440, height: 900 });
      await page.goto(origin + route.path);
      await page.locator(route.root).waitFor();
      await waitForStableLayout(page, route.root);
      for (const sidebarState of ['collapsed', 'expanded']) {
        await setDesktopSidebar(page, sidebarState === 'expanded');
        await waitForStableLayout(page, route.root);
        await check(`desktop ${route.name} ${sidebarState} overflow`, () => assertNoHorizontalOverflow(page, `desktop ${route.name} ${sidebarState}`));
        await check(`desktop ${route.name} ${sidebarState} control`, () => assertReachableControl(page, route.control, `desktop ${route.name} ${sidebarState}`));
        await saveBaselineScreenshot(page, screenshotDirectory, route.name, sidebarState);
      }
    }

    for (const viewport of viewports) {
      for (const route of routes) {
        await page.setViewportSize({ width: viewport.width, height: viewport.height });
        await page.goto(origin + route.path);
        await page.locator(route.root).waitFor();
        await waitForStableLayout(page, route.root);
        await check(`${viewport.name} ${route.name} overflow`, () => assertNoHorizontalOverflow(page, `${viewport.name} ${route.name}`));
        await check(`${viewport.name} ${route.name} control`, () => assertReachableControl(page, route.control, `${viewport.name} ${route.name}`));
        if (route.name === 'lobby' && viewport.width < 641) {
          await check(`${viewport.name} mobile navigation`, () => assertMobileMenu(page, viewport.name));
        }
      }
    }

    assert.deepEqual(errors, [], `browser errors: ${errors.join('; ')}`);
    if (failures.length > 0) throw Error(`${failures.length} responsive checks failed\n${failures.join('\n')}`);
    console.log(JSON.stringify({ viewports: viewports.length, routes: routes.length, sessionId: session.sessionId, users: users.length }));
  } finally {
    await fixture.close();
  }
})().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
