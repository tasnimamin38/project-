/* ═══════════════════════════════════════════════════════════════
   ANOMALY · MV-7 — app.js
   Modules: GravityField (Three.js) · Wallet · SandboxStore ·
            SecretGate (Konami + hash) · BioAuth · OwnerConsole
   ═══════════════════════════════════════════════════════════════ */
'use strict';

const $  = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => [...r.querySelectorAll(s)];
const fmt = n => Math.round(n).toLocaleString('en-US');

const MASTER_KEY = 'Tasnim@25714$';          // owner credential (front-end demo gate)
const SECRET_HASH = '#root-gateway';          // hidden deep path
const KONAMI = ['ArrowUp','ArrowUp','ArrowDown','ArrowDown','ArrowLeft','ArrowRight','ArrowLeft','ArrowRight','b','a'];

/* ─────────────────────────── boot flash ─────────────────────── */
(function boot(){
  const b = document.createElement('div');
  b.className = 'boot-flash';
  b.innerHTML = '<span>ANOMALY // GRAVITY NULLIFIED ▮</span>';
  document.body.appendChild(b);
  setTimeout(() => b.remove(), 2200);
})();

/* ═══════════════ 1. THREE.JS ANTI-GRAVITY FIELD ═══════════════ */
class GravityField {
  constructor(mount){
    this.mount = mount;
    this.mouse = { x: 0, y: 0, wx: 0, wy: 0 };   // normalized + world-projected
    this.clock = new THREE.Clock();
    this.initScene();
    this.initParticles();
    this.initDebris();
    this.initCore();
    this.bind();
    this.animate();
  }

