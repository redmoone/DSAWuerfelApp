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

async function assertManagementLayout(page, routeName, label) {
  const layout = await page.evaluate(route => {
    const read = selector => {
      const element = document.querySelector(selector);
      if (!element) return null;
      const rect = element.getBoundingClientRect();
      const style = getComputedStyle(element);
      return {
        top: rect.top,
        bottom: rect.bottom,
        left: rect.left,
        right: rect.right,
        width: rect.width,
        height: rect.height,
        gridTemplateColumns: style.gridTemplateColumns,
        borderTopWidth: style.borderTopWidth,
        boxShadow: style.boxShadow
      };
    };
    const selectors = route === 'lobby'
      ? { shell: '.lobby-shell', list: '.session-board', form: '.session-workbench', panel: '.session-board', active: '.session-node.active' }
      : { shell: '.helden-verwaltung-shell', list: '.hero-list-panel', form: '.hero-import-panel', panel: '.hero-list-panel', active: '.hero-status.active' };
    return {
      shell: read(selectors.shell),
      list: read(selectors.list),
      form: read(selectors.form),
      panel: read(selectors.panel),
      active: read(selectors.active),
      viewportWidth: document.documentElement.clientWidth
    };
  }, routeName);

  assert.ok(layout.shell && layout.list && layout.form, `${label}: management geometry is incomplete`);
  assert.equal(layout.shell.boxShadow, 'none', `${label}: shell has a shadow`);
  assert.equal(layout.list.borderTopWidth, '1px', `${label}: list panel border is not 1px`);
  assert.equal(layout.form.borderTopWidth, '1px', `${label}: form panel border is not 1px`);
  const columnCount = layout.shell.gridTemplateColumns.trim().split(/\s+/).length;

  if (page.viewportSize().width <= 900) {
    assert.ok(layout.form.top >= layout.list.bottom - 1, `${label}: form panel does not follow list panel`);
    assert.equal(columnCount, 1, `${label}: expected one management column`);
  } else {
    assert.ok(Math.abs(layout.form.top - layout.list.top) <= 2, `${label}: management panels are not side by side`);
    assert.ok(layout.form.left >= layout.list.right - 1, `${label}: management panels overlap`);
    assert.equal(columnCount, 2, `${label}: expected two management columns`);
  }

  const controlSelector = routeName === 'lobby'
    ? '.session-board button, .session-workbench button, .session-workbench input'
    : '.hero-list-panel button, .hero-import-panel button, .hero-import-panel input, .hero-import-panel label';
  const controls = await page.locator(controlSelector).evaluateAll(elements => elements.map(element => {
    const rect = element.getBoundingClientRect();
    return { height: rect.height, left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom, tag: element.tagName };
  }));
  assert.ok(controls.length > 0, `${label}: no management controls rendered`);
  for (const control of controls) {
    assert.ok(control.height >= 43, `${label}: ${control.tag} control is shorter than 44px (${control.height})`);
    assert.ok(control.left >= -1 && control.right <= page.viewportSize().width + 1, `${label}: control is clipped ${JSON.stringify(control)}`);
  }

  const focusTarget = routeName === 'lobby'
    ? page.locator('.session-player-name, .mode-pill, .session-toggle').first()
    : page.locator('#hero-files, .management-btn, .jump-link').first();
  await focusTarget.focus();
  const focused = await focusTarget.evaluate(element => {
    const rect = element.getBoundingClientRect();
    return { left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom };
  });
  assert.ok(focused.left >= -1 && focused.right <= page.viewportSize().width + 1 && focused.top >= -1 && focused.bottom <= page.viewportSize().height + 1, `${label}: focused control is clipped ${JSON.stringify(focused)}`);

  if (routeName === 'lobby') {
    const active = page.locator('.lobby-page .session-node.active').first();
    await active.waitFor({ state: 'visible' });
    const renameButton = active.getByRole('button', { name: 'Umbenennen', exact: true });
    await renameButton.click();
    const edit = active.locator('.session-edit-input');
    await edit.fill('Eine sehr lange Sessionbezeichnung für den Umbruch an schmalen Arbeitsflächen');
    await assertNoHorizontalOverflow(page, `${label} long session editor`);
    await active.getByRole('button', { name: 'Abbrechen', exact: true }).click();
    await edit.waitFor({ state: 'hidden' });

    const deleteButton = active.getByRole('button', { name: 'Löschen', exact: true });
    await deleteButton.click();
    await active.locator('.session-confirmation').waitFor({ state: 'visible' });
    await assertNoHorizontalOverflow(page, `${label} session confirmation`);
    await active.locator('.session-confirmation').getByRole('button', { name: 'Abbrechen', exact: true }).click();
  } else {
    const fileInput = page.locator('#hero-files');
    await fileInput.setInputFiles({
      name: 'eine-sehr-lange-heldendatei-fuer-den-umbruchtest-mit-mehr-als-einhundertzeichen.xml',
      mimeType: 'text/xml',
      buffer: Buffer.from('<not-a-valid-hero />')
    });
    await page.getByText('eine-sehr-lange-heldendatei-fuer-den-umbruchtest-mit-mehr-als-einhundertzeichen.xml', { exact: true }).waitFor();
    await assertNoHorizontalOverflow(page, `${label} long file name`);
    await fileInput.setInputFiles([]);

    const removeButton = page.getByRole('button', { name: 'Entfernen', exact: true }).first();
    await removeButton.click();
    await page.locator('.hero-confirmation').waitFor({ state: 'visible' });
    await assertNoHorizontalOverflow(page, `${label} hero confirmation`);
    await page.locator('.hero-confirmation').getByRole('button', { name: 'Abbrechen', exact: true }).click();
  }
}

