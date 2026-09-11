const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { createAppFixture } = require('./browser-fixture.cjs');

const publishDirectory = path.resolve(process.argv[2] ?? path.join(__dirname, '../../../artifacts/combat-p1-publish'));
const screenshotDirectory = path.resolve(process.argv[3] ?? path.join(__dirname, '../../../artifacts/combat-revision-p1-screenshots'));
const viewports = [320, 390, 640, 900, 901, 1280, 1600];

(async () => {
  await fs.mkdir(screenshotDirectory, { recursive: true });
  const fixture = await createAppFixture(publishDirectory, { userCount: 1 });

  try {
    const { origin, pages, errors } = await fixture.prepare();
    const page = pages[0];

    for (const width of viewports) {
      await page.setViewportSize({ width, height: 900 });
      await page.goto(`${origin}/kampf`, { waitUntil: 'domcontentloaded' });
      await page.locator('.combat-page').waitFor();
      await page.locator('.combat-zone-row').first().waitFor();

      const metrics = await page.evaluate(() => ({
        bodyWidth: document.body.clientWidth,
        bodyScrollWidth: document.body.scrollWidth,
        zoneRows: document.querySelectorAll('.combat-zone-row').length,
        facingButtons: document.querySelectorAll('.combat-facing-button').length,
        canvases: document.querySelectorAll('.combat-dice-viewport canvas').length,
        hasActionBar: document.querySelectorAll('.results-bar').length === 1,
        hasHistory: document.querySelectorAll('.roll-history').length === 1,
        hasSideColumn: document.querySelectorAll('.combat-side').length === 1
      }));

      assert.equal(metrics.zoneRows, 8, `expected eight armor zones at ${width}px`);
      assert.equal(metrics.facingButtons, 2, `expected front/back controls at ${width}px`);
      assert.equal(metrics.canvases, 1, `expected one shared dice viewport at ${width}px`);
      assert.equal(metrics.hasActionBar, true, `expected shared action bar at ${width}px`);
      assert.equal(metrics.hasHistory, true, `expected shared roll history at ${width}px`);
      assert.equal(metrics.hasSideColumn, true, `expected information side column at ${width}px`);
      assert.ok(metrics.bodyScrollWidth <= metrics.bodyWidth + 1,
        `horizontal overflow at ${width}px: ${metrics.bodyScrollWidth} > ${metrics.bodyWidth}`);

      if (width === 390 || width === 1440) {
        await page.screenshot({
          path: path.join(screenshotDirectory, `combat-p1-${width}.png`),
          fullPage: true
        });
      }

      console.log(JSON.stringify({ width, ...metrics }));
    }

    for (const reference of [
      { name: 'combat', path: '/kampf', root: '.combat-page' },
      { name: 'dice', path: '/wuerfel', root: '.wuerfel-page-container' },
      { name: 'heroes', path: '/helden-verwaltung', root: '.helden-verwaltung-page' }
    ]) {
      for (const width of [390, 1440]) {
        await page.setViewportSize({ width, height: 900 });
        await page.goto(`${origin}${reference.path}`, { waitUntil: 'domcontentloaded' });
        await page.locator(reference.root).waitFor();
        await page.screenshot({
          path: path.join(screenshotDirectory, `${reference.name}-reference-${width}.png`),
          fullPage: true
        });
      }
    }

    assert.equal(errors.length, 0, `browser errors: ${errors.join(' | ')}`);
  } finally {
    await fixture.close();
  }
})().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
