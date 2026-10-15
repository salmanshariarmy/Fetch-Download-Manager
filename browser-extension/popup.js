"use strict";
const $ = id => document.getElementById(id);
function status(message, isError=false) {
  $("status").textContent = message;
  $("status").className = isError ? "error" : "";
}
chrome.storage.local.get({captureDownloads:false}, settings => {
  $("capture").checked = !!settings.captureDownloads;
});
$("capture").addEventListener("change", () => {
  chrome.storage.local.set({captureDownloads:$("capture").checked});
  status($("capture").checked ? "Automatic mirroring enabled (browser copy remains)." : "Automatic mirroring disabled.");
});
$("send").addEventListener("click", () => {
  const url = $("url").value.trim();
  if (!/^https?:\/\//i.test(url)) return status("Enter a direct HTTP or HTTPS link.",true);
  $("send").disabled=true;
  chrome.runtime.sendMessage({type:"download",url}, response => {
    $("send").disabled=false;
    if(chrome.runtime.lastError) status(chrome.runtime.lastError.message,true);
    else status(response?.message || "No response from native host.", !response?.ok);
  });
});
$("url").addEventListener("keydown",e=>{if(e.key==="Enter")$("send").click();});

chrome.storage.session.get({lastResult:null}, ({lastResult}) => {
  if(lastResult?.message) status(lastResult.message, !lastResult.ok);
});

chrome.storage.local.get({videoButtons:false}, settings => {
  $("videoButtons").checked = !!settings.videoButtons;
});
$("videoButtons").addEventListener("change", () => {
  const enabled = $("videoButtons").checked;
  chrome.storage.local.set({videoButtons:enabled});
  status(enabled ? "Direct HTML5 media buttons enabled. Reload your tab if needed." : "Media buttons disabled.");
});

// Popup user actions have a temporary activeTab grant. No background page scan.
function activeTab() {
  return new Promise(resolve => chrome.tabs.query({active:true,currentWindow:true},
    tabs => resolve(tabs?.[0] || null)));
}
function validPage(url) {
  try { const u = new URL(url); return ["http:","https:"].includes(u.protocol) && !u.username && !u.password; }
  catch (_) { return false; }
}
$("pageMedia").addEventListener("click", async () => {
  const tab = await activeTab();
  if (!validPage(tab?.url)) return status("Open an HTTP(S) media page first.", true);
  $("pageMedia").disabled = true;
  chrome.runtime.sendMessage({type:"download",url:tab.url,sourcePage:tab.url,media:true}, result => {
    $("pageMedia").disabled = false;
    status(result?.message || chrome.runtime.lastError?.message || "No response.", !result?.ok);
  });
});
$("findStreams").addEventListener("click", async () => {
  const tab = await activeTab();
  if (!validPage(tab?.url) || !Number.isInteger(tab?.id))
    return status("Open a normal website tab first.", true);
  chrome.tabs.sendMessage(tab.id, {type:"fetch:listMedia"}, response => {
    if (chrome.runtime.lastError) return status("Reload the page and try again.", true);
    const box = $("mediaList");
    box.replaceChildren();
    const entries = Array.isArray(response?.items) ? response.items.slice(0,25) : [];
    if (!entries.length) return status("No directly exposed media/playlist URLs found.");
    status(`Found ${entries.length} exposed media link(s).`);
    for (const entry of entries) {
      if (!validPage(entry.url)) continue;
      const button = document.createElement("button");
      button.type = "button";
      const parsed = new URL(entry.url);
      button.textContent = `${entry.label || "Media"}: ${parsed.hostname}${parsed.pathname.slice(-55)}`;
      button.title = "Send this link to Fetch";
      button.addEventListener("click", () => {
        button.disabled = true;
        chrome.runtime.sendMessage({type:"download",url:entry.url,
          sourcePage:tab.url,media:entry.media === true}, result => {
          button.disabled = false;
          status(result?.message || chrome.runtime.lastError?.message || "No response.", !result?.ok);
        });
      });
      box.appendChild(button);
    }
  });
});
