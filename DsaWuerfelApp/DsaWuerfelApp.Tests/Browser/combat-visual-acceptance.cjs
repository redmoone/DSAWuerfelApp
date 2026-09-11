const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { createAppFixture } = require('./browser-fixture.cjs');

const publishDirectory = path.resolve(process.argv[2] ?? path.join(__dirname, '../../../artifacts/combat-visual-publish'));
const screenshotDirectory = path.resolve(process.argv[3] ?? path.join(__dirname, '../../../artifacts/combat-visual-screenshots'));
const viewports = [
  { name: 'desktop-1440x900', width: 1440, height: 900 },
  { name: 'tablet-1024x768', width: 1024, height: 768 },
  { name: 'phone-390x844', width: 390, height: 844 }
];

const combatXml = `
<daten>
  <angaben><name>Darian Falkenstein</name><wundschwelle>4</wundschwelle></angaben>
  <eigenschaften><mut><akt>14</akt></mut><klugheit><akt>13</akt></klugheit><intuition><akt>15</akt></intuition><charisma><akt>12</akt></charisma><fingerfertigkeit><akt>14</akt></fingerfertigkeit><gewandtheit><akt>14</akt></gewandtheit><konstitution><akt>8</akt></konstitution><koerperkraft><akt>8</akt></koerperkraft><lebensenergie><akt>22</akt></lebensenergie><ausdauer><akt>28</akt></ausdauer></eigenschaften>
  <kampfsets><kampfset nr="1" tzm="true" inbenutzung="true"><ausweichen>13</ausweichen><ini>11</ini><ruestungzonen><kopf>0</kopf><brust>0</brust><ruecken>0</ruecken><bauch>0</bauch><linkerarm>0</linkerarm><rechterarm>0</rechterarm><linkesbein>0</linkesbein><rechtesbein>0</rechtesbein></ruestungzonen><nahkampfwaffen><nahkampfwaffe><nummer>1</nummer><name>Magierstab</name><at>19</at><pa>14</pa><tp>1W+4</tp></nahkampfwaffe></nahkampfwaffen></kampfset></kampfsets>
</daten>`;

async function assertNoHorizontalOverflow(page, label) {
  const geometry = await page.evaluate(() => ({
    clientWidth: document.documentElement.clientWidth,
    scrollWidth: document.documentElement.scrollWidth,
    bodyScrollWidth: document.body.scrollWidth
  }));
  assert.ok(geometry.scrollWidth <= geometry.clientWidth + 1, `${label}: document overflow ${JSON.stringify(geometry)}`);
  assert.ok(geometry.bodyScrollWidth <= geometry.clientWidth + 1, `${label}: body overflow ${JSON.stringify(geometry)}`);
}

async function assertNoClippedControls(page, label) {
  const clipped = await page.evaluate(() => [...document.querySelectorAll('button, input, select, textarea')]
    .map(element => {
      const rect = element.getBoundingClientRect();
      return {
        label: element.getAttribute('aria-label') ?? element.textContent?.trim().slice(0, 40),
        left: rect.left,
        right: rect.right,
        width: rect.width
      };
    })
    .filter(element => element.width > 0 && (element.left < -1 || element.right > window.innerWidth + 1)));
  assert.deepEqual(clipped, [], `${label}: clipped controls ${JSON.stringify(clipped)}`);
}

async function assertViewportRect(page, selector, viewport, label) {
  const rect = await page.locator(selector).first().evaluate(element => {
    const value = element.getBoundingClientRect();
    return { left: value.left, right: value.right, top: value.top, bottom: value.bottom, width: value.width, height: value.height };
  });
  assert.ok(rect.width > 0 && rect.height > 0, `${label}: control has no size`);
  assert.ok(rect.left >= -1 && rect.right <= viewport.width + 1, `${label}: horizontal clipping ${JSON.stringify(rect)}`);
  assert.ok(rect.top >= -1 && rect.bottom <= viewport.height + 1, `${label}: outside initial viewport ${JSON.stringify(rect)}`);
}

(async () => {
  await fs.mkdir(screenshotDirectory, { recursive: true });
  const fixture = await createAppFixture(publishDirectory, { userCount: 1, heroSourceXml: combatXml });
  try {
    const { origin, pages, errors } = await fixture.prepare();
    const page = pages[0];

    for (const viewport of viewports) {
      await page.setViewportSize({ width: viewport.width, height: viewport.height });
      await page.goto(`${origin}/kampf`, { waitUntil: 'domcontentloaded' });
      await page.locator('.combat-resource-strip').waitFor();
      await page.locator('.combat-action-panel').waitFor();
      await assertNoHorizontalOverflow(page, viewport.name);
      await assertNoClippedControls(page, viewport.name);

      if (viewport.width === 1440) {
        await assertViewportRect(page, '.combat-action-panel .right-actions .dsa-btn', viewport, 'desktop combat roll');
      }

      if (viewport.width === 390) {
        const resource = page.locator('.hero-resource-lep');
        await resource.click();
        const drawer = page.locator('.combat-details-drawer');
        await drawer.waitFor();
        const drawerRect = await drawer.boundingBox();
        assert.ok(drawerRect && drawerRect.width <= viewport.width + 1 && drawerRect.x >= -1,
          `phone drawer is clipped ${JSON.stringify(drawerRect)}`);
        await drawer.locator('.combat-details-close').click();
        await drawer.waitFor({ state: 'detached' });
      }

      await page.screenshot({
        path: path.join(screenshotDirectory, `combat-${viewport.name}.png`),
        fullPage: true
      });
    }

    assert.equal(errors.length, 0, `browser errors: ${errors.join(' | ')}`);
    console.log(JSON.stringify({ viewports: viewports.map(viewport => viewport.name), screenshots: screenshotDirectory }));
  } finally {
    await fixture.close();
  }
})().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