const diceModes = [
  { button: 'mode-probe', panel: '.probe-setup-section' },
  { button: 'mode-attribute', panel: '.attribute-setup-section' },
  { button: 'mode-bad-trait', panel: '.bad-trait-setup-section' },
  { button: 'mode-free-roll', panel: '.free-roll-setup-section' }
];

async function assertDiceMode(page, mode, label) {
  const modeButton = page.locator(`[data-testid="${mode.button}"]`);
  await modeButton.click();
  await page.waitForTimeout(40);

  const activeModes = page.locator('.roll-mode-button.active');
  assert.equal(await activeModes.count(), 1, `${label}: expected exactly one active mode`);
  assert.equal(await activeModes.first().getAttribute('data-testid'), mode.button, `${label}: active mode mismatch`);
  assert.equal((await modeButton.getAttribute('aria-pressed')).toLowerCase(), 'true', `${label}: aria-pressed is not true`);

  const visiblePanels = await Promise.all(
    diceModes.map(({ panel }) => page.locator(`${panel}:visible`).count())
  );
  assert.equal(visiblePanels.reduce((sum, count) => sum + count, 0), 1, `${label}: more than one full mode panel is visible`);
  assert.equal(visiblePanels[diceModes.indexOf(mode)], 1, `${label}: selected mode panel is not visible`);

  const buttonHeight = await modeButton.evaluate(element => element.getBoundingClientRect().height);
  assert.ok(buttonHeight >= 43, `${label}: mode button is shorter than 44px (${buttonHeight})`);

  if (mode.button === 'mode-bad-trait') {
    assert.equal(await page.locator('.results-bar').count(), 0, `${label}: shared action bar should be hidden for bad traits`);
    const trait = page.locator('.bad-trait-panel:not(.compact) .bad-trait-chip').first();
    await trait.waitFor({ state: 'visible' });
    await trait.click();
    await assertReachableControl(page, '.bad-trait-panel:not(.compact) .dsa-btn', `${label} primary action`);
  } else {
    assert.equal(await page.locator('.results-bar').count(), 1, `${label}: expected exactly one shared action bar`);
    await assertReachableControl(page, '.results-bar button.dsa-btn', `${label} primary action`);
  }

  await assertNoHorizontalOverflow(page, label);
}

