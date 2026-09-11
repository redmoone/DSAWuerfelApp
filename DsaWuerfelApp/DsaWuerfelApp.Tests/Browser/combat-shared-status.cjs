const assert = require('node:assert/strict');
const path = require('node:path');
const { createAppFixture } = require('./browser-fixture.cjs');

const publishDirectory = path.resolve(process.argv[2] ?? path.join(__dirname, '../../../artifacts/combat-shared-status-publish'));
const combatXml = `
<daten>
  <angaben><name>Darian Falkenstein</name><wundschwelle>4</wundschwelle></angaben>
  <eigenschaften><lebensenergie><akt>22</akt></lebensenergie><ausdauer><akt>28</akt></ausdauer><astralenergie><akt>12</akt></astralenergie></eigenschaften>
  <kampfsets><kampfset nr="1" tzm="true" inbenutzung="true"><ausweichen>13</ausweichen><ini>11</ini><ruestungzonen><kopf>0</kopf><brust>0</brust><ruecken>0</ruecken><bauch>0</bauch><linkerarm>0</linkerarm><rechterarm>0</rechterarm><linkesbein>0</linkesbein><rechtesbein>0</rechtesbein></ruestungzonen></kampfset></kampfsets>
</daten>`;

(async () => {
  const fixture = await createAppFixture(publishDirectory, { userCount: 1, heroSourceXml: combatXml });
  try {
    const { origin, pages, errors } = await fixture.prepare();
    const page = pages[0];

    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(`${origin}/wuerfel`, { waitUntil: 'domcontentloaded' });
    await page.locator('.wuerfel-status-surface .combat-resource-strip').waitFor();

    const status = page.locator('.wuerfel-status-surface .combat-status-panel');
    assert.match(await status.innerText(), /Held0/);
    assert.match(await page.locator('.hero-resource-lep').innerText(), /22 \/ 22/);
    assert.equal(await page.locator('.hero-resource').filter({ hasText: /AeP/i }).count(), 1);

    await page.locator('.hero-resource-lep').click();
    const drawer = page.locator('.combat-details-drawer');
    await drawer.waitFor();
    await drawer.locator('input[aria-label="LeP"]').fill('-3');
    await drawer.getByRole('button', { name: 'Anwenden', exact: true }).click();
    await page.waitForFunction(() => document.querySelector('.hero-resource-lep')?.textContent.includes('-3 / 22'));

    await page.goto(`${origin}/kampf`, { waitUntil: 'domcontentloaded' });
    await page.locator('.combat-resource-strip').waitFor();
    assert.match(await page.locator('.hero-resource-lep').innerText(), /-3 \/ 22/);

    await page.goto(`${origin}/wuerfel`, { waitUntil: 'domcontentloaded' });
    await page.locator('.wuerfel-status-surface .combat-resource-strip').waitFor();
    assert.match(await page.locator('.hero-resource-lep').innerText(), /-3 \/ 22/);
    assert.equal(await page.locator('.wuerfel-page-container').evaluate(element => element.scrollWidth <= element.clientWidth), true);
    assert.equal(errors.length, 0, `browser errors: ${errors.join(' | ')}`);
    console.log(JSON.stringify({ sharedStatus: true, negativeLeP: true, navigation: true }));
  } finally {
    await fixture.close();
  }
})().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
