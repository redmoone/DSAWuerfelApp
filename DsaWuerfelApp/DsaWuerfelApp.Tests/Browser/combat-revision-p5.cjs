const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { createAppFixture } = require('./browser-fixture.cjs');

const publishDirectory = path.resolve(process.argv[2] ?? path.join(__dirname, '../../../artifacts/combat-p5-publish'));
const screenshotDirectory = path.resolve(process.argv[3] ?? path.join(__dirname, '../../../artifacts/combat-revision-p5-screenshots'));
const viewports = [320, 390, 640, 900, 901, 1280, 1600];

const profiles = [
  {
    name: 'Darian',
    xml: `<daten><angaben><name>Darian Falkenstein</name><wundschwelle>4</wundschwelle></angaben><eigenschaften><lebensenergie><akt>22</akt></lebensenergie><ausdauer><akt>28</akt></ausdauer><konstitution><akt>8</akt></konstitution><koerperkraft><akt>8</akt></koerperkraft></eigenschaften><kampfsets><kampfset nr="1" tzm="true" inbenutzung="true" defaultrsmodel="true"><ausweichen>13</ausweichen><ini>11</ini><ruestungzonen><kopf>0</kopf><brust>0</brust><ruecken>0</ruecken><bauch>0</bauch><linkerarm>0</linkerarm><rechterarm>0</rechterarm><linkesbein>0</linkesbein><rechtesbein>0</rechtesbein></ruestungzonen><nahkampfwaffen><nahkampfwaffe><nummer>1</nummer><name>Magierstab als Stab</name><at>19</at><pa>14</pa><tp>1W+4</tp></nahkampfwaffe></nahkampfwaffen></kampfset></kampfsets></daten>`,
    expected: page => page.locator('.combat-page').innerText().then(async text => {
      assert.match(text, /22/);
      assert.match(text, /AT 19/);
      assert.match(text, /PA 14/);
      assert.equal(await page.locator('.combat-zone-row').count(), 8);
    })
  },
  {
    name: 'Ardor',
    xml: `<daten><angaben><name>Ardor Collen</name><wundschwelle>9</wundschwelle></angaben><eigenschaften><lebensenergie><akt>40</akt></lebensenergie><ausdauer><akt>35</akt></ausdauer></eigenschaften><kampfsets><kampfset nr="1" tzm="true" inbenutzung="true" defaultrsmodel="true"><ausweichen>11</ausweichen><ruestungzonen><kopf>5</kopf><brust>5</brust><ruecken>5</ruecken><bauch>5</bauch><linkerarm>5</linkerarm><rechterarm>5</rechterarm><linkesbein>3</linkesbein><rechtesbein>3</rechtesbein></ruestungzonen><nahkampfwaffen><nahkampfwaffe><nummer>1</nummer><name>(Lang-)Schwert</name><at>21</at><pa>16</pa><tpinkl>1W+5</tpinkl></nahkampfwaffe><nahkampfwaffe><nummer>2</nummer><name>(Lang-)Schwert</name><at>21</at><pa>16</pa><tpinkl>1W+5</tpinkl></nahkampfwaffe></nahkampfwaffen></kampfset><kampfset nr="2" tzm="true" inbenutzung="true"><ruestungzonen><kopf>5</kopf><brust>5</brust><ruecken>5</ruecken><bauch>5</bauch><linkerarm>5</linkerarm><rechterarm>5</rechterarm><linkesbein>3</linkesbein><rechtesbein>3</rechtesbein></ruestungzonen><schilder><schild><nummer>1</nummer><name>Buckler</name><pa>14</pa><typ>Paradewaffe</typ></schild></schilder></kampfset></kampfsets></daten>`,
    expected: page => page.locator('.combat-page').innerText().then(text => {
      assert.match(text, /40/);
      assert.match(text, /AT 21/);
      assert.match(text, /RS 5/);
      assert.match(text, /Nr\. 2/);
    })
  },
  {
    name: 'Cordula',
    xml: `<daten><angaben><name>Cordula</name><wundschwelle>9</wundschwelle></angaben><eigenschaften><lebensenergie><akt>33</akt></lebensenergie><ausdauer><akt>30</akt></ausdauer></eigenschaften><kampfsets><kampfset nr="1" tzm="true" inbenutzung="true"><ausweichen>14</ausweichen><ruestungzonen><kopf>0</kopf><brust>0</brust><ruecken>0</ruecken><bauch>0</bauch><linkerarm>1</linkerarm><rechterarm>1</rechterarm><linkesbein>1</linkesbein><rechtesbein>1</rechtesbein></ruestungzonen><fernkampfwaffen><fernkampfwaffe><nummer>1</nummer><name>Elfenbogen</name><at>27</at><tp>1W+5</tp><reichweite>10 / 25 / 50 / 100 / 200</reichweite><tpmod>3 / 2 / 1 / 1 / 0</tpmod><ladezeit>3</ladezeit><kampftalent>Bogen</kampftalent></fernkampfwaffe></fernkampfwaffen></kampfset></kampfsets></daten>`,
    expected: page => page.locator('.combat-page').innerText().then(text => {
      assert.match(text, /33/);
      assert.match(text, /Elfenbogen/);
      assert.match(text, /FK 27/);
    })
  }
];

