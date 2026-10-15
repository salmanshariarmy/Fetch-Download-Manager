"use strict";
const HOST = "com.fetch.download_manager";
const MENU_LINK = "download-with-fetch";
const MENU_PAGE = "download-page-media-with-fetch";
const validUrl = (url) => {
  try {
    const u = new URL(url);
    return (u.protocol === "https:" || u.protocol === "http:") &&
      !u.username && !u.password && url.length <= 4096;
  } catch (_) { return false; }
};
const sourceOrigin = (pageUrl) => {
  if (!validUrl(pageUrl)) return undefined;
  const u = new URL(pageUrl);
  return u.origin + "/"; // Never transmit a private page path, query, or fragment.
};

// Chromium native messaging requires the installed host to be registered with
// this exact extension ID. Neither cookies nor Authorization headers are read.
function sendToFetch(url, options = {}) {
  if (!validUrl(url)) return Promise.resolve({ok:false, message:"Only HTTP(S) URLs without embedded credentials are supported."});
  return new Promise(resolve => {
    chrome.runtime.sendNativeMessage(HOST, {
      type:"download", url, media:options.media === true,
      sourcePage:sourceOrigin(options.sourcePage)
    }, reply => {
      if (chrome.runtime.lastError) {
        resolve({ok:false, message:`Fetch native host unavailable: ${chrome.runtime.lastError.message}. Check the extension ID registration.`});
      } else resolve(reply && typeof reply.ok === "boolean" ? reply : {ok:false, message:"Invalid reply from Fetch."});
    });
  });
}

chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.removeAll(() => {
    chrome.contextMenus.create({id:MENU_LINK, title:"Download link with Fetch", contexts:["link", "video", "audio"]});
    chrome.contextMenus.create({id:MENU_PAGE, title:"Download page media with Fetch (yt-dlp)", contexts:["page"]});
  });
});
chrome.contextMenus.onClicked.addListener(async info => {
  const url = info.menuItemId === MENU_PAGE ? info.pageUrl : info.linkUrl || info.srcUrl;
  if (info.menuItemId !== MENU_PAGE && info.menuItemId !== MENU_LINK) return;
  const result = await sendToFetch(url || "", {
    media:info.menuItemId === MENU_PAGE,
    sourcePage:info.pageUrl
  });
  chrome.storage.session.set({lastResult:result});
});
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message?.type === "download" && typeof message.url === "string") {
    sendToFetch(message.url, {
      media:message.media === true,
      sourcePage:sender?.tab?.url || message.sourcePage
    }).then(sendResponse);
    return true;
  }
  return false;
});

// Opt-in auto MIRROR. Never cancel the browser's download: the named-pipe
// acknowledgment only proves that Fetch queued a job, not that bytes arrived.
// Mirroring creates duplicates by design; users can instead use context menu.
chrome.downloads.onCreated.addListener(async item => {
  const settings = await chrome.storage.local.get({captureDownloads:false});
  const url = item.finalUrl || item.url;
  if (!settings.captureDownloads || !validUrl(url) ||
      (chrome.runtime.id && item.byExtensionId === chrome.runtime.id)) return;
  const mime = (item.mime || "").toLowerCase();
  if (/^(text\/html|application\/(json|xhtml\+xml))/.test(mime)) return;
  const pathname = new URL(url).pathname.toLowerCase();
  if (/\.(html?|xhtml|json)$/.test(pathname)) return;
  const result = await sendToFetch(url, {sourcePage:item.referrer});
  chrome.storage.session.set({lastResult:result});
});