  initScene(){
    this.scene = new THREE.Scene();
    this.scene.fog = new THREE.FogExp2(0x03060d, 0.028);
    this.camera = new THREE.PerspectiveCamera(62, innerWidth / innerHeight, 0.1, 200);
    this.camera.position.set(0, 0, 26);
    this.renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true });
    this.renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
    this.renderer.setSize(innerWidth, innerHeight);
    this.mount.appendChild(this.renderer.domElement);

    const key = new THREE.PointLight(0x00f0ff, 1.6, 90);
    key.position.set(12, 10, 18);
    const rim = new THREE.PointLight(0xff6b1a, 1.1, 80);
    rim.position.set(-16, -8, 10);
    const fill = new THREE.PointLight(0x2f7bff, .9, 100);
    fill.position.set(0, -14, -10);
    this.scene.add(key, rim, fill);
    this.keyLight = key;
  }

  /* zero-G particle system with drift + mouse repulsion */
  initParticles(){
    const N = 1400;
    const pos = new Float32Array(N * 3);
    const vel = new Float32Array(N * 3);
    const col = new Float32Array(N * 3);
    const palette = [[0,.94,1],[.18,.48,1],[1,.42,.1],[1,.77,.26]];
    for(let i = 0; i < N; i++){
      pos[i*3]   = (Math.random()-.5)*70;
      pos[i*3+1] = (Math.random()-.5)*46;
      pos[i*3+2] = (Math.random()-.5)*40 - 6;
      vel[i*3]   = (Math.random()-.5)*.02;
      vel[i*3+1] = (Math.random()-.5)*.02;
      vel[i*3+2] = (Math.random()-.5)*.01;
      const c = palette[Math.random()*palette.length|0];
      col[i*3]=c[0]; col[i*3+1]=c[1]; col[i*3+2]=c[2];
    }
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(pos,3));
    g.setAttribute('color', new THREE.BufferAttribute(col,3));
    const m = new THREE.PointsMaterial({ size:.14, vertexColors:true, transparent:true, opacity:.85,
      blending:THREE.AdditiveBlending, depthWrite:false });
    this.points = new THREE.Points(g, m);
    this.pVel = vel;
    this.scene.add(this.points);
  }

  /* floating debris shards orbiting in anti-gravity */
  initDebris(){
    this.debris = [];
    const geos = [
      () => new THREE.IcosahedronGeometry(1, 0),
      () => new THREE.TetrahedronGeometry(1.1, 0),
      () => new THREE.OctahedronGeometry(1, 0),
      () => new THREE.BoxGeometry(.7, 1.6, .5),
      () => new THREE.TorusGeometry(.9, .16, 6, 14)
    ];
    const colors = [0x00f0ff, 0x2f7bff, 0xff6b1a, 0x9fb8ff, 0x0c1a33];
    for(let i = 0; i < 26; i++){
      const geo = geos[i % geos.length]();
      const wire = i % 3 === 0;
      const mat = wire
        ? new THREE.MeshBasicMaterial({ color: colors[i % colors.length], wireframe: true, transparent: true, opacity: .5 })
        : new THREE.MeshStandardMaterial({ color: 0x0a1428, metalness: .85, roughness: .3,
            emissive: colors[i % colors.length], emissiveIntensity: .18 });
      const mesh = new THREE.Mesh(geo, mat);
      const s = .35 + Math.random() * 1.15;
      mesh.scale.setScalar(s);
      mesh.position.set((Math.random()-.5)*52, (Math.random()-.5)*34, (Math.random()-.5)*26 - 4);
      mesh.userData = {
        base: mesh.position.clone(),
        spin: new THREE.Vector3((Math.random()-.5)*.012,(Math.random()-.5)*.012,(Math.random()-.5)*.01),
        bobF: .3 + Math.random()*.7, bobA: 1 + Math.random()*2.4, ph: Math.random()*6.28,
        vel: new THREE.Vector3()
      };
      this.scene.add(mesh);
      this.debris.push(mesh);
    }
  }

  /* pulsing anomaly core (the singularity the whole field orbits) */
  initCore(){
    this.coreGroup = new THREE.Group();
    const core = new THREE.Mesh(
      new THREE.IcosahedronGeometry(2.1, 1),
      new THREE.MeshBasicMaterial({ color: 0x00f0ff, wireframe: true, transparent: true, opacity: .35 })
    );
    const inner = new THREE.Mesh(
      new THREE.SphereGeometry(1.15, 24, 24),
      new THREE.MeshStandardMaterial({ color: 0x02040a, emissive: 0xff6b1a, emissiveIntensity: .55, metalness: 1, roughness: .2 })
    );
    const ring = new THREE.Mesh(
      new THREE.TorusGeometry(3.4, .06, 8, 80),
      new THREE.MeshBasicMaterial({ color: 0x2f7bff, transparent: true, opacity: .5 })
    );
    ring.rotation.x = 1.2;
    const ring2 = ring.clone();
    ring2.rotation.set(-.7, .5, 0);
    ring2.material = new THREE.MeshBasicMaterial({ color: 0x00f0ff, transparent: true, opacity: .4 });
    this.coreGroup.add(core, inner, ring, ring2);
    this.coreGroup.position.set(9, 3, -8);
    this.scene.add(this.coreGroup);
    this.coreParts = { core, inner, ring, ring2 };
  }

  bind(){
    addEventListener('resize', () => {
      this.camera.aspect = innerWidth / innerHeight;
      this.camera.updateProjectionMatrix();
      this.renderer.setSize(innerWidth, innerHeight);
    });
    addEventListener('pointermove', e => {
      this.mouse.x = (e.clientX / innerWidth) * 2 - 1;
      this.mouse.y = -(e.clientY / innerHeight) * 2 + 1;
    }, { passive: true });
    // scroll parallax
    addEventListener('scroll', () => {
      this.scrollY = scrollY;
    }, { passive: true });
  }

  animate(){
    const t = this.clock.getElapsedTime();
    // camera drifts toward mouse — reactive physics field feel
    const tx = this.mouse.x * 3.2, ty = this.mouse.y * 2.1;
    this.camera.position.x += (tx - this.camera.position.x) * .045;
    this.camera.position.y += (ty - this.camera.position.y) * .045;
    const sc = (this.scrollY || 0) * .0016;
    this.camera.position.z = 26 + Math.sin(sc) * 3;
    this.camera.lookAt(0, 0, 0);

    // project mouse into world space at z=0 for repulsion
    const v = new THREE.Vector3(this.mouse.x, this.mouse.y, .5).unproject(this.camera);
    const dir = v.sub(this.camera.position).normalize();
    const dist = -this.camera.position.z / dir.z;
    this.mouse.wx = this.camera.position.x + dir.x * dist;
    this.mouse.wy = this.camera.position.y + dir.y * dist;

    // particles: drift + swirl + cursor repulsion
    const p = this.points.geometry.attributes.position.array;
    const vel = this.pVel;
    for(let i = 0; i < p.length; i += 3){
      // gentle curl swirl (anti-gravity turbulence)
      vel[i]   += Math.sin(p[i+1]*.15 + t*.4)*.0006;
      vel[i+1] += Math.cos(p[i]*.12 + t*.3)*.0006;
      // repel from cursor field radius 7
      const dx = p[i]-this.mouse.wx, dy = p[i+1]-this.mouse.wy;
      const d2 = dx*dx + dy*dy;
      if(d2 < 49 && d2 > .001){
        const f = .35 / d2;
        vel[i] += dx*f; vel[i+1] += dy*f;
      }
      vel[i] *= .985; vel[i+1] *= .985; vel[i+2] *= .985;
      p[i]+=vel[i]; p[i+1]+=vel[i+1]; p[i+2]+=vel[i+2];
      // wrap bounds
      if(p[i] >  36) p[i] = -36; if(p[i] < -36) p[i] = 36;
      if(p[i+1] > 24) p[i+1] = -24; if(p[i+1] < -24) p[i+1] = 24;
      if(p[i+2] > 14) p[i+2] = -26; if(p[i+2] < -26) p[i+2] = 14;
    }
    this.points.geometry.attributes.position.needsUpdate = true;

    // debris: bob + slow push away from cursor (physics shove)
    for(const d of this.debris){
      const u = d.userData;
      d.rotation.x += u.spin.x; d.rotation.y += u.spin.y; d.rotation.z += u.spin.z;
      const bx = u.base.x + Math.sin(t*u.bobF + u.ph)*u.bobA;
      const by = u.base.y + Math.cos(t*u.bobF*.8 + u.ph)*u.bobA*.6;
      const dx = bx - d.position.x, dy = by - d.position.y;
      const sx = d.position.x - this.mouse.wx, sy = d.position.y - this.mouse.wy;
      const dd = sx*sx + sy*sy;
      if(dd < 90){ const f = 1.6/Math.max(dd,1); u.vel.x += sx*f*.016; u.vel.y += sy*f*.016; }
      u.vel.multiplyScalar(.94);
      d.position.x += dx*.02 + u.vel.x;
      d.position.y += dy*.02 + u.vel.y;
      u.vel.setLength(Math.min(u.vel.length(), .3));
    }

    // core pulse
    const { core, inner, ring, ring2 } = this.coreParts;
    const pulse = 1 + Math.sin(t*2.1)*.08;
    core.scale.setScalar(pulse);
    inner.scale.setScalar(1 + Math.sin(t*3.3)*.05);
    inner.material.emissiveIntensity = .45 + Math.sin(t*3.3)*.25;
    core.rotation.y += .004; core.rotation.x += .002;
    ring.rotation.z += .01; ring2.rotation.z -= .008;
    this.coreGroup.position.y = 3 + Math.sin(t*.6)*1.4;

    this.keyLight.position.x = this.mouse.wx * 1.4;
    this.keyLight.position.y = this.mouse.wy * 1.4;

    this.renderer.render(this.scene, this.camera);
    requestAnimationFrame(() => this.animate());
  }
}
if (window.THREE) new GravityField($('#gravity-field'));

