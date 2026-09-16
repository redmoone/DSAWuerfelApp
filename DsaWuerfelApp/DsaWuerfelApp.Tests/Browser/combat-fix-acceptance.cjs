const assert = require('node:assert/strict');
const path = require('node:path');
const { createAppFixture } = require('./browser-fixture.cjs');

const publishDirectory = path.resolve(process.argv[2] ?? path.join(__dirname, '../../../artifacts/combat-fix-publish'));
const combatXml = `
<daten>
  <angaben><name>Darian Falkenstein</name><wundschwelle>4</wundschwelle></angaben>
  <eigenschaften><mut><akt>14</akt></mut><klugheit><akt>13</akt></klugheit><intuition><akt>15</akt></intuition><charisma><akt>12</akt></charisma><fingerfertigkeit><akt>14</akt></fingerfertigkeit><gewandtheit><akt>14</akt></gewandtheit><konstitution><akt>8</akt></konstitution><koerperkraft><akt>8</akt></koerperkraft><lebensenergie><akt>22</akt></lebensenergie><ausdauer><akt>28</akt></ausdauer></eigenschaften>
  <kampfsets><kampfset nr="1" tzm="true" inbenutzung="true"><ausweichen>13</ausweichen><ini>11</ini><ruestungzonen><kopf>0</kopf><brust>0</brust><ruecken>0</ruecken><bauch>0</bauch><linkerarm>0</linkerarm><rechterarm>0</rechterarm><linkesbein>0</linkesbein><rechtesbein>0</rechtesbein></ruestungzonen><nahkampfwaffen><nahkampfwaffe><nummer>1</nummer><name>Magierstab</name><at>19</at><pa>14</pa><tp>1W+4</tp></nahkampfwaffe></nahkampfwaffen></kampfset></kampfsets>
</daten>`;

async function waitForResult(page) {
  await page.locator('.history-entry').last().waitFor();
  await page.locator('.combat-dice-viewport canvas').waitFor();
  await page.waitForFunction(() => {
    const result = document.querySelector('.history-entry:last-child');
    return Boolean(result?.textContent?.trim());
  });
  return page.locator('.history-entry').last().innerText();
}

(async () => {
  const fixture = await createAppFixture(publishDirectory, { userCount: 1, heroSourceXml: combatXml });

  try {
    const { origin, pages, errors } = await fixture.prepare();
    const page = pages[0];

    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(`${origin}/kampf`, { waitUntil: 'domcontentloaded' });
    await page.locator('.combat-action-panel').waitFor();
    await page.locator('.combat-initiative-overview').waitFor();
    assert.equal(await page.locator('.combat-dice-viewport').count(), 0,
      'empty combat viewport is rendered before the first result');

    const rollButton = page.getByRole('button', { name: 'Attacke würfeln', exact: true });
    await rollButton.click();
    const firstResult = await waitForResult(page);
    assert.match(firstResult, /Attacke|gelungen|misslungen/i,
      `result disappeared after the dice canvas mounted: ${await page.evaluate(() => ({
        location: location.href,
        resultCount: document.querySelectorAll('.history-entry').length,
        actionText: document.querySelector('.combat-action-panel')?.textContent?.trim() ?? ''
      }))}`);
    assert.match(await page.locator('.combat-action-panel').innerText(), /Attacke auf 19/);
    assert.equal(await page.locator('.roll-history').count(), 1);

    await page.getByRole('tab', { name: 'Rüstung & Wunden', exact: true }).click();
    await page.locator('.combat-zone-row').first().waitFor();
    await page.getByRole('tab', { name: 'Kampf', exact: true }).click();
    await page.waitForTimeout(150);
    await page.locator('.combat-action-panel').waitFor();
    await page.waitForFunction(() => Boolean(document.querySelector('.combat-dice-viewport canvas')));
    assert.equal(await page.locator('.roll-history').count(), 1,
      'tab return created an additional history entry');

    await page.getByRole('button', { name: 'Attacke würfeln', exact: true }).click();
    await page.waitForFunction(() => document.querySelectorAll('.history-entry').length === 2);
    await waitForResult(page);
    assert.equal(await page.locator('.combat-dice-viewport canvas').count(), 1,
      'second result lost its dice viewport');
    assert.equal(await page.locator('.roll-history').count(), 1,
      'the history component was duplicated after the second roll');
    assert.equal(await page.locator('.history-entry').count(), 2,
      'second roll was not added to the history');

    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto(`${origin}/kampf`, { waitUntil: 'domcontentloaded' });
    await page.locator('.hero-status-resources').waitFor();
    await page.locator('.combat-initiative-overview').waitFor();
    const mobileGeometry = await page.evaluate(() => {
      const loadout = document.querySelector('.combat-loadout-details')?.getBoundingClientRect();
      const resources = document.querySelector('.hero-status-resources')?.getBoundingClientRect();
      return {
        scrollWidth: document.documentElement.scrollWidth,
        clientWidth: document.documentElement.clientWidth,
        loadoutHeight: loadout?.height ?? 0,
        resourcesHeight: resources?.height ?? 0,
        initiativeVisible: getComputedStyle(document.querySelector('.combat-initiative-overview')).display !== 'none'
      };
    });
    assert.ok(mobileGeometry.scrollWidth <= mobileGeometry.clientWidth + 1,
      `mobile horizontal overflow: ${JSON.stringify(mobileGeometry)}`);
    assert.ok(mobileGeometry.loadoutHeight < 220,
      `mobile set picker retained a desktop-sized flex basis: ${JSON.stringify(mobileGeometry)}`);
    assert.ok(mobileGeometry.resourcesHeight > 0,
      `mobile status blocks overlap: ${JSON.stringify(mobileGeometry)}`);
    assert.equal(mobileGeometry.initiativeVisible, true);
    assert.equal(await page.locator('.combat-details-drawer').count(), 0,
      'combat status unexpectedly uses the old resource drawer');

    await page.setViewportSize({ width: 320, height: 844 });
    await page.goto(`${origin}/kampf`, { waitUntil: 'domcontentloaded' });
    await page.locator('.hero-status-resources').waitFor();
    const narrowGeometry = await page.evaluate(() => ({
      scrollWidth: document.documentElement.scrollWidth,
      clientWidth: document.documentElement.clientWidth,
      controls: [...document.querySelectorAll('button, input, select')]
        .map(element => {
          const rect = element.getBoundingClientRect();
          return { left: rect.left, right: rect.right, width: rect.width };
        })
        .filter(rect => rect.width > 0 && (rect.left < -1 || rect.right > window.innerWidth + 1))
    }));
    assert.ok(narrowGeometry.scrollWidth <= narrowGeometry.clientWidth + 1,
      `320px horizontal overflow: ${JSON.stringify(narrowGeometry)}`);
    assert.deepEqual(narrowGeometry.controls, [],
      `320px controls are clipped: ${JSON.stringify(narrowGeometry)}`);
    assert.equal(errors.length, 0, `browser errors: ${errors.join(' | ')}`);
    console.log(JSON.stringify({ firstResult, mobileGeometry, narrowGeometry }));
  } finally {
    await fixture.close();
  }
})().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
