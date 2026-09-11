const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { createAppFixture } = require('./browser-fixture.cjs');

const publishDirectory = path.resolve(process.argv[2] ?? path.join(__dirname, '../../../artifacts/combat-p3-publish'));
const screenshotDirectory = path.resolve(process.argv[3] ?? path.join(__dirname, '../../../artifacts/combat-revision-p3-screenshots'));
const combatXml = `
<daten>
  <angaben><name>Darian Falkenstein</name><wundschwelle>4</wundschwelle></angaben>
  <eigenschaften><lebensenergie><akt>22</akt></lebensenergie><ausdauer><akt>28</akt></ausdauer><mut><akt>14</akt></mut><klugheit><akt>14</akt></klugheit><intuition><akt>15</akt></intuition><charisma><akt>14</akt></charisma><fingerfertigkeit><akt>14</akt></fingerfertigkeit><gewandtheit><akt>14</akt></gewandtheit><konstitution><akt>8</akt></konstitution><koerperkraft><akt>8</akt></koerperkraft></eigenschaften>
  <sonderfertigkeiten><sonderfertigkeit><name>Aufmerksamkeit</name><bezeichner>Aufmerksamkeit</bezeichner><bereich>Nahkampf</bereich><bereich>Kampf</bereich></sonderfertigkeit></sonderfertigkeiten>
  <verbilligtesonderfertigkeiten><sonderfertigkeit><name>Binden</name><bezeichner>Binden</bezeichner><bereich>Nahkampf</bereich></sonderfertigkeit></verbilligtesonderfertigkeiten>
  <kampfsets><kampfset nr="1" tzm="true" inbenutzung="true" defaultrsmodel="true"><ausweichen>13</ausweichen><ini>11</ini><ruestungzonen><kopf>0</kopf><brust>0</brust><ruecken>0</ruecken><bauch>0</bauch><linkerarm>0</linkerarm><rechterarm>0</rechterarm><linkesbein>0</linkesbein><rechtesbein>0</rechtesbein></ruestungzonen><nahkampfwaffen><nahkampfwaffe><nummer>1</nummer><name>Magierstab als Stab</name><at>19</at><pa>14</pa><tp>1W+4</tp><tpinkl>1W+3</tpinkl></nahkampfwaffe><nahkampfwaffe><nummer>2</nummer><name>Magierstab als Stab</name><at>19</at><pa>14</pa><tp>1W+4</tp><tpinkl>1W+3</tpinkl></nahkampfwaffe></nahkampfwaffen></kampfset></kampfsets>
</daten>`;

(async () => {
  await fs.mkdir(screenshotDirectory, { recursive: true });
  const fixture = await createAppFixture(publishDirectory, { userCount: 1, heroSourceXml: combatXml });

  try {
    const { origin, pages, errors } = await fixture.prepare();
    const page = pages[0];
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(`${origin}/kampf`, { waitUntil: 'domcontentloaded' });
    await page.locator('.combat-resource-strip').waitFor();

    await page.locator('.combat-option').filter({ hasText: 'Magierstab als Stab Nr. 2' }).click();
    await page.getByText('Waffe / Abwehr', { exact: true }).waitFor();
    let infoText = await page.locator('.combat-info-surface').innerText();
    assert.match(infoText, /Nr\. 2/);
    assert.match(infoText, /AT 19/);
    assert.match(infoText, /TP 1W\+4/);
    assert.match(infoText, /inkl\. 1W\+3/);

    const maneuverSearch = page.locator('.combat-maneuver-block .search-input');
    await maneuverSearch.click();
    await maneuverSearch.fill('Aufmerksamkeit');
    const learnedEntry = page.locator('.combat-maneuver-block .dropdown-item-main').getByText('Aufmerksamkeit', { exact: true });
    await learnedEntry.click();
    infoText = await page.locator('.combat-info-surface').innerText();
    assert.match(infoText, /Sonderfertigkeit[\s\S]*Aufmerksamkeit/);
    assert.match(infoText, /gelernt/);
    assert.match(infoText, /Nahkampf · Kampf/);

    await maneuverSearch.click();
    await maneuverSearch.fill('Binden');
    const discountedEntry = page.locator('.combat-maneuver-block .dropdown-item.inactive');
    await discountedEntry.getByText('Binden (vergünstigt)', { exact: true }).waitFor();
    assert.equal(await discountedEntry.locator('button').count(), 0);

    const rollButton = page.getByRole('button', { name: 'Kampfwurf', exact: true });
    assert.equal(await rollButton.isDisabled(), true);
    assert.equal(await page.locator('.roll-history').count(), 1);
    assert.equal(await page.locator('.combat-zone-row').count(), 8);

    const geometry = await page.evaluate(() => ({
      clientWidth: document.body.clientWidth,
      scrollWidth: document.body.scrollWidth
    }));
    assert.ok(geometry.scrollWidth <= geometry.clientWidth + 1);

    for (const width of [390, 1440]) {
      await page.setViewportSize({ width, height: 900 });
      await page.screenshot({
        path: path.join(screenshotDirectory, `combat-p3-${width}.png`),
        fullPage: true
      });
    }

    assert.equal(errors.length, 0, `browser errors: ${errors.join(' | ')}`);
    console.log(JSON.stringify({ geometry, screenshots: screenshotDirectory }));
  } finally {
    await fixture.close();
  }
})().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