/* ═══════════════ 2. WALLET + SANDBOX STORE ═══════════════ */
const Wallet = {
  state: { gold: 0, diamond: 0, plat: 0 },
  owned: new Set(),
  load(){ try{ Object.assign(this.state, JSON.parse(sessionStorage.getItem('anomaly_wallet')||'{}')); }catch(e){} },
  save(){ sessionStorage.setItem('anomaly_wallet', JSON.stringify(this.state)); },
  render(changed){
    for(const k of ['gold','diamond','plat']){
      const el = $(`[data-wallet="${k}"]`);
      if(el) el.textContent = fmt(this.state[k]);
    }
    if(changed){
      const chip = $(`.chip-${changed === 'diamond' ? 'diamond' : changed === 'plat' ? 'plat' : 'gold'}`);
      if(chip){ chip.classList.remove('pulse'); void chip.offsetWidth; chip.classList.add('pulse'); }
    }
  },
  add(cur, amt){ this.state[cur] = Math.max(0, this.state[cur] + amt); this.save(); this.render(cur); },
};
Wallet.load(); Wallet.render();

function toast(title, msg, kind = ''){
  const stack = $('#toastStack');
  const t = document.createElement('div');
  t.className = `toast ${kind}`;
  t.innerHTML = `<b>${title}</b>${msg}`;
  stack.appendChild(t);
  setTimeout(() => { t.classList.add('out'); setTimeout(() => t.remove(), 420); }, 3400);
  while(stack.children.length > 4) stack.firstChild.remove();
}

const CUR_LABEL = { gold: 'Gold', diamond: 'Diamonds', plat: 'Platinum' };

document.addEventListener('click', e => {
  const btn = e.target.closest('.buy');
  if(!btn) return;
  const cur  = btn.dataset.cur;
  const amt  = parseInt(btn.dataset.amt, 10);
  const item = btn.dataset.item || '';

  if(amt < 0){ // spend-path items still FREE-granted in sandbox mode: grant then mark owned
    Wallet.add(cur, -amt);                       // sandbox: refund-style grant, no real charge
    toast('SANDBOX GRANT', `${fmt(-amt)} ${CUR_LABEL[cur]} credited to your session balance${item ? ` · ${item}` : ''}. No payment taken.`);
  } else {
    Wallet.add(cur, amt);                        // instant free credit — testing feature
    toast('SANDBOX GRANT', `+${fmt(amt)} ${CUR_LABEL[cur]} added instantly${item ? ` · ${item}` : ''}. Settlement handled manually by backend in production.`);
  }
  if(btn.dataset.grant === 'pass'){
    toast('BATTLE PASS', 'Inversion Premium Pass activated — Tier 25 boost applied to your account.', 'gold');
  }
  const card = btn.closest('.scard');
  if(card && item.includes('skin')){
    card.classList.add('owned');
    btn.textContent = 'EQUIPPED ✓';
  }
});

