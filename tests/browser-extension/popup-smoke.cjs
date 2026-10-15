"use strict";
const assert = require("node:assert/strict");
const vm = require("node:vm");
const fs = require("node:fs");
const path = require("node:path");
const controls = new Map();
function element(id) {
  if (!controls.has(id)) controls.set(id, {
    id, value:"", checked:false, disabled:false, className:"", textContent:"",
    children:[], events:{}, addEventListener(name,fn){this.events[name]=fn;},
    appendChild(child){this.children.push(child);}, replaceChildren(){this.children=[];},
    async click(){return this.events.click?.();}
  });
  return controls.get(id);
}
const sent = [];
let requests = 0;
const chrome = {
  storage: {
    local:{get(_options,cb){cb({captureDownloads:false,videoButtons:false});},set(){}},
    session:{get(_options,cb){cb({lastResult:null});}}
  },
  runtime:{lastError:null,sendMessage(message,callback){sent.push(message);callback({ok:true,message:"Queued"});}},
  tabs:{query(_query,callback){callback([{id:7,url:"https://example.org/watch?v=123"}]);},
    sendMessage(id,message,callback){
      assert.equal(id,7);assert.equal(message.type,"fetch:listMedia");requests++;
      callback({items:[{url:"https://cdn.example.org/stream.m3u8",label:"Playlist",media:true}]});
    }}
};
const document = {getElementById:element,createElement(){return {
  events:{}, addEventListener(event,cb){this.events[event]=cb;},
  click(){this.events.click();}, disabled:false
};}};
const script=fs.readFileSync(path.join(__dirname,"../../browser-extension/popup.js"),"utf8");
vm.runInNewContext(script,{chrome,document,URL,console});
(async()=>{
  await element("pageMedia").click();
  await new Promise(resolve=>setImmediate(resolve));
  assert.equal(sent.length,1);
  assert.equal(sent[0].media,true);
  assert.equal(sent[0].url,"https://example.org/watch?v=123");
  await element("findStreams").click();
  await new Promise(resolve=>setImmediate(resolve));
  assert.equal(requests,1);
  assert.equal(element("mediaList").children.length,1);
  element("mediaList").children[0].click();
  assert.equal(sent.length,2);
  assert.equal(sent[1].url,"https://cdn.example.org/stream.m3u8");
  assert.equal(sent[1].media,true);
  console.log("PASS: popup sends explicit current-page media and selected discovered streams");
})().catch(error=>{console.error(error);process.exitCode=1;});
