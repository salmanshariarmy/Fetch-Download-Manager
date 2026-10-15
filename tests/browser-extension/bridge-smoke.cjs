"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs");
const vm = require("node:vm");
const path = require("node:path");
let messageHandler, createdHandler, clickedHandler, installedHandler;
let capture = false;
const sent = [];
const canceled = [];
const menuIds = [];
const chrome = {
  runtime: {
    onInstalled: {addListener(fn){installedHandler=fn;}},
    onMessage: {addListener(fn){messageHandler=fn;}},
    lastError: null,
    sendNativeMessage(host, payload, callback) {
      sent.push({host,payload});
      callback({ok:true, message:"Queued in Fetch."});
    }
  },
  contextMenus: {
    removeAll(fn){fn();},
    create(options){menuIds.push(options.id);},
    onClicked: {addListener(fn){clickedHandler=fn;}}
  },
  downloads: {
    onCreated: {addListener(fn){createdHandler=fn;}},
    async cancel(id){canceled.push(id);}
  },
  storage: {
    local: {async get(){return {captureDownloads:capture};}},
    session: {set(){}}
  }
};
vm.runInNewContext(fs.readFileSync(path.join(__dirname,"../../browser-extension/background.js"),"utf8"),
  {chrome,URL,console});
async function send(message, sender={}) {
  return await new Promise(resolve => assert.equal(messageHandler(message,sender,resolve),true));
}
async function tick() { await new Promise(resolve=>setImmediate(resolve)); }
(async()=>{
  installedHandler();
  assert.ok(menuIds.includes("download-with-fetch"));
  assert.ok(menuIds.includes("download-page-media-with-fetch"));
  let response = await send({type:"download",url:"file:///etc/passwd"});
  assert.equal(response.ok,false);
  assert.equal(sent.length,0);
  response = await send({type:"download",url:"https://example.com/file.zip",
    sourcePage:"https://example.com/private?token=super-secret"});
  assert.equal(response.ok,true);
  assert.equal(sent[0].host,"com.fetch.download_manager");
  assert.equal(sent[0].payload.sourcePage,"https://example.com/");
  assert.ok(!JSON.stringify(sent).includes("super-secret"));
  await clickedHandler({menuItemId:"download-page-media-with-fetch",
    pageUrl:"https://www.youtube.com/watch?v=abc"});
  await tick();
  assert.equal(sent.at(-1).payload.media,true);
  assert.equal(sent.at(-1).payload.url,"https://www.youtube.com/watch?v=abc");
  // A page that embeds basic credentials must never be forwarded.
  await send({type:"download",url:"https://username:password@example.com/secret"});
  assert.equal(sent.length,2);
  await createdHandler({id:1,url:"https://example.com/a.zip"});
  assert.equal(sent.length,2,"off by default");
  capture = true;
  await createdHandler({id:2,url:"https://example.com/b.zip",
    referrer:"https://example.com/private?t=1"});
  assert.equal(sent.length,3,"opt-in mirroring forwards link");
  assert.equal(sent.at(-1).payload.sourcePage,"https://example.com/");
  assert.deepEqual(canceled,[],"browser copy must never be canceled merely because Fetch queued");
  await createdHandler({id:3,url:"https://example.com/watch",mime:"text/html"});
  await createdHandler({id:4,url:"https://example.com/index.html",mime:"application/octet-stream"});
  assert.equal(sent.length,3,"HTML/JSON not mirrored");
  console.log("PASS: safe browser forwarding, explicit page-media request, referrer sanitization and non-destructive mirroring");
})().catch(error=>{console.error(error);process.exitCode=1;});