/* store tabs */
$$('.tab').forEach(tab => tab.addEventListener('click', () => {
  $$('.tab').forEach(t => t.classList.toggle('active', t === tab));
  $$('.tab-panel').forEach(p => p.classList.toggle('active', p.id === `panel-${tab.dataset.tab}`));
}));

/* mobile nav */
$('#navToggle').addEventListener('click', () => $('#navLinks').classList.toggle('open'));
$$('#navLinks a').forEach(a => a.addEventListener('click', () => $('#navLinks').classList.remove('open')));

/* reveal on scroll */
const io = new IntersectionObserver(es => es.forEach(en => {
  if(en.isIntersecting){ en.target.classList.add('in'); io.unobserve(en.target); }
}), { threshold: .12 });
$$('.reveal').forEach(el => io.observe(el));

/* ═══════════════ 3. SECRET GATE (Konami + #root-gateway) ═══════════════ */
let kIdx = 0;
addEventListener('keydown', e => {
  const key = e.key.length === 1 ? e.key.toLowerCase() : e.key;
  kIdx = (key === KONAMI[kIdx]) ? kIdx + 1 : (key === KONAMI[0] ? 1 : 0);
  if(kIdx === KONAMI.length){ kIdx = 0; openGate(); }
});
addEventListener('hashchange', () => { if(location.hash === SECRET_HASH) openGate(); });
if(location.hash === SECRET_HASH) setTimeout(openGate, 900);

const gate = $('#adminOverlay');
function openGate(){
  gate.hidden = false;
  document.body.style.overflow = 'hidden';
  startBioScans();
  setTimeout(() => $('#masterKey')?.focus(), 700);
}
function closeGate(){
  gate.hidden = true;
  document.body.style.overflow = '';
  if(location.hash === SECRET_HASH) history.replaceState(null,'',location.pathname + location.search);
  resetBio();
}
$('#adminClose').addEventListener('click', closeGate);

/* ═══════════════ 4. BIOMETRIC AUTH SEQUENCE ═══════════════ */
let bioOK = { face: false, fp: false };
function resetBio(){
  bioOK = { face:false, fp:false };
  $('#faceScan').className = 'face-scan';
  $('#fingerScan').className = 'finger-scan';
  $('#faceStatus').textContent = 'FACE SCAN · STANDBY';
  $('#fpStatus').textContent = 'PRINT · AWAITING';
  $('#authMsg').textContent = ''; $('#authMsg').className = 'auth-msg';
  $('#masterKey').value = '';
}
function startBioScans(){
  const fs = $('#faceScan'), fp = $('#fingerScan');
  fs.classList.add('scanning');
  $('#faceStatus').textContent = 'SCANNING TOPOLOGY…';
  setTimeout(() => {
    fs.classList.remove('scanning'); fs.classList.add('ok-face');
    $('#faceStatus').textContent = 'FACE VERIFIED · OWNER MATCH';
    bioOK.face = true;
  }, 2600);
  setTimeout(() => {
    fp.classList.add('scanning-fp');
    $('#fpStatus').textContent = 'READING RIDGES…';
  }, 1400);
  setTimeout(() => {
    fp.classList.remove('scanning-fp'); fp.classList.add('ok-fp');
    $('#fpStatus').textContent = 'PRINT VERIFIED · 99.7%';
    bioOK.fp = true;
  }, 4200);
}

$('#pwEye').addEventListener('click', () => {
  const inp = $('#masterKey');
  inp.type = inp.type === 'password' ? 'text' : 'password';
});

$('#credForm').addEventListener('submit', e => {
  e.preventDefault();
  const msg = $('#authMsg');
  const val = $('#masterKey').value;
  if(val !== MASTER_KEY){
    msg.textContent = 'ACCESS DENIED · KEY REJECTED · INCIDENT LOGGED';
    msg.className = 'auth-msg bad';
    $('.admin-frame').classList.add('shake');
    setTimeout(() => $('.admin-frame').classList.remove('shake'), 450);
    return;
  }
  if(!(bioOK.face && bioOK.fp)){
    msg.textContent = 'BIOMETRICS PENDING · STAND BY FOR VERIFICATION…';
    msg.className = 'auth-msg';
    setTimeout(() => $('#credForm').dispatchEvent(new Event('submit')), 900);
    return;
  }
  msg.textContent = 'CLEARANCE CONFIRMED · WELCOME, OWNER.';
  msg.className = 'auth-msg good';
  setTimeout(() => { closeGate(); openConsole(); }, 700);
});

