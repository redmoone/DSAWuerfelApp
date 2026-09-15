const assert = require('node:assert/strict');
const { createAppFixture } = require('./browser-fixture.cjs');

const combatXml = `
<daten>
  <angaben><name>Sessionheld</name><wundschwelle>4</wundschwelle></angaben>
  <eigenschaften><mut><akt>14</akt></mut><klugheit><akt>13</akt></klugheit><intuition><akt>15</akt></intuition><charisma><akt>12</akt></charisma><fingerfertigkeit><akt>14</akt></fingerfertigkeit><gewandtheit><akt>14</akt></gewandtheit><konstitution><akt>8</akt></konstitution><koerperkraft><akt>8</akt></koerperkraft><lebensenergie><akt>22</akt></lebensenergie><ausdauer><akt>28</akt></ausdauer></eigenschaften>
  <kampfsets><kampfset nr="1" tzm="true" inbenutzung="true"><ausweichen>13</ausweichen><ini>11</ini><ruestungzonen><kopf>0</kopf><brust>0</brust><ruecken>0</ruecken><bauch>0</bauch><linkerarm>0</linkerarm><rechterarm>0</rechterarm><linkesbein>0</linkesbein><rechtesbein>0</rechtesbein></ruestungzonen><nahkampfwaffen><nahkampfwaffe><nummer>1</nummer><name>Magierstab</name><at>19</at><pa>14</pa><tp>1W+4</tp></nahkampfwaffe></nahkampfwaffen></kampfset></kampfsets>
</daten>`;

async function waitFor(test, label) {
  for (let attempt = 0; attempt < 100; attempt += 1) {
    if (await test()) return;
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  throw new Error(label);
}

async function readRows(page) {
  return page.locator('.combat-initiative-overview-row').evaluateAll(rows =>
    rows.map(row => row.innerText.trim()).sort());
}

(async () => {
  const fixture = await createAppFixture(process.argv[2], {
    userCount: 2,
    heroSourceXml: combatXml
  });

  try {
    const { origin, pages, errors } = await fixture.prepare();
    const [master, player] = pages;

    await master.getByRole('button', { name: 'Erstellen', exact: true }).click();
    await master.getByPlaceholder('z.B. Borbarads Erben').fill('Kampfübersicht-Test');
    await master.getByRole('button', { name: 'Session Erstellen', exact: true }).click();
    await master.locator('.wuerfel-page-container').waitFor();

    const session = await master.evaluate(async () =>
      (await (await fetch('/api/sessions/mine')).json())[0]);
    assert.ok(session?.sessionId);

    await player.getByPlaceholder('z.B. X7K9').fill(session.joinCode);
    await player.getByRole('button', { name: 'Sitzung Beitreten', exact: true }).click();
    await player.locator('.wuerfel-page-container').waitFor();

    const details = () => master.evaluate(async id =>
      (await fetch(`/api/sessions/${id}`)).json(), session.sessionId);
    await waitFor(async () => {
      const current = await details();
      return current.players.length === 2 && current.players.every(item => item.activeHeroId);
    }, 'session heroes were not associated');

    for (const page of [master, player]) {
      await page.setViewportSize({ width: 1440, height: 900 });
      await page.goto(`${origin}/kampf`, { waitUntil: 'domcontentloaded' });
      await page.locator('.hero-status-resources').waitFor();
      await page.locator('.combat-initiative-overview-list').waitFor();
      const overviewText = await page.locator('.combat-initiative-overview').innerText();
      assert.match(overviewText, /Vorbereitung|Noch kein gemeinsamer Startwurf/);
      assert.match(overviewText, /Held0/);
      assert.match(overviewText, /Held1/);
      assert.match(overviewText, /INI —/);
    }

    const rollInitiative = async page => {
      await page.locator('.combat-action-options button').filter({ hasText: 'Initiative' }).click();
      await page.getByRole('button', { name: 'Initiative würfeln', exact: true }).click();
    };
    await rollInitiative(master);
    await waitFor(async () => !(await master.locator('.combat-initiative-overview-row').filter({ hasText: 'Held0' }).innerText()).includes('INI —'),
      'master initiative did not appear in the persistent overview');
    await waitFor(async () => (await readRows(player)).some(row => row.includes('Held0') && !row.includes('INI —')),
      'master initiative did not synchronize to the second session client');

    await rollInitiative(player);
    await player.waitForTimeout(500);
    await waitFor(async () => {
      const rows = await readRows(master);
      return rows.length === 2 && rows.every(row => !row.includes('INI —')) && rows.some(row => row.includes('JETZT'));
    }, 'shared initiative order did not settle');
    await waitFor(async () => {
      const masterRows = await readRows(master);
      const playerRows = await readRows(player);
      return JSON.stringify(masterRows) === JSON.stringify(playerRows);
    }, 'initiative snapshots diverged between session clients');

    for (const page of [master, player]) {
      await page.getByRole('tab', { name: 'Rüstung & Wunden', exact: true }).click();
      await page.locator('.combat-zone-row').first().waitFor();
      assert.equal(await page.locator('.combat-initiative-overview-row').count(), 2);
      await page.getByRole('tab', { name: 'Kampf', exact: true }).click();
    }

    await master.getByRole('tab', { name: 'Rundenübersicht', exact: true }).click();
    await master.locator('.combat-initiative-list').waitFor();
    await master.locator('.combat-work-area').evaluate(element => { element.style.minHeight = '1600px'; });
    await master.evaluate(() => window.scrollTo(0, document.body.scrollHeight));
    const stickyRect = await master.locator('.combat-initiative-overview').boundingBox();
    assert.ok(stickyRect && stickyRect.y >= 0 && stickyRect.y < 120,
      `desktop initiative overview was not sticky: ${JSON.stringify(stickyRect)}`);

    await player.setViewportSize({ width: 390, height: 844 });
    await player.goto(`${origin}/kampf`, { waitUntil: 'domcontentloaded' });
    await player.locator('.combat-initiative-overview-list').waitFor();
    const mobileListStyle = await player.locator('.combat-initiative-overview-list').evaluate(element => {
      const style = getComputedStyle(element);
      return { overflowX: style.overflowX, overflowY: style.overflowY, display: style.display };
    });
    assert.equal(mobileListStyle.overflowX, 'auto');
    assert.equal(await player.locator('.combat-initiative-overview-row').count(), 2);
    assert.equal(errors.length, 0, `browser errors: ${errors.join(' | ')}`);
    console.log(JSON.stringify({ sessionOverview: true, rows: await readRows(master), mobileListStyle }));
  } finally {
    await fixture.close();
  }
})().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
