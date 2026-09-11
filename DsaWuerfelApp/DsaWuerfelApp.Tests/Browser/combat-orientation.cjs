const assert = require('node:assert/strict');
const path = require('node:path');
const { createAppFixture } = require('./browser-fixture.cjs');

const publishDirectory = path.resolve(process.argv[2] ?? path.join(__dirname, '../../../artifacts/combat-orientation-publish'));
const combatXml = `
<daten>
  <angaben><name>Darian Falkenstein</name><wundschwelle>4</wundschwelle></angaben>
  <eigenschaften><intuition><akt>15</akt></intuition><lebensenergie><akt>22</akt></lebensenergie><ausdauer><akt>28</akt></ausdauer></eigenschaften>
  <kampfsets><kampfset nr="1" tzm="true" inbenutzung="true"><ausweichen>13</ausweichen><ini>11</ini><ruestungzonen><kopf>0</kopf><brust>0</brust><ruecken>0</ruecken><bauch>0</bauch><linkerarm>0</linkerarm><rechterarm>0</rechterarm><linkesbein>0</linkesbein><rechtesbein>0</rechtesbein></ruestungzonen></kampfset></kampfsets>
  <sonderfertigkeiten><sonderfertigkeit><name>Aufmerksamkeit</name><bezeichner>Aufmerksamkeit</bezeichner><bereich>Kampf</bereich></sonderfertigkeit></sonderfertigkeiten>
</daten>`;

(async () => {
  const fixture = await createAppFixture(publishDirectory, { userCount: 1, heroSourceXml: combatXml });
  try {
    const { origin, pages, errors } = await fixture.prepare();
    const page = pages[0];

    await page.getByRole('button', { name: 'Erstellen', exact: true }).click();
    await page.getByPlaceholder('z.B. Borbarads Erben').fill('Orientierungsrunde');
    await page.getByRole('button', { name: 'Session Erstellen', exact: true }).click();
    await page.locator('.wuerfel-page-container').waitFor();
    await page.goto(`${origin}/kampf`, { waitUntil: 'domcontentloaded' });
    await page.locator('.combat-resource-strip').waitFor();
    await page.getByRole('button', { name: 'Eigene INI', exact: true }).waitFor();

    const orientationButton = page.getByRole('button', { name: 'Orientieren', exact: true });
    assert.equal(await orientationButton.isDisabled(), true);

    await page.getByRole('button', { name: 'Eigene INI', exact: true }).click();
    await page.getByRole('button', { name: '1W6 würfeln', exact: true }).click();
    await page.locator('.combat-initiative-row.current .combat-initiative-value').filter({ hasText: /INI/ }).waitFor();
    assert.equal(await orientationButton.isDisabled(), false);

    await orientationButton.click();
    const drawer = page.locator('.combat-details-drawer');
    await drawer.getByText(/Aufmerksamkeit: vorhanden/, { exact: false }).waitFor();
    await drawer.getByRole('button', { name: 'Handlung anlegen', exact: true }).click();

    const orientationEntry = page.locator('.combat-action-entry').filter({ hasText: 'Orientieren' }).first();
    await orientationEntry.getByRole('button', { name: 'Orientieren abschließen', exact: true }).click();
    await page.locator('.combat-notice').filter({ hasText: 'Orientieren abgeschlossen' }).waitFor();
    assert.equal(await page.locator('.combat-action-entry.completed').filter({ hasText: 'Orientieren' }).count(), 1);
    assert.match(await page.locator('.combat-initiative-row.current .combat-initiative-value').innerText(), /INI 17/);
    assert.equal(errors.length, 0, `browser errors: ${errors.join(' | ')}`);
    console.log(JSON.stringify({ orientation: true, attention: true, initiativeAfterOrientation: 17 }));
  } finally {
    await fixture.close();
  }
})().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
