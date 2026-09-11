const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { createAppFixture } = require('./browser-fixture.cjs');

const publishDirectory = path.resolve(process.argv[2] ?? path.join(__dirname, '../../../artifacts/combat-p4-publish'));
const screenshotDirectory = path.resolve(process.argv[3] ?? path.join(__dirname, '../../../artifacts/combat-revision-p4-screenshots'));
const combatXml = `
<daten>
  <angaben><name>Darian Falkenstein</name><wundschwelle>4</wundschwelle></angaben>
  <eigenschaften><lebensenergie><akt>22</akt></lebensenergie><ausdauer><akt>28</akt></ausdauer><mut><akt>14</akt></mut><klugheit><akt>14</akt></klugheit><intuition><akt>15</akt></intuition><charisma><akt>14</akt></charisma><fingerfertigkeit><akt>14</akt></fingerfertigkeit><gewandtheit><akt>14</akt></gewandtheit><konstitution><akt>8</akt></konstitution><koerperkraft><akt>8</akt></koerperkraft></eigenschaften>
  <kampfsets><kampfset nr="1" tzm="true" inbenutzung="true" defaultrsmodel="true"><ausweichen>13</ausweichen><ini>11</ini><ruestungzonen><kopf>0</kopf><brust>0</brust><ruecken>0</ruecken><bauch>0</bauch><linkerarm>0</linkerarm><rechterarm>0</rechterarm><linkesbein>0</linkesbein><rechtesbein>0</rechtesbein></ruestungzonen><nahkampfwaffen><nahkampfwaffe><nummer>1</nummer><name>Magierstab als Stab</name><at>19</at><pa>14</pa><tp>1W+4</tp></nahkampfwaffe></nahkampfwaffen></kampfset></kampfsets>
</daten>`;

function statusText(page) {
  return page.locator('.combat-status-panel').innerText();
}

async function assertFocus(page, locator, label) {
  for (let attempt = 0; attempt < 20; attempt += 1) {
    if (await locator.evaluate(element => element === document.activeElement)) {
      return;
    }
    await page.waitForTimeout(25);
  }

  assert.fail(`${label}: focus was not returned`);
}

async function setNumber(drawer, label, value) {
  await drawer.locator(`input[aria-label="${label}"]`).fill(String(value));
  await drawer.getByRole('button', { name: 'Anwenden', exact: true }).click();
}

(async () => {
  await fs.mkdir(screenshotDirectory, { recursive: true });
  const fixture = await createAppFixture(publishDirectory, { userCount: 1, heroSourceXml: combatXml });

  try {
    const { origin, pages, errors } = await fixture.prepare();
    const page = pages[0];
    await page.setViewportSize({ width: 390, height: 900 });
    await page.goto(`${origin}/kampf`, { waitUntil: 'domcontentloaded' });
    await page.locator('.combat-resource-strip').waitFor();

    let text = await statusText(page);
    assert.match(text, /—\s*\/\s*22/);
    assert.match(text, /Aktueller Zustand noch nicht erfasst/);
    assert.equal(await page.getByRole('button', { name: 'Mit Maximalwerten beginnen', exact: true }).count(), 1);

    const initialResourceTrigger = page.getByRole('button', { name: 'Aktuelle Werte erfassen', exact: true });
    await initialResourceTrigger.click();
    const resourceDrawer = page.locator('.combat-details-drawer');
    await resourceDrawer.waitFor();
    await resourceDrawer.locator('input[aria-label="LeP"]').fill('17');
    await resourceDrawer.getByRole('button', { name: 'Abbrechen', exact: true }).click();
    await assertFocus(page, initialResourceTrigger, 'resource drawer cancel');
    assert.match(await page.locator('.hero-resource-lep').innerText(), /—\s*\/\s*22/);

    await initialResourceTrigger.click();
    await resourceDrawer.waitFor();
    await resourceDrawer.press('Escape');
    await resourceDrawer.waitFor({ state: 'detached' });
    await assertFocus(page, initialResourceTrigger, 'resource drawer escape');

    const resource = page.locator('.hero-resource-lep');
    await resource.click();
    await setNumber(page.locator('.combat-details-drawer'), 'LeP', 17);
    await assertFocus(page, resource, 'resource drawer apply');
    text = await statusText(page);
    assert.match(text, /17\s*\/\s*22/);
    assert.match(text, /WUNDEN\s+—/i);

    await page.getByRole('button', { name: 'Zonen', exact: true }).click();
    const zones = page.locator('.combat-zones-area.active');
    await zones.locator('.combat-zone-row').filter({ hasText: 'Brust' }).click();
    await setNumber(page.locator('.combat-details-drawer'), 'Wunden', 1);
    text = await statusText(page);
    assert.match(text, /17\s*\/\s*22/);
    assert.match(text, /WUNDEN\s+1\s+\+\s+\?/i);
    assert.match(await zones.locator('.combat-zone-row').filter({ hasText: 'Brust' }).innerText(), /Wunden 1\/3/);

    await zones.locator('.combat-facing-button').nth(1).click();
    assert.match(await zones.locator('.combat-zone-row').filter({ hasText: /Rück/ }).innerText(), /Wunden 1\/3/);
    await zones.locator('.combat-facing-button').first().click();

    await page.getByRole('button', { name: 'Rückgängig', exact: true }).click();
    text = await statusText(page);
    assert.match(text, /17\s*\/\s*22/);
    assert.match(text, /WUNDEN\s+—/i);
    assert.match(await zones.locator('.combat-zone-row').filter({ hasText: 'Brust' }).innerText(), /Wunden \?\/3/);

    await zones.locator('.combat-zone-row').filter({ hasText: 'Brust' }).click();
    await page.locator('.combat-details-drawer').getByRole('button', { name: 'Abbrechen', exact: true }).click();
    assert.match(await zones.locator('.combat-zone-row').filter({ hasText: 'Brust' }).innerText(), /Wunden \?\/3/);

    await zones.locator('.combat-zone-row').filter({ hasText: 'Brust' }).click();
    await setNumber(page.locator('.combat-details-drawer'), 'Wunden', 1);
    await page.reload({ waitUntil: 'domcontentloaded' });
    await page.locator('.combat-resource-strip').waitFor();
    await page.getByRole('button', { name: 'Zonen', exact: true }).click();
    await page.locator('.combat-zones-area.active .combat-zone-row').filter({ hasText: 'Brust' }).waitFor();
    text = await statusText(page);
    assert.match(text, /17\s*\/\s*22/);
    assert.match(text, /WUNDEN\s+1\s+\+\s+\?/i);
    assert.match(await page.locator('.combat-zones-area.active .combat-zone-row').filter({ hasText: 'Brust' }).innerText(), /Wunden 1\/3/);
    const storageKeys = await page.evaluate(() => Object.keys(localStorage).filter(key => key.startsWith('dsa.combat-state:')));
    assert.equal(storageKeys.length, 1);

    const geometry = await page.evaluate(() => ({
      clientWidth: document.body.clientWidth,
      scrollWidth: document.body.scrollWidth
    }));
    assert.ok(geometry.scrollWidth <= geometry.clientWidth + 1);

    for (const width of [390, 1440]) {
      await page.setViewportSize({ width, height: 900 });
      await page.screenshot({
        path: path.join(screenshotDirectory, `combat-p4-${width}.png`),
        fullPage: true
      });
    }

    assert.equal(errors.length, 0, `browser errors: ${errors.join(' | ')}`);
    console.log(JSON.stringify({ geometry, storageKeys, screenshots: screenshotDirectory }));
  } finally {
    await fixture.close();
  }
})().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
