const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { createAppFixture } = require('./browser-fixture.cjs');

const publishDirectory = path.resolve(process.argv[2] ?? path.join(__dirname, '../../../artifacts/combat-p2-publish'));
const screenshotDirectory = path.resolve(process.argv[3] ?? path.join(__dirname, '../../../artifacts/combat-revision-p2-screenshots'));

const combatXml = `
<daten>
  <config><rsmodell>zone</rsmodell></config>
  <angaben><name>Ardor Collen</name><wundschwelle>9</wundschwelle></angaben>
  <eigenschaften>
    <mut><akt>13</akt></mut><klugheit><akt>11</akt></klugheit><intuition><akt>14</akt></intuition>
    <charisma><akt>10</akt></charisma><fingerfertigkeit><akt>11</akt></fingerfertigkeit><gewandtheit><akt>15</akt></gewandtheit>
    <konstitution><akt>14</akt></konstitution><koerperkraft><akt>15</akt></koerperkraft>
    <lebensenergie><akt>40</akt></lebensenergie><ausdauer><akt>35</akt></ausdauer>
  </eigenschaften>
  <vorteile><vorteil><name>Eisern</name><bezeichner>Eisern</bezeichner></vorteil></vorteile>
  <sonderfertigkeiten>
    <sonderfertigkeit><name>Aufmerksamkeit</name><bezeichner>Aufmerksamkeit</bezeichner><bereich>Kampf</bereich></sonderfertigkeit>
    <sonderfertigkeit><name>Ausweichen I</name><bezeichner>Ausweichen I</bezeichner><bereich>Kampf</bereich></sonderfertigkeit>
  </sonderfertigkeiten>
  <verbilligtesonderfertigkeiten><sonderfertigkeit><name>Binden</name><bezeichner>Binden</bezeichner></sonderfertigkeit></verbilligtesonderfertigkeiten>
  <kampfsets>
    <kampfset nr="1" tzm="true" inbenutzung="true" defaultrsmodel="true">
      <ausweichen>11</ausweichen><geschwindigkeitinklbe>7</geschwindigkeitinklbe><ini>11</ini>
      <ruestungzonen><kopf>5</kopf><brust>5</brust><ruecken>5</ruecken><bauch>5</bauch><linkerarm>5</linkerarm><rechterarm>5</rechterarm><linkesbein>3</linkesbein><rechtesbein>3</rechtesbein><gesamt>5</gesamt><gesamtschutz>5</gesamtschutz><gesamtzonenschutz>5</gesamtzonenschutz><behinderung>1</behinderung></ruestungzonen>
      <nahkampfwaffen>
        <nahkampfwaffe><nummer>1</nummer><möglich>true</möglich><name>(Lang-)Schwert</name><at>21</at><pa>16</pa><tp>1W+4</tp><tpinkl>1W+5</tpinkl><waffentalent>Schwerter</waffentalent></nahkampfwaffe>
        <nahkampfwaffe><nummer>2</nummer><möglich>true</möglich><name>(Lang-)Schwert</name><at>21</at><pa>16</pa><tp>1W+4</tp><tpinkl>1W+5</tpinkl><waffentalent>Schwerter</waffentalent></nahkampfwaffe>
      </nahkampfwaffen>
      <ruestungen><ruestung><nummer>1</nummer><name>Armschienen</name><grundlage>Armschienen</grundlage><rs>*</rs><be>*</be><kopf>0</kopf></ruestung></ruestungen>
    </kampfset>
    <kampfset nr="1" tzm="false" inbenutzung="true" defaultrsmodel="false">
      <ausweichen>8</ausweichen><geschwindigkeitinklbe>4</geschwindigkeitinklbe><ruestungeinfach><gesamt>6</gesamt><behinderung>4</behinderung></ruestungeinfach>
      <nahkampfwaffen><nahkampfwaffe><nummer>1</nummer><möglich>true</möglich><name>(Lang-)Schwert</name><at>20</at><pa>15</pa><tp>1W+4</tp><tpinkl>1W+5</tpinkl></nahkampfwaffe></nahkampfwaffen>
    </kampfset>
    <kampfset nr="2" tzm="true" inbenutzung="true" defaultrsmodel="true">
      <ausweichen>11</ausweichen><ruestungzonen><kopf>5</kopf><brust>5</brust><ruecken>5</ruecken><bauch>5</bauch><linkerarm>5</linkerarm><rechterarm>5</rechterarm><linkesbein>3</linkesbein><rechtesbein>3</rechtesbein></ruestungzonen>
      <nahkampfwaffen><nahkampfwaffe><nummer>1</nummer><name>(Lang-)Schwert</name><at>21</at><pa>16</pa></nahkampfwaffe></nahkampfwaffen>
      <schilder><schild><nummer>1</nummer><name>Buckler (Vollmetall)</name><at>0</at><pa>14</pa><typ>Paradewaffe</typ></schild></schilder>
    </kampfset>
  </kampfsets>
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
    await page.locator('.combat-zone-row').first().waitFor();

    const initialText = await page.locator('.combat-page').innerText();
    assert.match(initialText, /40/);
    assert.match(initialText, /35/);
    assert.match(initialText, /AT 21/);
    assert.match(initialText, /PA 16/);
    assert.match(initialText, /RS 5/);
    assert.match(initialText, /Schwert/);

    const setOptions = await page.locator('.combat-select option').evaluateAll(options =>
      options.map(option => ({ value: option.value, label: option.textContent.trim() })));
    assert.equal(await page.locator('.combat-select optgroup').count(), 2);
    assert.equal(setOptions.filter(option => option.label.includes('Zonen')).length, 2);
    assert.equal(setOptions.filter(option => option.label.includes('Einfache')).length, 1);

    await page.locator('.combat-maneuver-block input').fill('Aufmerksamkeit');
    await page.getByText('Aufmerksamkeit', { exact: true }).waitFor();

    const simpleSet = setOptions.find(option => option.label.includes('Einfache'));
    assert.ok(simpleSet);
    await page.locator('.combat-select').selectOption(simpleSet.value);
    await page.getByText('AT 20', { exact: true }).waitFor();
    const simpleText = await page.locator('.combat-page').innerText();
    assert.match(simpleText, /PA 15/);
    assert.match(simpleText, /RS \/ BE[\s\S]*6 \/ 4/);

    const bodyMetrics = await page.evaluate(() => ({
      scrollWidth: document.body.scrollWidth,
      clientWidth: document.body.clientWidth,
      zoneRows: document.querySelectorAll('.combat-zone-row').length,
      sourceXmlText: document.body.innerText.includes('<daten>')
    }));
    assert.ok(bodyMetrics.scrollWidth <= bodyMetrics.clientWidth + 1);
    assert.equal(bodyMetrics.zoneRows, 8);
    assert.equal(bodyMetrics.sourceXmlText, false);

    for (const width of [390, 1440]) {
      await page.setViewportSize({ width, height: 900 });
      await page.goto(`${origin}/kampf`, { waitUntil: 'domcontentloaded' });
      await page.locator('.combat-resource-strip').waitFor();
      await page.screenshot({
        path: path.join(screenshotDirectory, `combat-p2-${width}.png`),
        fullPage: true
      });
    }

    assert.equal(errors.length, 0, `browser errors: ${errors.join(' | ')}`);
    console.log(JSON.stringify({ setOptions, bodyMetrics, screenshots: screenshotDirectory }));
  } finally {
    await fixture.close();
  }
})().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
