// Focused management regression checks against a Release publish.
// Usage: node management-ui.cjs <publish-directory>
const assert = require('node:assert/strict');
const { createAppFixture } = require('./browser-fixture.cjs');

(async () => {
  const fixture = await createAppFixture(process.argv[2], { userCount: 3, heroesPerUser: 2 });
  let origin;
  let pages;
  let users;

  const waitFor = async (test, label) => {
    for (let i = 0; i < 100; i++) {
      if (await test()) return;
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    throw Error(label);
  };

  try {
    ({ origin, pages, users } = await fixture.prepare());
    const [master, player, leavingPlayer] = pages;

    await master.getByRole('button', { name: 'Erstellen', exact: true }).click();
    await master.getByPlaceholder('z.B. Borbarads Erben').fill('Managementrunde');
    await master.getByRole('button', { name: 'Session Erstellen', exact: true }).click();
    await master.locator('.wuerfel-page-container').waitFor();

    const session = await master.evaluate(async () => await (await fetch('/api/sessions/mine')).json().then(items => items[0]));
    assert.ok(session?.sessionId);
    for (const page of [player, leavingPlayer]) {
      await page.goto(origin);
      await page.getByPlaceholder('z.B. X7K9').fill(session.joinCode);
      await page.getByRole('button', { name: 'Sitzung Beitreten', exact: true }).click();
      await page.locator('.wuerfel-page-container').waitFor();
    }

    const details = () => master.evaluate(async id => await (await fetch(`/api/sessions/${id}`)).json(), session.sessionId);
    await waitFor(async () => (await details()).players.length === 3, 'three management test players missing');

    await master.goto(origin);
    const masterNode = master.locator('.lobby-page .session-node.active').first();
    await masterNode.waitFor();
    await masterNode.locator('.session-body').waitFor({ state: 'visible' });
    const masterToggle = masterNode.locator('.session-toggle');
    assert.equal(await masterToggle.getAttribute('aria-expanded'), 'true');
    await masterToggle.click();
    assert.equal(await masterToggle.getAttribute('aria-expanded'), 'false');
    assert.equal(await masterNode.locator('.session-body').isVisible(), false);

    await player.goto(origin);
    const playerNode = player.locator('.lobby-page .session-node.active').first();
    await playerNode.waitFor();
    await playerNode.getByRole('button', { name: 'Deinen Namen in dieser Session ändern', exact: true }).click();
    await playerNode.getByPlaceholder('Spielername', { exact: true }).fill('Refreshname');
    await playerNode.getByRole('button', { name: 'Speichern', exact: true }).click();
    await waitFor(async () => (await details()).players.some(item => item.name === 'Refreshname'), 'player rename missing');
    await waitFor(async () => !(await masterNode.locator('.session-body').isVisible()), 'manual collapse was reopened by refresh');

    await masterNode.locator('.session-toggle').click();
    await masterNode.getByRole('button', { name: 'Löschen', exact: true }).click();
    const deleteConfirmation = masterNode.locator('.session-confirmation');
    await deleteConfirmation.waitFor({ state: 'visible' });
    assert.match(await deleteConfirmation.innerText(), /Managementrunde/);
    await deleteConfirmation.getByRole('button', { name: 'Abbrechen', exact: true }).click();
    await deleteConfirmation.waitFor({ state: 'hidden' });
    assert.ok((await details()).sessionId === session.sessionId, 'delete cancel changed the session');

    await masterNode.getByRole('button', { name: 'Umbenennen', exact: true }).click();
    await masterNode.getByLabel('Sessionname', { exact: true }).fill('Umbenannte Runde');
    await masterNode.getByRole('button', { name: 'Speichern', exact: true }).click();
    await waitFor(async () => (await details()).name === 'Umbenannte Runde', 'session rename missing');

    await leavingPlayer.goto(origin);
    const leavingNode = leavingPlayer.locator('.lobby-page .session-node.active').first();
    await leavingNode.waitFor();
    const playersBeforeLeaveCancel = (await details()).players.length;
    await leavingNode.getByRole('button', { name: 'Session verlassen', exact: true }).click();
    const leaveConfirmation = leavingNode.locator('.session-confirmation');
    await leaveConfirmation.waitFor({ state: 'visible' });
    assert.match(await leaveConfirmation.innerText(), /Umbenannte Runde/);
    await leaveConfirmation.getByRole('button', { name: 'Abbrechen', exact: true }).click();
    await leaveConfirmation.waitFor({ state: 'hidden' });
    await new Promise(resolve => setTimeout(resolve, 150));
    assert.equal((await details()).players.length, playersBeforeLeaveCancel, 'leave cancel changed membership');

    await master.goto(`${origin}/helden-verwaltung`);
    await master.locator('.helden-verwaltung-page').waitFor();
    const heroRows = master.locator('.hero-row');
    await waitFor(async () => await heroRows.count() === 2, 'two seeded heroes missing');
    assert.equal(await master.locator('.hero-status.active').count(), 1, 'expected one active hero');

    const heroRow = name => master.locator('.hero-row').filter({ has: master.locator('.hero-name', { hasText: new RegExp(`^${name}$`) }) }).first();
    const activeHero = heroRow('Held0');
    await activeHero.getByRole('button', { name: 'Entfernen', exact: true }).click();
    const activeDeleteConfirmation = activeHero.locator('.hero-confirmation');
    await activeDeleteConfirmation.waitFor({ state: 'visible' });
    assert.match(await activeDeleteConfirmation.innerText(), /Held0/);
    assert.match(await activeDeleteConfirmation.innerText(), /aktive Held/);
    await activeDeleteConfirmation.getByRole('button', { name: 'Abbrechen', exact: true }).click();
    await activeDeleteConfirmation.waitFor({ state: 'hidden' });
    assert.equal(await heroRows.count(), 2, 'hero delete cancel changed the list');

    await heroRow('Held0-1').getByRole('button', { name: 'Aktivieren', exact: true }).click();
    await waitFor(async () => await master.locator('.hero-status.active').count() === 1, 'hero activation did not update the list');
    assert.equal(await heroRow('Held0-1').getByRole('button', { name: 'Aktiv', exact: true }).isDisabled(), true);

    const oldHero = heroRow('Held0');
    await oldHero.getByRole('button', { name: 'Entfernen', exact: true }).click();
    await oldHero.locator('.hero-confirmation').getByRole('button', { name: 'Entfernen bestätigen', exact: true }).click();
    await waitFor(async () => await heroRows.count() === 1, 'inactive hero was not removed');
    assert.equal(await master.locator('.hero-name', { hasText: /^Held0-1$/ }).count(), 1);

    const heroInput = master.locator('#hero-files');
    await heroInput.setInputFiles({ name: 'not-a-hero.txt', mimeType: 'text/plain', buffer: Buffer.from('invalid') });
    await master.getByRole('alert').filter({ hasText: 'Keine Datei akzeptiert' }).waitFor();
    assert.equal(await master.getByRole('button', { name: 'Importieren', exact: true }).count(), 0, 'invalid selection rendered an import action');

    await heroInput.setInputFiles(Array.from({ length: 16 }, (_, index) => ({
      name: `hero-${index}.xml`,
      mimeType: 'text/xml',
      buffer: Buffer.from('<hero />')
    })));
    await master.getByRole('alert').filter({ hasText: 'Maximal 15 Dateien' }).waitFor();

    const remainingHero = heroRow('Held0-1');
    await remainingHero.getByRole('button', { name: 'Entfernen', exact: true }).click();
    await remainingHero.locator('.hero-confirmation').getByRole('button', { name: 'Entfernen bestätigen', exact: true }).click();
    await waitFor(async () => await heroRows.count() === 0, 'active hero was not removed');
    assert.equal(await master.locator('.hero-status.active').count(), 0, 'an automatic replacement hero was activated');

    console.log('SessionTree and hero management, refresh-safe collapse, activation, import validation and confirmations passed');
    assert.deepEqual(fixture.errors, []);
  } catch (error) {
    if (pages?.[0]) console.log('Management page at failure:', await pages[0].locator('body').innerText({ timeout: 2000 }));
    throw error;
  } finally {
    await fixture.close();
  }
})().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
