const assert = require('node:assert/strict');
const path = require('node:path');
const { createAppFixture } = require('./browser-fixture.cjs');

const publishDirectory = path.resolve(process.argv[2] ?? path.join(__dirname, '../../../artifacts/combat-runtime-publish'));
const combatXml = `
<daten>
  <angaben><name>Darian Falkenstein</name><wundschwelle>4</wundschwelle></angaben>
  <eigenschaften><lebensenergie><akt>22</akt></lebensenergie><ausdauer><akt>28</akt></ausdauer></eigenschaften>
  <kampfsets><kampfset nr="1" tzm="true" inbenutzung="true"><ausweichen>13</ausweichen><ini>11</ini><ruestungzonen><kopf>0</kopf><brust>0</brust><ruecken>0</ruecken><bauch>0</bauch><linkerarm>0</linkerarm><rechterarm>0</rechterarm><linkesbein>0</linkesbein><rechtesbein>0</rechtesbein></ruestungzonen><nahkampfwaffen><nahkampfwaffe><nummer>1</nummer><name>Magierstab</name><at>19</at><pa>14</pa><tp>1W+4</tp></nahkampfwaffe></nahkampfwaffen></kampfset></kampfsets>
</daten>`;

async function setNumber(drawer, label, value) {
  await drawer.locator(`input[aria-label="${label}"]`).fill(String(value));
  await drawer.getByRole('button', { name: 'Anwenden', exact: true }).click();
}

(async () => {
  const fixture = await createAppFixture(publishDirectory, { userCount: 1, heroSourceXml: combatXml });
  try {
    const { origin, pages, errors } = await fixture.prepare();
    const page = pages[0];
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(`${origin}/kampf`, { waitUntil: 'domcontentloaded' });
    await page.locator('.combat-action-panel').waitFor();

    await page.locator('.hero-resource-lep').click();
    await setNumber(page.locator('.combat-details-drawer'), 'LeP', 10);
    await page.locator('.hero-resource').filter({ hasText: /AuP/ }).click();
    await setNumber(page.locator('.combat-details-drawer'), 'AuP', 8);

    await page.locator('.combat-zone-row').filter({ hasText: 'Brust' }).click();
    await setNumber(page.locator('.combat-details-drawer'), 'Wunden', 1);

    const target = page.locator('.combat-target-preview');
    await page.waitForFunction(() => document.querySelector('.combat-target-preview strong')?.textContent.trim() === '16');
    assert.match(await target.innerText(), /automatisch/);
    assert.match(await target.innerText(), /Brustwunden -1/);
    assert.match(await target.innerText(), /Niedrige LeP -1/);
    assert.match(await target.innerText(), /Niedrige AuP -1/);

    await page.getByRole('button', { name: 'Kampfwurf ausführen', exact: true }).click();
    await page.locator('[data-testid="combat-roll-result"]').waitFor();
    await page.locator('[data-testid="combat-roll-result"]').getByRole('button', { name: 'Details', exact: true }).click();
    const details = page.locator('.current-roll-panel');
    await details.waitFor();
    const detailsText = await details.innerText();
    assert.match(detailsText, /Brustwunden -1 \[WdS S\. 107/);
    assert.match(detailsText, /Niedrige LeP -1 \[WdS S\. 83\]/);
    assert.match(detailsText, /Niedrige AuP -1 \[WdS S\. 83\]/);
    assert.equal(errors.length, 0, `browser errors: ${errors.join(' | ')}`);
    console.log(JSON.stringify({ runtimeModifiers: true, effectiveTarget: 16, details: true }));
  } finally {
    await fixture.close();
  }
})().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
