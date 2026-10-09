// DOM interaction checks for the design prototype; no browser/pixel-render assertion.
const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const {parseHTML} = require('linkedom');
const path = process.argv[2];
if (!path) throw new Error('Pass the design-prototype HTML path as the first argument. The historical prototype is not included in this repository.');
const source = fs.readFileSync(path, 'utf8');
assert(source.length < 1000000);
assert(!/<!doctype|<html[ >]|<head[ >]|<body[ >]/i.test(source));
assert(!/\bfetch\s*\(|XMLHttpRequest|WebSocket/.test(source));
const {window, document} = parseHTML(source);
let focused = null;
Object.defineProperty(document, 'activeElement', {get:()=>focused});
window.HTMLElement.prototype.focus = function(){focused=this;};
window.HTMLInputElement.prototype.setSelectionRange = function(a,b){this.selectionStart=a;this.selectionEnd=b;};
// Linkedom does not implement native select's value setter.
Object.defineProperty(window.HTMLSelectElement.prototype, 'value', {
  configurable:true,
  get(){const o=this.querySelector('option[selected]')||this.querySelector('option');return o?(o.getAttribute('value')??o.textContent):'';},
  set(value){this.querySelectorAll('option').forEach(o=>{if((o.getAttribute('value')??o.textContent)===value)o.setAttribute('selected','');else o.removeAttribute('selected');});}
});
const saved=[];let tweaks;
window.openai={setWidgetState:s=>{saved.push(s);assert(Buffer.byteLength(JSON.stringify(s))<16384);return Promise.resolve();}};
class Tweak{constructor(config){tweaks={config};}addSlider(object,key){tweaks.object=object;}addToggle(){} }
const intervals=new Map();let timer=0;
vm.runInNewContext(document.querySelector('script').textContent,{
  document,window,Tweak,lucide:{createIcons:()=>{}},
  setTimeout:()=>++timer,clearTimeout:()=>{},
  setInterval:f=>{intervals.set(++timer,f);return timer;},clearInterval:id=>intervals.delete(id)
},{filename:'wallpaper-ui-preview.html'});
const root=document.getElementById('wallpaper-design-preview');
const g=root.querySelector('[data-layout="gallery"]');
const m=root.querySelector('[data-layout="immersive"]');
function q(s,scope=g){const e=scope.querySelector(s);assert(e,'Missing '+s);return e;}
function click(selector,scope=g){q(selector,scope).dispatchEvent(new window.Event('click',{bubbles:true}));}
function change(selector,value,scope=g){const e=q(selector,scope);if(typeof value==='boolean')e.checked=value;else e.value=value;e.dispatchEvent(new window.Event('change',{bubbles:true}));}
function input(selector,value){const e=q(selector);e.value=value;e.selectionStart=value.length;e.dispatchEvent(new window.Event('input',{bubbles:true}));}
function state(k='gallery'){return saved.at(-1).privateContent.sessions[k];}
function nav(page,scope=g){click('[data-action="nav"][data-page="'+page+'"]',scope);}
function tick(){for(const [id,fn] of [...intervals])if(intervals.has(id))fn();}
let count=0;const check=(name,fn)=>{fn();count++;console.log('PASS '+name);};
check('initial render, isolated variants and no initial save',()=>{
  assert.equal(root.querySelectorAll(':scope > section[data-variant]').length,2);
  assert(root.querySelector('[data-variant="沉浸预览"]').hasAttribute('hidden'));
  assert(g.querySelector('.sj-hero'));assert(m.querySelector('.sj-immersive'));
  assert.equal(saved.length,0);
});
check('discovery filter and search empty state',()=>{
  nav('discover');assert.equal(g.querySelectorAll('.sj-tile').length,4);
  click('[data-action="filter"][data-value="抽象"]');assert.equal(g.querySelectorAll('.sj-tile').length,1);
  input('[data-query="search"]','不存在');assert(g.querySelector('.sj-empty'));
  click('[data-action="clearFilters"]');assert.equal(g.querySelectorAll('.sj-tile').length,4);
});
check('detail does not replace today; modal dismiss',()=>{
  click('[data-action="detail"][data-id="3"]');assert(g.querySelector('[role="dialog"]'));
  assert.equal(state().todayId,0);assert.equal(state().selected,3);
  const e=new window.Event('keydown',{bubbles:true});e.key='Escape';q('[data-action="close"]').dispatchEvent(e);
  assert(!g.querySelector('[role="dialog"]'));nav('today');assert(g.querySelector('.sj-hero h3').textContent.includes('沙丘与晨光'));
});
check('favorite updates library and independent session',()=>{
  click('[data-action="favorite"][data-id="0"]');nav('library');assert.equal(g.querySelectorAll('.sj-tile').length,3);
  assert.equal(state('immersive').favorites.length,2);
});
check('download cancellation, restart, completion',()=>{
  click('[data-action="download"][data-id="0"]');tick();assert.equal(q('progress').getAttribute('value'),'25');
  click('[data-action="cancelDownload"]');assert.equal(intervals.size,0);assert(!g.querySelector('progress'));
  click('[data-action="download"][data-id="0"]');for(let i=0;i<4;i++)tick();
  assert.equal(intervals.size,0);assert(state().downloaded.includes(0));
  click('[data-action="libraryTab"][data-value="已下载"]');assert.equal(g.querySelectorAll('.sj-tile').length,1);
});
check('source validation, escaping and enable filtering',()=>{
  nav('sources');click('[data-action="addSource"]');q('[data-form="name"]').value='';click('[data-action="commitSource"]');assert(q('[role="alert"]').textContent.includes('请输入'));
  q('[data-form="name"]').value='新源';q('[data-form="type"]').value='direct';q('[data-form="address"]').value='http://example.com/a.jpg';click('[data-action="commitSource"]');assert(q('[role="alert"]').textContent.includes('HTTPS'));
  q('[data-form="name"]').value='Bing 每日图';q('[data-form="address"]').value='https://example.com/a.jpg';click('[data-action="commitSource"]');assert(q('[role="alert"]').textContent.includes('同名'));
  q('[data-form="name"]').value='<img src=x onerror=bad()>风景';click('[data-action="commitSource"]');assert.equal(g.querySelectorAll('.sj-source-row').length,4);assert(!g.querySelector('.sj-source-copy img'));
  change('[data-source-toggle="2"]',false);nav('discover');assert.equal(g.querySelectorAll('.sj-tile').length,2);
});
check('theme, global position and cross-screen target',()=>{
  nav('settings');change('[data-select="theme"]','深色');assert.equal(g.style.colorScheme,'dark');
  change('[data-select="target"]','主显示器');change('[data-select="fit"]','跨屏');
  nav('today');click('[data-action="apply"][data-id="0"]');assert.equal(state().target,'所有显示器');
  assert(q('.sj-toast').textContent.includes('模拟应用'));
});
check('offline missing original blocked; cached/local usable',()=>{
  tweaks.object.offline=true;tweaks.config.onChange();assert(g.querySelector('.sj-offline'));
  click('[data-action="next"]');click('[data-action="download"][data-id="1"]');assert(!g.querySelector('progress'));assert(q('.sj-toast').textContent.includes('联网'));
  click('[data-action="apply"][data-id="1"]');assert(q('.sj-toast').textContent.includes('尚未下载'));
  nav('library');click('[data-action="import"]');click('[data-action="commitImport"]');assert(state().local.includes(3));
  click('[data-action="detail"][data-id="3"]');click('[data-action="apply"][data-id="3"]');assert(q('.sj-toast').textContent.includes('模拟应用'));
});
check('host state restore does not save recursively',()=>{
  const n=saved.length;const e=new window.CustomEvent('openai:set_globals',{detail:{globals:{widgetState:saved.at(-1)}}});window.dispatchEvent(e);assert.equal(saved.length,n);
  assert.equal(state().theme,'深色');assert.equal(state('immersive').theme,'跟随系统');
});
check('all sample image origins are allowed CDN',()=>{
  root.querySelectorAll('img').forEach(e=>assert.equal(new URL(e.src).origin,'https://cdn.jsdelivr.net'));
});
console.log(JSON.stringify({checks:count,widgetStateSnapshots:saved.length,validation:'DOM logic only; no pixel rendering or live OS/API calls'}));