/* ═══════════════ 5. OWNER CONTROL CENTER ═══════════════ */
const consoleEl = $('#adminConsole');
const S = {                      // live simulated telemetry state
  players: 1_247_832, spm: 20_410, vol: 384_920,
  inflation: 0, drop: 1, xp: 1,
  maintenance: false,
  threats: { inj: 1420, hij: 87, wh: 33 },
  series: [], nodes: [], users: [], logs: []
};
for(let i = 0; i < 60; i++) S.series.push(S.players + Math.sin(i/6)*40000 + (Math.random()-.5)*30000);

const WORLDS = ['Nexus-0','Ember Drift','Shatterfield','Glass Orbit'];
const RANKS  = ['Gravity Crown','Singularity','Event Horizon','Freefaller','Voidborn','Orbital'];
const HANDLES = ['NullVector','EchoWren','Gravitas','SpaghettI','MvSeven','ZeroAsh','DriftKing','IonSpectre',
  'QuasarKit','PlummetPro','AntiCore','Vantablack','Tachyon9','Slipstream','HaloBreak','Massless'];

function seedUsers(){
  S.users = Array.from({length: 16}, (_,i) => ({
    uid: 'MV-' + (70400 + i*137),
    handle: HANDLES[i % HANDLES.length] + (i > 11 ? i : ''),
    world: WORLDS[i % 4], rank: RANKS[(i*3) % 6],
    level: 12 + ((i*17) % 88), score: 4000 + i*971 + (i%5)*2200,
    spend: +( (i%7)*43.5 + 12 ).toFixed(2),
    status: i % 6 === 0 ? 'idle' : 'online'
  }));
}
seedUsers();

function seedNodes(){
  const regions = ['EU-WEST','NA-EAST','NA-WEST','AP-SOUTH','AP-NE','SA-EAST','EU-NORTH','MENA','OCE','AF-SOUTH','LATAM-2','ORBIT-RELAY'];
  S.nodes = regions.map((r,i) => ({
    name: `ANOM-${r}`, state: i===4 ? 'degraded' : i===9 ? 'offline' : 'online',
    ping: 8 + (i*7)%46, load: 22 + (i*13)%70, players: 40000 + i*18500
  }));
}
seedNodes();

function openConsole(){
  consoleEl.hidden = false;
  document.body.style.overflow = 'hidden';
  $('#sessionId').textContent = 'SES-' + Math.random().toString(36).slice(2,10).toUpperCase();
  logAccess('ROOT SESSION OPENED · TIER-0 CLEARANCE GRANTED');
  renderServers(); renderUsers(); renderWorldBars(); renderMoneyBars(); drawChart(); tickClock();
  feedEvent('Owner console attached to global bus.');
}
function lockConsole(){
  consoleEl.hidden = true;
  document.body.style.overflow = '';
  logAccess('CONSOLE LOCKED BY OWNER');
}
$('#consoleExit').addEventListener('click', lockConsole);
$('#logoutBtn').addEventListener('click', () => { lockConsole(); toast('SESSION CLOSED','Owner console locked. Biometric keys purged from memory.','err'); });

/* side nav */
$$('.side-btn').forEach(b => b.addEventListener('click', () => {
  $$('.side-btn').forEach(x => x.classList.toggle('active', x === b));
  $$('.view').forEach(v => v.classList.toggle('active', v.id === `view-${b.dataset.view}`));
  if(b.dataset.view === 'metrics') drawChart();
}));

/* clock */
function tickClock(){
  const d = new Date();
  $('#conTime').textContent = d.toTimeString().slice(0,8) + ' UTC';
  $('#conDate').textContent = d.toDateString().toUpperCase();
}
setInterval(() => { if(!consoleEl.hidden) tickClock(); }, 1000);

/* live metrics simulation */
setInterval(() => {
  if(consoleEl.hidden) return;
  const drift = (Math.random()-.48) * 9000 * (1 + S.inflation/40);
  S.players = Math.max(400000, S.players + drift);
  S.spm = Math.max(4000, S.spm + (Math.random()-.5)*900);
  S.vol = Math.max(50000, S.vol + Math.random()*2400*(1+S.drop/2));
  S.series.push(S.players); S.series.shift();
  $('#mPlayers').textContent = fmt(S.players);
  $('#mSpm').textContent = fmt(S.spm);
  $('#mVol').textContent = '$' + fmt(S.vol);
  $('#mPlayersD').textContent = '▲ ' + (Math.abs(drift/S.players*100)).toFixed(2) + '%';
  $('#mVolD').textContent = '▲ ' + (Math.random()*4+1).toFixed(1) + '%';
  $('#mUp').textContent = (99.9 + Math.random()*0.09).toFixed(2) + '%';
  S.threats.inj += Math.floor(Math.random()*4);
  if(Math.random()<.25) S.threats.hij++;
  if(Math.random()<.15) S.threats.wh++;
  $('#tInj').textContent = fmt(S.threats.inj);
  $('#tHij').textContent = fmt(S.threats.hij);
  $('#tWh').textContent = fmt(S.threats.wh);
  drawChart();
  if(Math.random()<.5) randomFeedEvent();
}, 1600);

