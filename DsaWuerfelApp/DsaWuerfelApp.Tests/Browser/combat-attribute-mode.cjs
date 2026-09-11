const assert = require('node:assert/strict');
const path = require('node:path');
const { createAppFixture } = require('./browser-fixture.cjs');

const publishDirectory = path.resolve(process.argv[2] ?? path.join(__dirname, '../../../artifacts/combat-attribute-publish'));
const combatXml = `
<daten>
  <angaben><name>Darian Falkenstein</name><wundschwelle>4</wundschwelle></angaben>
  <eigenschaften><mut><akt>14</akt></mut><klugheit><akt>13</akt></klugheit><intuition><akt>15</akt></intuition><charisma><akt>12</akt></charisma><fingerfertigkeit><akt>14</akt></fingerfertigkeit><gewandtheit><akt>14</akt></gewandtheit><konstitution><akt>8</akt></konstitution><koerperkraft><akt>8</akt></koerperkraft><lebensenergie><akt>22</akt></lebensenergie><ausdauer><akt>28</akt></ausdauer></eigenschaften>
  <kampfsets><kampfset nr="1" tzm="true" inbenutzung="true"><ausweichen>13</ausweichen><ini>11</ini><ruestungzonen><kopf>0</kopf><brust>0</brust><ruecken>0</ruecken><bauch>0</bauch><linkerarm>0</linkerarm><rechterarm>0</rechterarm><linkesbein>0</linkesbein><rechtesbein>0</rechtesbein></ruestungzonen><nahkampfwaffen><nahkampfwaffe><nummer>1</nummer><name>Magierstab</name><at>19</at><pa>14</pa><tp>1W+4</tp></nahkampfwaffe></nahkampfwaffen></kampfset></kampfsets>
</daten>`;

(async () => {
  const fixture = await createAppFixture(publishDirectory, { userCount: 1, heroSourceXml: combatXml });
  try {
    const { origin, pages, errors } = await fixture.prepare();
    const page = pages[0];
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(`${origin}/kampf`, { waitUntil: 'domcontentloaded' });
    await page.locator('.combat-resource-strip').waitFor();

    await page.getByRole('tab', { name: 'Eigenschaften', exact: true }).click();
    assert.equal(await page.locator('.combat-set-row').count(), 0);
    assert.equal(await page.locator('.combat-maneuver-block').count(), 0);
    assert.match(await page.locator('.combat-attribute-block').innerText(), /Eigenschaftsprobe/i);

    await page.getByRole('button', { name: 'MU auswählen', exact: true }).click();
    assert.equal(await page.locator('.combat-selection-chip').count(), 1);
    assert.match(await page.locator('.combat-selection-chips').innerText(), /MU/);

    await page.getByRole('button', { name: 'Eigenschaftsprobe ausführen', exact: true }).click();
    await page.locator('[data-testid="combat-roll-result"]').waitFor();
    assert.match(await page.locator('[data-testid="combat-roll-result"]').innerText(), /Eigenschaftsprobe/i);
    assert.equal(await page.locator('.combat-dice-viewport canvas').count(), 1);

    await page.getByRole('button', { name: 'MU aus Auswahl entfernen', exact: true }).click();
    assert.equal(await page.locator('.combat-selection-chip').count(), 0);
    assert.match(await page.locator('.combat-selection-chips').innerText(), /Eigenschaften auswählen/);

    await page.getByRole('tab', { name: 'Kampf', exact: true }).click();
    await page.locator('.combat-set-row').waitFor();
    assert.equal(await page.locator('.combat-attribute-block').count(), 0);
    assert.equal(errors.length, 0, `browser errors: ${errors.join(' | ')}`);
    console.log(JSON.stringify({ attributeMode: true, result: true }));
  } finally {
    await fixture.close();
  }
})().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
