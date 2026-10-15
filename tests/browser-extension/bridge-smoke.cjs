"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs");
const vm = require("node:vm");
const path = require("node:path");
let messageHandler, createdHandler, clickedHandler, installedHandler;
let capture = false;
let sent = [];
let canceled = [];
let menuMade = false;
const chrome = {
  runtime: {
    onInstalled: {addListener(fn){installedHandler=fn;}},
    onMessage: {addListener(fn){messageHandler=fn;}},
    lastError: null,
    sendNativeMessage(host, payload, callback) {
      sent.push({host,payload});
      callback({ok: true, message: "Link queued in Fetch."});
    }
  },
  contextMenus: {
    removeAll(fn){fn();},
    create(opts){menuMade=opts.id==='download-with-fetch';},
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
const code = fs.readFileSync(path.join(__dirname,'../../browser-extension/background.js'),'utf8');
vm.runInNewContext(code, {chrome, URL, console});
async function send(url) {
  return await new Promise(resolve=> {
    assert.equal(messageHandler({type:'download',url},{},resolve),true);
  });
}
(async()=> {
  installedHandler();
  assert.equal(menuMade,true,'context menu registered');
  let r = await send('file:///etc/passwd');
  assert.equal(r.ok,false,'non-http URL is denied');
  assert.equal(sent.length,0,'no native host called');
  r = await send('https://example.com/file.zip');
  assert.equal(r.ok,true);
  assert.equal(sent[0].host,'com.fetch.download_manager');
  assert.equal(sent[0].payload.url,'https://example.com/file.zip');
  // Auto-capture disabled must not send or cancel anything.
  await createdHandler({id:1,url:'https://example.com/a.zip'});
  assert.equal(canceled.length,0);
  assert.equal(sent.length,1);
  capture = true;
  await createdHandler({id:2,url:'https://example.com/b.zip'});
  assert.deepEqual(canceled,[2]);
  console.log('PASS: browser extension context menu, URL filtering, native messaging, opt-in capture');
})().catch(error=>{console.error(error);process.exitCode=1;});