/* main chart (canvas line) */
function drawChart(){
  const cv = $('#chartMain'); if(!cv) return;
  const ctx = cv.getContext('2d');
  const W = cv.width, H = cv.height;
  ctx.clearRect(0,0,W,H);
  const min = Math.min(...S.series), max = Math.max(...S.series);
  const X = i => i/(S.series.length-1)*(W-20)+10;
  const Y = v => H-14 - (v-min)/((max-min)||1)*(H-34);
  // grid
  ctx.strokeStyle = '#102240'; ctx.lineWidth = 1;
  for(let g = 0; g <= 4; g++){ const y = 10 + g*(H-24)/4;
    ctx.beginPath(); ctx.moveTo(10,y); ctx.lineTo(W-10,y); ctx.stroke(); }
  // area glow
  const grad = ctx.createLinearGradient(0,0,0,H);
  grad.addColorStop(0,'rgba(0,240,255,.30)'); grad.addColorStop(1,'rgba(0,240,255,0)');
  ctx.beginPath(); ctx.moveTo(X(0),H-10);
  S.series.forEach((v,i)=>ctx.lineTo(X(i),Y(v)));
  ctx.lineTo(X(S.series.length-1),H-10); ctx.closePath();
  ctx.fillStyle = grad; ctx.fill();
  // line
  ctx.beginPath(); S.series.forEach((v,i)=> i?ctx.lineTo(X(i),Y(v)):ctx.moveTo(X(i),Y(v)));
  ctx.strokeStyle = '#00f0ff'; ctx.lineWidth = 2; ctx.shadowColor='#00f0ff'; ctx.shadowBlur=8; ctx.stroke();
  ctx.shadowBlur = 0;
  // head dot
  const last = S.series[S.series.length-1];
  ctx.beginPath(); ctx.arc(X(S.series.length-1),Y(last),4,0,7); ctx.fillStyle='#ff6b1a';
  ctx.shadowColor='#ff6b1a'; ctx.shadowBlur=10; ctx.fill(); ctx.shadowBlur=0;
  // labels
  ctx.fillStyle = '#7d8bab'; ctx.font = '11px Rajdhani, sans-serif';
  ctx.fillText(fmt(max)+' peak', 12, 20); ctx.fillText(fmt(min)+' floor', 12, H-16);
}

/* bars helpers */
function barRow(label, pct, cls=''){
  return `<div class="bar-row"><span>${label}</span><div class="bar-track"><div class="bar-fill ${cls}" style="width:${pct}%"></div></div><b>${pct.toFixed(0)}%</b></div>`;
}
function renderWorldBars(){
  const weights = [.34,.24,.26,.16].map(w => w*(1+S.inflation/120));
  const total = weights.reduce((a,b)=>a+b,0);
  $('#worldBars').innerHTML = WORLDS.map((w,i)=>barRow(w, weights[i]/total*100, i===2?'o':'')).join('');
}
function renderMoneyBars(){
  const infl = S.inflation;
  const rows = [
    ['Gold faucet', Math.min(100, 46+infl*1.6+S.drop*6), 'g'],
    ['Gold sink',   Math.min(100, 38+(($('#tglSink')?.checked)?14:0)), ''],
    ['Diamond burn',Math.min(100, 52+infl*.9), ''],
    ['Platinum flow',Math.min(100, 30+(($('#tglPlatBonus')?.checked)?18:0)), 'p'],
  ];
  $('#moneyBars').innerHTML = rows.map(r=>barRow(...r)).join('');
}