(async () => {
  await fs.mkdir(screenshotDirectory, { recursive: true });

  for (const profile of profiles) {
    const fixture = await createAppFixture(publishDirectory, { userCount: 1, heroSourceXml: profile.xml });
    try {
      const { origin, pages, errors } = await fixture.prepare();
      const page = pages[0];
      await page.setViewportSize({ width: 1440, height: 900 });
      await page.goto(`${origin}/kampf`, { waitUntil: 'domcontentloaded' });
      await page.locator('.combat-page').waitFor();
      await page.locator('.combat-resource-strip').waitFor();
      await page.locator('.combat-zone-row').first().waitFor();
      await profile.expected(page);

      const geometry = await page.evaluate(() => ({
        clientWidth: document.body.clientWidth,
        scrollWidth: document.body.scrollWidth,
        sourceXmlText: document.body.innerText.includes('<daten>')
      }));
      assert.ok(geometry.scrollWidth <= geometry.clientWidth + 1);
      assert.equal(geometry.sourceXmlText, false);
      await page.screenshot({
        path: path.join(screenshotDirectory, `combat-p5-${profile.name.toLowerCase()}-1440.png`),
        fullPage: true
      });
      assert.equal(errors.length, 0, `${profile.name} browser errors: ${errors.join(' | ')}`);
    } finally {
      await fixture.close();
    }
  }

  const emptyFixture = await createAppFixture(publishDirectory, { userCount: 1 });
  try {
    const { origin, pages, errors } = await emptyFixture.prepare();
    const page = pages[0];
    await page.setViewportSize({ width: 390, height: 900 });
    await page.goto(`${origin}/kampf`, { waitUntil: 'domcontentloaded' });
    await page.locator('.combat-status-panel').waitFor();
    const emptyText = await page.locator('.combat-status-panel').innerText();
    assert.match(emptyText, /Kampfprofil|Kampfdaten|fehlen/i);
    assert.equal(await page.locator('.combat-resource-strip').count(), 0);
    assert.equal(await page.locator('.combat-hit-capture').count(), 0);
    assert.equal(errors.length, 0, `empty browser errors: ${errors.join(' | ')}`);
  } finally {
    await emptyFixture.close();
  }

  const responsiveFixture = await createAppFixture(publishDirectory, { userCount: 1, heroSourceXml: profiles[0].xml });
  try {
    const { origin, pages, errors } = await responsiveFixture.prepare();
    const page = pages[0];
    for (const width of viewports) {
      await page.setViewportSize({ width, height: 520 });
      await page.goto(`${origin}/kampf`, { waitUntil: 'domcontentloaded' });
      await page.locator('.combat-resource-strip').waitFor();
      const geometry = await page.evaluate(() => ({
        clientWidth: document.body.clientWidth,
        scrollWidth: document.body.scrollWidth,
        zoneRows: document.querySelectorAll('.combat-zone-row').length
      }));
      assert.equal(geometry.zoneRows, 8);
      assert.ok(geometry.scrollWidth <= geometry.clientWidth + 1,
        `horizontal overflow at ${width}px: ${geometry.scrollWidth} > ${geometry.clientWidth}`);
    }
    assert.equal(errors.length, 0, `responsive browser errors: ${errors.join(' | ')}`);
  } finally {
    await responsiveFixture.close();
  }

  console.log(JSON.stringify({ profiles: profiles.map(profile => profile.name), viewports, screenshots: screenshotDirectory }));
})().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
