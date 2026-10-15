"use strict";
const HOST = "com.fetch.download_manager";
const MENU_ID = "download-with-fetch";
const validUrl = (url) => {
  try { const u = new URL(url); return (u.protocol === "https:" || u.protocol === "http:") && !u.username && !u.password; }
  catch (_) { return false; }
};

// Chromium native messaging requires the installed native host to be registered
// with this unpacked extension's exact ID (see Register-FetchBrowser.ps1).
function sendToFetch(url) {
  if (!validUrl(url)) return Promise.resolve({ok:false, message:"Only direct HTTP(S) download links are supported."});
  return new Promise((resolve) => {
    chrome.runtime.sendNativeMessage(HOST, {type: "download", url}, (reply) => {
      if (chrome.runtime.lastError) {
        resolve({ok:false, message:`Fetch native host unavailable: ${chrome.runtime.lastError.message}. Check the extension ID registration.`});
      } else resolve(reply && typeof reply.ok === "boolean" ? reply : {ok:false, message:"Invalid reply from Fetch."});
    });
  });
}

chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.removeAll(() => {
    chrome.contextMenus.create({id:MENU_ID, title:"Download with Fetch", contexts:["link", "video", "audio"]});
  });
});
chrome.contextMenus.onClicked.addListener(async (info) => {
  if (info.menuItemId !== MENU_ID) return;
  const result = await sendToFetch(info.linkUrl || info.srcUrl || "");
  chrome.storage.session.set({lastResult:result});
});
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message?.type === "download" && typeof message.url === "string") {
    sendToFetch(message.url).then(sendResponse);
    return true;
  }
  return false;
});

// OFF by default. Enable only in the extension popup after testing manual
// forwarding. Cancel Chrome's download ONLY AFTER Fetch confirms accepting it.
chrome.downloads.onCreated.addListener(async (item) => {
  const settings = await chrome.storage.local.get({captureDownloads:false});
  const url = item.finalUrl || item.url;
  // Only intercept actual downloadable resources, not HTML/JSON pages or links
  // the user intended the browser to handle. Do not intercept this extension's
  // own downloads (should one be created in a future version).
  if (!settings.captureDownloads || !validUrl(url) || (chrome.runtime.id && item.byExtensionId === chrome.runtime.id)) return;
  const mime = (item.mime || "").toLowerCase();
  if (/^(text\/html|application\/(json|xhtml\+xml))/.test(mime)) return;
  const pathname = new URL(url).pathname.toLowerCase();
  if (/\.(html?|xhtml|json)$/.test(pathname)) return;
  const response = await sendToFetch(url);
  if (response.ok) {
    try { await chrome.downloads.cancel(item.id); }
    catch (_) { /* Browser may already have completed; never erase the user's file. */ }
  } else {
    chrome.storage.session.set({lastResult:response});
  }
});
