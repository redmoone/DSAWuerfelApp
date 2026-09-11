const assert = require('node:assert/strict');
const path = require('node:path');
const { createAppFixture } = require('./browser-fixture.cjs');

const publishDirectory = path.resolve(process.argv[2] ?? path.join(__dirname, '../../../artifacts/combat-orientation-probe-publish'));
const combatXml = `
<daten>
  <angaben><name>Ardor Collen</name><wundschwelle>9</wundschwelle></angaben>
  <eigenschaften><intuition><akt>15</akt></intuition><lebensenergie><akt>40</akt></lebensenergie><ausdauer><akt>35</akt></ausdauer></eigenschaften>
  <kampfsets><kampfset nr="1" tzm="true" inbenutzung="true"><ausweichen>11</ausweichen><ini>14</ini><ruestungzonen><kopf>0</kopf><brust>0</brust><ruecken>0</ruecken><bauch>0</bauch><linkerarm>0</linkerarm><rechterarm>0</rechterarm><linkesbein>0</linkesbein><rechtesbein>0</rechtesbein></ruestungzonen></kampfset></kampfsets>
</daten>`;

(async () => {
  const fixture = await createAppFixture(publishDirectory, { userCount: 1, heroSourceXml: combatXml });
  try {
    const { origin, pages, errors } = await fixture.prepare();
    const page = pages[0];

    await page.getByRole('button', { name: 'Erstellen', exact: true }).click();
    await page.getByPlaceholder('z.B. Borbarads Erben').fill('Orientierungsprobe');
    await page.getByRole('button', { name: 'Session Erstellen', exact: true }).click();
    await page.locator('.wuerfel-page-container').waitFor();
    await page.goto(`${origin}/kampf`, { waitUntil: 'domcontentloaded' });
    await page.locator('.combat-resource-strip').waitFor();

    const orientationButton = page.getByRole('button', { name: 'Orientieren', exact: true });
    assert.equal(await orientationButton.isDisabled(), true);

    await page.getByRole('button', { name: 'Eigene INI', exact: true }).click();
    await page.getByRole('button', { name: /1W6/ }).click();
    await page.locator('.combat-initiative-row.current .combat-initiative-value').waitFor();
    assert.equal(await orientationButton.isDisabled(), false);

    await orientationButton.click();
    const drawer = page.locator('.combat-details-drawer');
    await drawer.getByText(/Aufmerksamkeit: nicht vorhanden/, { exact: false }).waitFor();
    assert.equal(await drawer.locator('input[aria-label="Kriegskunst-Erleichterung"]').count(), 1);
    await drawer.getByRole('button', { name: 'Handlung anlegen', exact: true }).click();

    const orientationEntry = page.locator('.combat-action-entry').filter({ hasText: 'Orientieren' }).first();
    await orientationEntry.getByRole('button', { name: /IN-Probe/ }).click();
    await page.locator('.combat-action-entry.completed').filter({ hasText: 'Orientieren' }).waitFor();
    await page.locator('.combat-notice').filter({ hasText: /IN-Probe/ }).waitFor();
    assert.equal(await page.locator('.combat-action-entry.completed').filter({ hasText: 'Orientieren' }).count(), 1);
    assert.equal(errors.length, 0, `browser errors: ${errors.join(' | ')}`);
    console.log(JSON.stringify({ orientation: true, attention: false, probe: true }));
  } finally {
    await fixture.close();
  }
})().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
