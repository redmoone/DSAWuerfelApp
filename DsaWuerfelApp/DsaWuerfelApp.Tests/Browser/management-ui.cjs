// Focused management regression checks against a Release publish.
// Usage: node management-ui.cjs <publish-directory>
const assert = require('node:assert/strict');
const { createAppFixture } = require('./browser-fixture.cjs');

(async () => {
  const fixture = await createAppFixture(process.argv[2], { userCount: 3 });
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

    console.log('SessionTree management, refresh-safe collapse, rename and confirmation cancel checks passed');
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
