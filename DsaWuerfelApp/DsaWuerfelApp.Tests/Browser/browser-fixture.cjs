const { chromium } = require('playwright');
const { DatabaseSync } = require('node:sqlite');
const { spawn } = require('node:child_process');
const { randomUUID, createHash } = require('node:crypto');
const fs = require('node:fs/promises');
const path = require('node:path');
const os = require('node:os');

async function createAppFixture(publishDirectory, { userCount = 3 } = {}) {
  const publish = path.resolve(publishDirectory);
  const temp = await fs.mkdtemp(path.join(os.tmpdir(), 'dsa-browser-fixture-'));
  const origin = 'http://127.0.0.1:5298';
  const threeRoot = path.resolve(path.dirname(require.resolve('three')), '..');
  let server;
  let browser;
  let db;
  const users = [];
  const contexts = [];
  const pages = [];
  const errors = [];

  const start = async () => {
    if (server && server.exitCode === null && server.signalCode === null) return;
    server = spawn(
      'dotnet',
      [path.join(publish, 'DsaWuerfelApp.dll')],
      {
        cwd: publish,
        windowsHide: true,
        stdio: 'ignore',
        env: {
          ...process.env,
          ASPNETCORE_ENVIRONMENT: 'Development',
          ASPNETCORE_URLS: origin,
          ConnectionStrings__HeroesDb: `Data Source=${path.join(temp, 'heroes.db')};Pooling=False`,
          DataProtection__KeysPath: path.join(temp, 'keys'),
          MagicLinkAuth__PublicBaseUrl: origin,
          MagicLinkAuth__ResendApiKey: ''
        }
      }
    );
    for (let i = 0; i < 100; i++) {
      try {
        if ((await fetch(origin)).ok) return;
      } catch {
        // The server is still starting.
      }
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    throw Error('test server did not start');
  };

  const stop = async () => {
    if (!server || server.exitCode !== null) return;
    const stopped = new Promise(resolve => server.once('exit', resolve));
    server.kill();
    await stopped;
  };

  const seedUsers = async () => {
    db = new DatabaseSync(path.join(temp, 'heroes.db'));
    for (let i = 0; i < userCount; i++) {
      const authId = randomUUID().toUpperCase();
      const id = authId.replaceAll('-', '').toLowerCase();
      const heroId = randomUUID().toUpperCase();
      const token = randomUUID();
      const email = `browser${i}@example.test`;
      const now = new Date().toISOString();
      const expires = new Date(Date.now() + 86400000).toISOString();
      db.prepare('INSERT INTO AuthUsers(Id,Email,DisplayName,CreatedAtUtc,LastLoginAtUtc) VALUES(?,?,?,?,?)')
        .run(authId, email, `Spieler${i}`, now, now);
      db.prepare('INSERT INTO MagicLinkTokens(Id,Email,TokenHash,RedirectPath,RequestedAtUtc,ExpiresAtUtc) VALUES(?,?,?,?,?,?)')
        .run(randomUUID().toUpperCase(), email, createHash('sha256').update(token).digest('hex').toUpperCase(), '/', now, expires);
      db.prepare('INSERT INTO Heroes(Id,OwnerUserId,IsActive,Name,Geschlecht,"Alter",Eigenschaften,SchlechteEigenschaften,Talente,Zauber,ImportVersion) VALUES(?,?,1,?,?,?,?,?,?,?,3)')
        .run(heroId, id, `Held${i}`, '', 20, JSON.stringify({ MU: 12, KL: 12, IN: 12, CH: 12, FF: 12, GE: 12, KO: 12, KK: 12 }), '{}', '{}', '{}');
      users.push({ id, heroId, token, email });
    }
    db.close();
    db = null;
  };

  const routeThree = async route => {
    const suffix = new URL(route.request().url()).pathname.split('/three@0.160.0/')[1];
    await route.fulfill({
      body: await fs.readFile(path.join(threeRoot, suffix)),
      contentType: 'text/javascript'
    });
  };

  const launch = async () => {
    browser = await chromium.launch({ channel: 'msedge', headless: true, args: ['--enable-unsafe-swiftshader'] });
    for (const user of users) {
      const context = await browser.newContext();
      contexts.push(context);
      await context.route('https://unpkg.com/three@0.160.0/**', routeThree);
      const page = await context.newPage();
      pages.push(page);
      page.on('pageerror', error => errors.push(error.message));
      page.on('console', message => {
        if (message.text().includes('Unhandled exception rendering')) errors.push(message.text());
      });
    }
  };

  const loginUsers = async () => {
    for (let i = 0; i < users.length; i++) {
      const page = pages[i];
      const user = users[i];
      await page.goto(`${origin}/auth/magic-link/verify?token=${user.token}`);
      await page.getByRole('button', { name: 'Logout', exact: true }).waitFor().catch(async error => {
        console.log('body', await page.locator('body').innerText());
        console.log('errors', errors);
        throw error;
      });
    }
  };

  const prepare = async () => {
    await start();
    await seedUsers();
    await launch();
    await loginUsers();
    return { origin, users, pages, errors };
  };

  const close = async () => {
    if (db) db.close();
    if (browser) await browser.close();
    await stop();
    if (!temp.startsWith(path.join(os.tmpdir(), 'dsa-browser-fixture-'))) {
      throw Error('Invalid cleanup path');
    }
    await fs.rm(temp, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 });
  };

  return { origin, users, pages, contexts, errors, start, stop, prepare, close };
}

module.exports = { createAppFixture };