async function assertDiceLayout(page, label) {
  const layout = await page.evaluate(() => {
    const readRect = selector => {
      const element = document.querySelector(selector);
      if (!element) return null;
      const rect = element.getBoundingClientRect();
      return { top: rect.top, bottom: rect.bottom, left: rect.left, right: rect.right, width: rect.width, height: rect.height };
    };
    return {
      setup: readRect('.roll-setup-panel'),
      feedback: readRect('.roll-feedback-column'),
      dice: readRect('.dice-3d-box'),
      actionBar: readRect('.results-bar'),
      history: readRect('.history-panel'),
      actionBarCount: document.querySelectorAll('.results-bar').length,
      currentRollCardCount: document.querySelectorAll('.current-roll-card').length,
      diceCanvasCount: document.querySelectorAll('.dice-3d-box canvas').length
    };
  });
  assert.ok(layout.setup && layout.feedback && layout.dice && layout.actionBar && layout.history, `${label}: workbench geometry is incomplete`);
  assert.equal(layout.actionBarCount, 1, `${label}: expected exactly one shared action bar`);
  assert.equal(layout.currentRollCardCount, 0, `${label}: permanent current-roll card is still rendered`);
  assert.equal(layout.diceCanvasCount, 1, `${label}: expected exactly one 3D dice canvas`);
  assert.ok(layout.dice.height >= 120 && layout.dice.height <= 140, `${label}: compact dice area is ${layout.dice.height}px tall`);
  if (page.viewportSize().width >= 901) {
    assert.ok(Math.abs(layout.setup.top - layout.feedback.top) <= 2, `${label}: setup and feedback are not side by side`);
    assert.ok(layout.feedback.left >= layout.setup.right - 1, `${label}: feedback column overlaps setup`);
  } else {
    assert.ok(layout.feedback.top >= layout.setup.bottom - 1, `${label}: feedback does not follow setup`);
  }
  assert.ok(layout.actionBar.top >= layout.dice.bottom - 1, `${label}: action bar does not follow the dice area`);
  assert.ok(layout.history.top >= layout.actionBar.bottom - 1, `${label}: history does not follow the action bar`);
}

async function assertHistoryViewport(page, label) {
  await page.locator('.history-panel').waitFor({ state: 'visible' });
  await waitFor(async () => await page.locator('.history-entry').count() >= 6, `${label}: history entries did not load`);
  const history = await page.locator('.roll-history-list').evaluate(list => {
    const listRect = list.getBoundingClientRect();
    const entries = [...list.querySelectorAll('.history-entry')].map(entry => {
      const rect = entry.getBoundingClientRect();
      return { top: rect.top, bottom: rect.bottom };
    });
    const panel = list.closest('.history-panel');
    const scrollOwners = [panel, ...panel.querySelectorAll('*')].filter(element => {
      const style = getComputedStyle(element);
      return /(auto|scroll)/.test(style.overflowY) && element.scrollHeight > element.clientHeight + 1;
    });
    return {
      listTop: listRect.top,
      listBottom: listRect.bottom,
      clientHeight: list.clientHeight,
      scrollHeight: list.scrollHeight,
      entries,
      scrollOwnerCount: scrollOwners.length
    };
  });
  assert.ok(history.clientHeight > 0, `${label}: history list has no visible height`);
  assert.ok(history.scrollHeight > history.clientHeight, `${label}: history list is not scrollable`);
  const visibleEntries = history.entries.filter(entry => entry.top >= history.listTop - 1 && entry.bottom <= history.listBottom + 1).length;
  const minimumVisibleEntries = page.viewportSize().width <= 900 ? 3 : 5;
  assert.ok(visibleEntries >= minimumVisibleEntries, `${label}: only ${visibleEntries} history entries are visible`);
  assert.equal(history.scrollOwnerCount, 1, `${label}: history has more than one active scroll owner`);

  const historyList = page.locator('.roll-history-list');
  await historyList.evaluate(list => { list.scrollTop = list.scrollHeight; });
  await page.waitForTimeout(20);
  const lastEntry = historyList.locator('.history-entry').last();
  const lastVisible = await lastEntry.evaluate((entry, list) => {
    const entryRect = entry.getBoundingClientRect();
    const listRect = list.getBoundingClientRect();
    return entryRect.bottom <= listRect.bottom + 1 && entryRect.top >= listRect.top - 1;
  }, await historyList.elementHandle());
  assert.ok(lastVisible, `${label}: oldest history entry cannot be reached in its scroll area`);
  await historyList.evaluate(list => { list.scrollTop = 0; });
}

