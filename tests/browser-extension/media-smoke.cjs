"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const code = fs.readFileSync(path.join(__dirname,"../../browser-extension/media-utils.js"),"utf8") +
  "\n;globalThis.testingUtils = FetchMediaUtils;";
const context = {URL};
vm.runInNewContext(code, context);
const pick = context.testingUtils.directMediaUrl;
function node(src, sources = []) {
  return {currentSrc:src, src, querySelectorAll(){return sources.map(s=>({src:s}));}};
}
assert.equal(pick(node("https://cdn.example.org/video.mp4"), "https://example.org"),
  "https://cdn.example.org/video.mp4");
assert.equal(pick(node("blob:https://example.org/id"), "https://example.org"), null);
assert.equal(pick(node("data:video/mp4;base64,AQ=="), "https://example.org"), null);
assert.equal(pick(node("javascript:alert(1)"), "https://example.org"), null);
assert.equal(pick(node("" ,["/media/clip.webm"]), "https://example.org/page"),
  "https://example.org/media/clip.webm");
assert.equal(pick(node("https://user:password@host.example/video"), "https://example.org"), null);
assert.equal(pick(null, "https://example.org"), null);
const manifest = JSON.parse(fs.readFileSync(path.join(__dirname,"../../browser-extension/manifest.json")));
assert.ok(manifest.content_scripts?.some(r=>
  r.js?.includes('media-utils.js') && r.js?.includes('content.js') &&
  r.matches?.includes('https://*/*')));
console.log("PASS: HTML5 media direct-link extraction, privacy-safe protocol filtering, content script manifest");
// Simulated DOM integration: hover on a direct HTML5 video -> user clicks.
const events = {}, pageWindow = {innerWidth:1200, innerHeight:800};
pageWindow.top = pageWindow;
pageWindow.addEventListener = (name, callback) => {events[`window:${name}`]=callback;};
let sendCount = 0, lastUrl = "", button;
const documentMock = {
  baseURI: "https://example.org/page",
  documentElement:{appendChild(el){button=el;}},
  contains(){return true;},
  createElement(){return {
    style:{}, hidden:false, setAttribute(){}, addEventListener(name,fn){this[name]=fn;}
  };},
  addEventListener(name,fn){events[`document:${name}`]=fn;}
};
const media = {
  ...node("https://cdn.example.org/play.mp4"),
  closest(){return this;},
  getBoundingClientRect(){return {top:200,left:100,right:660,bottom:570,width:560,height:370};}
};
const companion = {
  runtime:{lastError:null, sendMessage(message,callback){
    sendCount++; lastUrl=message.url; callback({ok:true,message:"Accepted"});
  }},
  storage:{local:{get(_opts, callback){callback({videoButtons:true});}},
    onChanged:{addListener(fn){events["settings"]=fn;}}}
};
vm.runInNewContext(fs.readFileSync(path.join(__dirname,"../../browser-extension/media-utils.js"),"utf8") +
  '\n' + fs.readFileSync(path.join(__dirname,"../../browser-extension/content.js"),"utf8"),
  {window:pageWindow,document:documentMock,chrome:companion,URL});
events["document:pointerover"]({target:media});
assert.equal(button.style.display,"block");
button.click();
assert.equal(sendCount,1);
assert.equal(lastUrl,"https://cdn.example.org/play.mp4");
assert.equal(button.textContent,"Queued in Fetch ✓");
events.settings({videoButtons:{newValue:false}},"local");
assert.equal(button.style.display,"none");
console.log("PASS: opt-in hover media button forwards direct video and can be disabled");
