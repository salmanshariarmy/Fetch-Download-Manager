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
  status($("capture").checked ? "Automatic capture enabled. Test a small file first." : "Automatic capture disabled.");
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