async function assertProbeInfoPresentation(page, label) {
  await page.locator('[data-testid="mode-probe"]').click();
  const search = page.locator('.search-input');
  await search.fill('Abvenenum');
  await page.getByRole('button', { name: /Abvenenum/ }).click();
  await page.locator('.probe-info-summary').waitFor({ state: 'visible' });
  await page.locator('.probe-info-button').click();
  const details = page.locator('.probe-info-details');
  await details.waitFor({ state: 'visible' });
  const presentation = await details.evaluate(element => {
    const rect = element.getBoundingClientRect();
    const style = getComputedStyle(element);
    return { position: style.position, left: rect.left, right: rect.right, bottom: rect.bottom, width: rect.width };
  });
  if (page.viewportSize().width <= 640) {
    assert.equal(presentation.position, 'fixed', `${label}: mobile info sheet is not fixed`);
    assert.ok(presentation.left <= 1 && presentation.width >= page.viewportSize().width - 1, `${label}: info sheet is not full width`);
    assert.ok(presentation.bottom >= page.viewportSize().height - 1, `${label}: info sheet is not anchored at the bottom`);
  } else {
    assert.equal(presentation.position, 'fixed', `${label}: desktop info panel is not fixed`);
    assert.ok(presentation.left > page.viewportSize().width / 2, `${label}: desktop info panel is not on the right`);
    assert.ok(presentation.right <= page.viewportSize().width + 1, `${label}: desktop info panel is clipped`);
  }
  await page.getByRole('button', { name: 'Probeninformationen schliessen', exact: true }).click();
  await details.waitFor({ state: 'hidden' });
  await assertNoHorizontalOverflow(page, `${label} after close`);
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
    await page.getByPlaceholder('z.B. Borbarads Erben').fill('Responsive Browserrunde mit langer Sessionbezeichnung');
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

    const seedPage = pages[1];
    const historyStart = (await details()).history.length;
    await seedPage.getByRole('button', { name: 'Freier Wurf', exact: true }).click();
    const sixSidedDie = seedPage.locator('.die-selector').filter({ has: seedPage.getByText('6', { exact: true }) }).first();
    for (let index = 0; index < 6; index++) {
      const action = seedPage.locator('.results-bar button.dsa-btn');
      if (await action.isDisabled()) await sixSidedDie.click();
      await action.click();
      await waitFor(async () => (await details()).history.length === historyStart + index + 1, `responsive history seed ${index + 1} missing`);
    }

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
        if (route.name === 'dice') {
          await check(`${viewport.name} dice modes`, async () => {
            for (const mode of diceModes) {
              await assertDiceMode(page, mode, `${viewport.name} ${mode.button}`);
            }
            await assertDiceLayout(page, `${viewport.name} dice layout`);
            await assertHistoryViewport(page, `${viewport.name} dice history`);
          });
          if (viewport.name === 'desktop-1440' || viewport.name === 'phone-390') {
            await check(`${viewport.name} probe info`, () => assertProbeInfoPresentation(page, `${viewport.name} probe info`));
          }
          if (screenshotDirectory && viewport.name === 'phone-390') {
            await page.screenshot({ path: path.join(screenshotDirectory, 'dice-phone-390.png'), animations: 'disabled', fullPage: true });
          }
        }
        if (route.name === 'lobby' && viewport.width < 641) {
          await check(`${viewport.name} mobile navigation`, () => assertMobileMenu(page, viewport.name));
        }
      }
    }

    for (const viewport of [
      { name: 'boundary-899', width: 899, height: 700 },
      { name: 'boundary-900', width: 900, height: 700 },
      { name: 'boundary-901', width: 901, height: 700 }
    ]) {
      for (const route of [
        { name: 'lobby', path: '/', root: '.lobby-page' },
        { name: 'heroes', path: '/helden-verwaltung', root: '.helden-verwaltung-page' }
      ]) {
        await page.setViewportSize({ width: viewport.width, height: viewport.height });
        await page.goto(origin + route.path);
        await page.locator(route.root).waitFor();
        await setDesktopSidebar(page, true);
        await waitForStableLayout(page, route.root);
        await check(`${viewport.name} ${route.name} management`, () => assertManagementLayout(page, route.name, `${viewport.name} ${route.name}`));
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
