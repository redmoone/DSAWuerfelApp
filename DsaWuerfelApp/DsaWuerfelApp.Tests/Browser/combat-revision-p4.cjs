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
    assert.match(text, /LEP\s+22\s+noch nicht gestartet/);
    assert.match(text, /Kampfzustand noch nicht gestartet/);
    assert.equal(await page.getByRole('button', { name: 'Kampfzustand starten', exact: true }).count(), 1);

    await page.getByRole('button', { name: 'Kampfzustand starten', exact: true }).click();
    await page.getByText('Kampfzustand auf diesem Gerät', { exact: true }).waitFor();
    text = await statusText(page);
    assert.match(text, /22 \/ 22/);
    assert.match(text, /WUNDEN GESAMT\s+0/i);

    const situationalModifier = page.locator('.combat-action-summary .modifier-pill');
    await situationalModifier.locator('.mod-btn').last().click();
    await situationalModifier.getByText('+1', { exact: true }).waitFor();

    await page.getByRole('button', { name: 'Treffer erfassen', exact: true }).click();
    const capture = page.locator('.combat-hit-capture');
    await capture.waitFor();
    await capture.locator('select').selectOption('Torso');
    await capture.locator('.text-pill-input[type="number"]').fill('5');
    await capture.locator('.modifier-pill .mod-btn').last().click();
    await capture.locator('.text-pill-input[placeholder="Optional"]').fill('Treffer am Torso');
    assert.match(await capture.locator('.combat-hit-preview').innerText(), /17/);
    assert.match(await capture.locator('.combat-hit-preview').innerText(), /0.*→ 1/);
    await capture.getByRole('button', { name: 'Übernehmen', exact: true }).click();

    text = await statusText(page);
    assert.match(text, /17 \/ 22/);
    assert.match(text, /WUNDEN GESAMT\s+1/i);
    const torsoRows = page.locator('.combat-zone-row').filter({ hasText: 'Brust' });
    assert.equal(await torsoRows.count(), 1);
    assert.match(await torsoRows.innerText(), /Wunden 1\/3/);
    assert.match(await page.locator('.combat-notice').innerText(), /Treffer wurde manuell erfasst/);

    await page.getByRole('button', { name: 'Rückseite', exact: true }).click();
    assert.match(await page.locator('.combat-zone-row').filter({ hasText: 'Rücken' }).innerText(), /Wunden 1\/3/);
    await page.getByRole('button', { name: 'Vorderseite', exact: true }).click();

    await page.getByRole('button', { name: 'Letzte Änderung rückgängig', exact: true }).click();
    text = await statusText(page);
    assert.match(text, /22 \/ 22/);
    assert.match(text, /WUNDEN GESAMT\s+0/i);
    assert.match(await page.locator('.combat-zone-row').filter({ hasText: 'Brust' }).innerText(), /Wunden 0\/3/);

    await page.getByRole('button', { name: 'Treffer erfassen', exact: true }).click();
    await page.locator('.combat-hit-capture').getByRole('button', { name: 'Abbrechen', exact: true }).click();
    await page.getByRole('button', { name: 'Treffer erfassen', exact: true }).click();
    await page.locator('.combat-hit-capture').locator('.text-pill-input[type="number"]').fill('3');
    await page.locator('.combat-hit-capture').getByRole('button', { name: 'Übernehmen', exact: true }).click();
    await page.reload({ waitUntil: 'domcontentloaded' });
    await page.locator('.combat-resource-strip').waitFor();
    await page.getByText('Kampfzustand auf diesem Gerät', { exact: true }).waitFor();
    text = await statusText(page);
    assert.match(text, /19 \/ 22/);
    assert.match(text, /WUNDEN GESAMT\s+0/i);
    assert.match(await page.locator('.combat-action-summary .modifier-pill').innerText(), /\+1/);
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
