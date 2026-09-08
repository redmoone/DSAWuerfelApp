// Isolated browser acceptance against a Release publish. Usage: node app-workflows.cjs <publish-directory>
const {chromium} = require('playwright');
const {DatabaseSync} = require('node:sqlite');
const {spawn} = require('node:child_process');
const {randomUUID, createHash} = require('node:crypto');
const fs = require('node:fs/promises'), path = require('node:path'), os = require('node:os');
const assert = require('node:assert/strict');
(async () => {
 const publish=path.resolve(process.argv[2]);
 const temp=await fs.mkdtemp(path.join(os.tmpdir(),'dsa-browser-acceptance-'));
 const origin='http://127.0.0.1:5298'; let server, browser, db; const pages=[];
 const start = async () => {
  server=spawn('dotnet',[path.join(publish,'DsaWuerfelApp.dll')],{cwd:publish,windowsHide:true,stdio:'ignore',env:{...process.env,ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:origin,ConnectionStrings__HeroesDb:`Data Source=${path.join(temp,'heroes.db')};Pooling=False`,DataProtection__KeysPath:path.join(temp,'keys'),MagicLinkAuth__PublicBaseUrl:origin,MagicLinkAuth__ResendApiKey:''}});
  for(let i=0;i<100;i++){try {if((await fetch(origin)).ok) return;} catch{} await new Promise(r=>setTimeout(r,100));} throw Error('test server did not start');
 };
 const stop = async () => {if(server && server.exitCode===null){const stopped=new Promise(r=>server.once('exit',r));server.kill();await stopped;}};
 try {
  await start();
  db=new DatabaseSync(path.join(temp,'heroes.db'));
  const users=[];
  for(let i=0;i<3;i++){
   const authId=randomUUID().toUpperCase(),id=authId.replaceAll('-','').toLowerCase(),heroId=randomUUID().toUpperCase(),token=randomUUID(),email=`browser${i}@example.test`,now=new Date().toISOString(),expires=new Date(Date.now()+86400000).toISOString();
   db.prepare('INSERT INTO AuthUsers(Id,Email,DisplayName,CreatedAtUtc,LastLoginAtUtc) VALUES(?,?,?,?,?)').run(authId,email,`Spieler${i}`,now,now);
   db.prepare('INSERT INTO MagicLinkTokens(Id,Email,TokenHash,RedirectPath,RequestedAtUtc,ExpiresAtUtc) VALUES(?,?,?,?,?,?)').run(randomUUID().toUpperCase(),email,createHash('sha256').update(token).digest('hex').toUpperCase(),'/',now,expires);
   db.prepare('INSERT INTO Heroes(Id,OwnerUserId,IsActive,Name,Geschlecht,"Alter",Eigenschaften,SchlechteEigenschaften,Talente,Zauber,ImportVersion) VALUES(?,?,1,?,?,?,?,?,?,?,3)').run(heroId,id,`Held${i}`,'',20,JSON.stringify({MU:12,KL:12,IN:12,CH:12,FF:12,GE:12,KO:12,KK:12}),'{}','{}','{}');
   users.push({id,heroId,token,email});
  }
  db.close(); db=null;
  browser=await chromium.launch({channel:'msedge',headless:true,args:['--enable-unsafe-swiftshader']});
  const contexts=[],errors=[];
  const threeRoot=path.resolve(path.dirname(require.resolve('three')),'..');
  for(const user of users){
   const context=await browser.newContext();contexts.push(context);
   await context.route('https://unpkg.com/three@0.160.0/**',async route=>{const suffix=new URL(route.request().url()).pathname.split('/three@0.160.0/')[1];await route.fulfill({body:await fs.readFile(path.join(threeRoot,suffix)),contentType:'text/javascript'});});
   const page=await context.newPage();pages.push(page);page.on('pageerror',e=>errors.push(e.message));page.on('console',msg=>{if(msg.text().includes('Unhandled exception rendering')) errors.push(msg.text());});
   await page.goto(`${origin}/auth/magic-link/verify?token=${user.token}`);
   await page.getByRole('button',{name:'Logout',exact:true}).waitFor().catch(async e=>{console.log('body',await page.locator('body').innerText());console.log('errors',errors);throw e;});
  }
  console.log('Three isolated browser logins passed');
  await pages[0].getByRole('button',{name:'Erstellen',exact:true}).click();
  await pages[0].getByPlaceholder('z.B. Borbarads Erben').fill('Browserrunde');
  await pages[0].getByRole('button',{name:'Session Erstellen',exact:true}).click();
  await pages[0].locator('.wuerfel-page-container').waitFor();
  const sessions=await pages[0].evaluate(async()=>await (await fetch('/api/sessions/mine')).json());
  const session=sessions[0]; assert.ok(session.sessionId);
  for(const page of pages.slice(1)){
   await page.getByPlaceholder('z.B. X7K9').fill(session.joinCode);
   await page.getByRole('button',{name:'Sitzung Beitreten',exact:true}).click();
   await page.locator('.wuerfel-page-container').waitFor();
  }
  const details = () => pages[0].evaluate(async id=>await (await fetch('/api/sessions/'+id)).json(),session.sessionId);
  const waitFor = async (test,label) => {for(let i=0;i<100;i++){if(await test())return;await new Promise(r=>setTimeout(r,100));}throw Error(label);};
  await waitFor(async()=>{const d=await details();return d.players.length===3 && d.players.every(p=>p.activeHeroId);},'hero sync missing');
  console.log('Session creation, two joins and three hero associations passed');
  const player=pages[1], master=pages[0];
  await player.locator('.die-selector').filter({has:player.getByText('6',{exact:true})}).click();
  await player.getByRole('button',{name:'W\u00fcrfeln',exact:true}).click();
  await waitFor(async()=>(await details()).history.length===1,'normal roll missing');
  await master.getByRole('button',{name:'Alle',exact:true}).click();
  await master.waitForFunction(()=>!document.querySelector('.text-pill-input').disabled);
  await master.locator('.attribute-pill').filter({has:master.getByText('MU',{exact:true})}).click();
  await master.getByRole('button',{name:'W\u00fcrfeln',exact:true}).click();
  await master.locator('.text-pill-input').fill('Vorbereiteter Wurf');
  await master.locator('.modifier-pill .mod-btn').last().click();
  const modifier=await master.locator('.modifier-pill .attribute-value').innerText();
  assert.equal(await player.evaluate(id=>localStorage.getItem('dsa.active-session:'+id),users[1].id),session.sessionId);
  await player.goto(origin);
  await player.locator('.active-session-strip').waitFor();
  const toggle=player.locator('.session-toggle:visible').first();
  await toggle.waitFor();
  if(!(await player.locator('.lobby-page .session-body').isVisible())) await toggle.click();
  await player.getByRole('button',{name:'Deinen Namen in dieser Session \u00e4ndern'}).click();
  await player.getByPlaceholder('Spielername',{exact:true}).fill('Umbenannt');
  await player.getByRole('button',{name:'Speichern',exact:true}).click();
  await waitFor(async()=>(await details()).players.some(p=>p.name==='Umbenannt'),'rename missing');
  assert.equal(await master.locator('.text-pill-input').inputValue(),'Vorbereiteter Wurf');
  assert.equal(await master.locator('.modifier-pill .attribute-value').innerText(),modifier);
  await pages[2].goto(origin);
  pages[2].once('dialog',dialog=>dialog.accept());
  await pages[2].getByRole('button',{name:'Session verlassen',exact:true}).click();
  await waitFor(async()=>(await details()).players.length===2,'leave missing');
  assert.ok((await details()).players.every(p=>p.activeHeroId));
  console.log('Normal/master rolls, rename form preservation and leave preservation passed');
  await player.goto(origin+'/wuerfel');
  await player.locator('.wuerfel-page-container').waitFor();
  await player.locator('.session-node.active').first().waitFor({state:'attached'});
  await player.waitForFunction(()=>!document.body.innerText.includes('Noch keine W\u00fcrfe in dieser Session.'));
  const history=(await details()).history.length;
  await stop();
  await player.locator('.die-selector').filter({has:player.getByText('6',{exact:true})}).click();
  await player.getByRole('button',{name:'W\u00fcrfeln',exact:true}).click();
  await player.getByText('Die Verbindung zur Sitzung wird wiederhergestellt. Bitte danach erneut w\u00fcrfeln.',{exact:true}).waitFor();
  await start();
  await waitFor(async()=>{const d=await details();return d.players.length===2 && d.players.every(p=>p.activeHeroId && p.isOnline);},'restart hero reconciliation missing');
  assert.equal((await details()).history.length,history);
  console.log('Restart reconnect restores heroes and history; disconnected roll creates no replacement');
  for(let i=0;i<10;i++){
   await master.locator('.nav-links a[href="kampf"]').click();
   await master.locator('.nav-links a[href=""]').click();
   await master.getByRole('button',{name:'Zur W\u00fcrfelseite',exact:true}).click();
   await master.locator('canvas').waitFor();
  }
  console.log('Ten actual page navigation cycles passed');
  assert.deepEqual(errors,[]);
 } catch(e) { if(pages[1]) console.log('Player page at failure:',await pages[1].locator('body').innerText({timeout:2000})); throw e; } finally {if(db)db.close();if(browser)await browser.close();await stop(); if(!temp.startsWith(path.join(os.tmpdir(),'dsa-browser-acceptance-')))throw Error('Invalid cleanup path'); await fs.rm(temp,{recursive:true,force:true,maxRetries:10,retryDelay:100});}
})().catch(e=>{console.error(e);process.exitCode=1;});