/* event feed */
const FEEDS = [
  ['season','Season 04 queue pop averaged <b>11s</b> globally.'],
  ['econ','Platinum purchases up <b>{p}%</b> after Double Platinum toggle.'],
  ['world','<b>Glass Orbit</b> echo-duels spiked — shard count <b>{n}</b>.'],
  ['shop','Top-up surge detected in <b>NA-EAST</b>: <b>{n}</b> txns/min.'],
  ['sec','Rate-limiter absorbed <b>{n}</b> malformed auth attempts.'],
  ['match','Ranked inversion final: <b>NullVector</b> retains Gravity Crown.'],
  ['ops','Node heartbeat sweep completed · <b>{n}</b> nodes green.'],
];
function feedEvent(html){
  const ul = $('#eventFeed'); if(!ul) return;
  const li = document.createElement('li');
  li.innerHTML = html + `<time>${new Date().toTimeString().slice(0,8)}</time>`;
  ul.prepend(li);
  while(ul.children.length > 14) ul.lastChild.remove();
}
function randomFeedEvent(){
  const [ , tpl ] = FEEDS[Math.random()*FEEDS.length|0];
  feedEvent(tpl.replace('{n}', fmt(200+Math.random()*9000|0)).replace('{p}', (Math.random()*40+5).toFixed(1)));
}

/* economy controls */
const inflS = $('#inflSlider'), dropS = $('#dropSlider'), xpS = $('#xpSlider');
function econSync(){
  S.inflation = +inflS.value; S.drop = +dropS.value; S.xp = +xpS.value;
  $('#inflVal').textContent = S.inflation.toFixed(1) + '%';
  $('#dropVal').textContent = S.drop.toFixed(1) + '×';
  $('#xpVal').textContent = S.xp.toFixed(1) + '×';
  $('#priceIdx').textContent = (100 * (1 + S.inflation/100) * (1 + (S.drop-1)*.12)).toFixed(1);
  renderWorldBars(); renderMoneyBars();
}
[inflS, dropS, xpS].forEach(s => s.addEventListener('input', econSync));
['#tglPlatBonus','#tglSink'].forEach(id => $(id).addEventListener('change', renderMoneyBars));
$('#tglWhales').addEventListener('change', e => {
  logAccess(`Whale detection heuristics ${e.target.checked?'ENABLED':'DISABLED'}`);
  feedEvent(`<b>SEC</b> · whale heuristics switched <b>${e.target.checked?'ON':'OFF'}</b>.`);
});

/* servers */
function renderServers(){
  $('#srvGrid').innerHTML = S.nodes.map((n,i) => `
    <div class="node ${n.state}" data-i="${i}">
      <h4><i class="dot ${n.state==='online'?'g':n.state==='degraded'?'o':'r'}"></i>${n.name}</h4>
      <div class="nmeta"><span>PING</span><b>${n.ping} ms</b></div>
      <div class="nmeta"><span>LOAD</span><b>${n.load}%</b></div>
      <div class="nbar"><i style="width:${n.load}%"></i></div>
      <div class="nmeta"><span>IN-NODE</span><b>${fmt(n.players)}</b></div>
      <button class="mini-btn" data-act="restart" data-i="${i}">RESTART</button>
      <button class="mini-btn warn" data-act="drain" data-i="${i}">${n.state==='offline'?'RESTORE':'DRAIN'}</button>
    </div>`).join('');
}
$('#srvGrid').addEventListener('click', e => {
  const b = e.target.closest('.mini-btn'); if(!b) return;
  const n = S.nodes[+b.dataset.i];
  if(b.dataset.act === 'restart') rebootNode(n);
  else {
    n.state = n.state === 'offline' ? 'online' : 'offline';
    logAccess(`Node ${n.name} ${n.state==='offline'?'DRAINED':'RESTORED'}`);
    feedEvent(`<b>OPS</b> · ${n.name} manually ${n.state==='offline'?'drained':'restored'}.`);
    renderServers();
  }
});
function rebootNode(n){
  n.state = 'booting'; renderServers();
  const el = $(`.node[data-i="${S.nodes.indexOf(n)}"]`);
  if(el){ el.className = 'node booting'; el.querySelector('h4').innerHTML = '<i class="dot c"></i>' + n.name; }
  logAccess(`Cold restart issued → ${n.name}`);
  setTimeout(() => { n.state='online'; n.load = 20+Math.random()*25|0; n.ping = 8+Math.random()*20|0;
    renderServers(); feedEvent(`<b>OPS</b> · ${n.name} back online · cold boot 3.4s`); }, 2400);
}
$('#restartAll').addEventListener('click', () => {
  S.nodes.forEach(n => { n.state='booting'; });
  renderServers(); logAccess('FLEET-WIDE RESTART ISSUED');
  feedEvent('<b>OPS</b> · fleet-wide restart cascading across 12 nodes…');
  setTimeout(() => { S.nodes.forEach(n=>{n.state='online';n.load=20+Math.random()*30|0;}); renderServers();
    feedEvent('<b>OPS</b> · all nodes nominal.'); }, 3200);
});
$('#maintenanceBtn').addEventListener('click', () => {
  S.maintenance = !S.maintenance;
  const st = $('#srvStatus');
  st.textContent = `STATUS: ${S.maintenance?'MAINTENANCE WINDOW OPEN':'NOMINAL'} · MAINTENANCE FLAG: ${S.maintenance?'ON':'OFF'}`;
  st.classList.toggle('maint', S.maintenance);
  logAccess(`Maintenance flag ${S.maintenance?'SET':'CLEARED'}`);
  toast(S.maintenance?'SERVERS':'SERVERS', S.maintenance?'Global maintenance ON — queues held at lobby.':'Back to live operations.', S.maintenance?'gold':'');
});
setInterval(() => {
  if(consoleEl.hidden) return;
  S.nodes.forEach(n => { if(n.state==='online'){ n.load=Math.min(97,Math.max(12,n.load+(Math.random()-.5)*10|0));
    n.ping=Math.max(6,n.ping+(Math.random()-.5)*6|0); n.players+= (Math.random()-.5)*3000|0; }});
  if($('.view.active')?.id === 'view-servers') renderServers();
}, 3000);

/* census table */
function renderUsers(){
  const q = ($('#userSearch').value||'').toLowerCase();
  const sort = $('#userSort').value;
  const rows = S.users
    .filter(u => !q || (u.handle+u.world+u.rank+u.uid).toLowerCase().includes(q))
    .sort((a,b) => b[sort] - a[sort]);
  $('#userRows').innerHTML = rows.map(u => `
    <tr>
      <td>${u.uid}</td><td><b>${u.handle}</b></td><td>${u.world}</td><td>${u.rank}</td>
      <td>${u.level}</td><td>$${u.spend.toFixed(2)}</td>
      <td><span class="badge ${u.status}">${u.status.toUpperCase()}</span></td>
      <td><button class="mini-btn" data-uid="${u.uid}" data-act="inspect">INSPECT</button>
          <button class="mini-btn warn" data-uid="${u.uid}" data-act="ban">${u.status==='banned'?'UNBAN':'BAN'}</button></td>
    </tr>`).join('');
}
$('#userRows').addEventListener('click', e => {
  const b = e.target.closest('.mini-btn'); if(!b) return;
  const u = S.users.find(x => x.uid === b.dataset.uid); if(!u) return;
  if(b.dataset.act === 'inspect'){
    feedEvent(`<b>CENSUS</b> · inspected <b>${u.handle}</b> — LVL ${u.level}, ${u.world}, spend $${u.spend}/24h.`);
    toast('PLAYER FILE', `${u.handle} · ${u.rank} · lifetime vector rating ${(u.score/100).toFixed(1)}σ`, 'gold');
  } else {
    u.status = u.status === 'banned' ? 'online' : 'banned';
    logAccess(`${u.handle} ${u.status==='banned'?'BANNED':'UNBANNED'} by owner`);
    renderUsers();
  }
});
$('#userSearch').addEventListener('input', renderUsers);
$('#userSort').addEventListener('change', renderUsers);

/* access log + destructive ops */
function logAccess(msg){
  S.logs.unshift({ t: new Date().toTimeString().slice(0,8), m: msg });
  S.logs = S.logs.slice(0, 30);
  const ul = $('#accessLog'); if(!ul) return;
  ul.innerHTML = S.logs.map(l => `<li><b>${l.t}</b> ${l.m}</li>`).join('');
}
logAccess('Gateway armed · awaiting credentials');

function holdConfirm(btn, done){
  let timer;
  const start = e => { e.preventDefault();
    $('#dangerConfirm').hidden = false; $('#dangerConfirm').textContent = 'KEEP HOLDING…';
    timer = setTimeout(() => { $('#dangerConfirm').hidden = true; done(); }, 1200); };
  const cancel = () => { clearTimeout(timer); $('#dangerConfirm').hidden = true; };
  btn.addEventListener('mousedown', start); btn.addEventListener('touchstart', start, {passive:false});
  ['mouseup','mouseleave','touchend'].forEach(ev => btn.addEventListener(ev, cancel));
}
holdConfirm($('#wipeBtn'), () => {
  Wallet.state = { gold:0, diamond:0, plat:0 }; Wallet.save(); Wallet.render();
  S.vol = 12000; logAccess('TEST LEDGERS PURGED · wallet re-zeroed');
  toast('LEDGER PURGE','Session wallets and test ledgers wiped. Economy volume reset baseline.','err');
});
holdConfirm($('#resetEconBtn'), () => {
  inflS.value = 0; dropS.value = 1; xpS.value = 1; econSync();
  $('#tglSink').checked = false; renderMoneyBars();
  logAccess('ECONOMY RESET TO FACTORY PARAMETERS');
  toast('ECONOMY','Inflation engine returned to factory defaults (0% drift · 1.0× rates).','gold');
});
