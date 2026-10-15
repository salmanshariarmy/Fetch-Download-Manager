"use strict";
// Opt-in direct HTML5 video/audio source button. Does not intercept requests,
// read cookies, bypass DRM, or scan site-wide network traffic.
(() => {
  if (window.top !== window) return;
  let enabled = false;
  let current = null;
  let button = null;

  function position() {
    if (!button || !current || !document.contains(current) || !enabled) {
      if (button) button.style.display = "none";
      return;
    }
    const rect = current.getBoundingClientRect();
    if (rect.width < 140 || rect.height < 85 || rect.bottom < 0 ||
        rect.top > window.innerHeight || rect.right < 0 || rect.left > window.innerWidth) {
      button.style.display = "none";
      return;
    }
    button.style.display = "block";
    button.style.top = `${Math.max(6, rect.top + 8)}px`;
    button.style.left = `${Math.max(6, Math.min(window.innerWidth - 168, rect.right - 164))}px`;
  }

  function initButton() {
    if (button) return;
    button = document.createElement("button");
    button.type = "button";
    button.textContent = "↓ Download with Fetch";
    button.setAttribute("aria-label", "Download direct media source with Fetch");
    button.style.cssText = [
      "all:initial", "position:fixed", "z-index:2147483647", "box-sizing:border-box",
      "background:#10253a", "color:#e4fcf8", "border:1px solid #33c4bd",
      "border-radius:7px", "font:600 12px Arial,sans-serif", "padding:9px 11px",
      "cursor:pointer", "box-shadow:0 4px 16px #0008", "max-width:166px"
    ].join(";");
    button.style.display = "none";
    button.addEventListener("click", () => {
      const url = FetchMediaUtils.directMediaUrl(current, document.baseURI);
      if (!url) {
        button.textContent = "No direct source";
        return;
      }
      button.disabled = true;
      button.textContent = "Sending to Fetch…";
      chrome.runtime.sendMessage({type:"download",url}, (reply) => {
        const error = chrome.runtime.lastError?.message;
        button.textContent = error ? "Fetch not connected" :
          reply?.ok ? "Queued in Fetch ✓" : "Not queued — open Fetch";
        button.title = error || reply?.message || "";
        button.disabled = false;
      });
    });
    document.documentElement.appendChild(button);
  }

  document.addEventListener("pointerover", (event) => {
    if (!enabled) return;
    const media = event.target?.closest?.("video,audio");
    if (!media) return;
    const url = FetchMediaUtils.directMediaUrl(media, document.baseURI);
    if (!url) return;
    current = media;
    initButton();
    button.textContent = "↓ Download with Fetch";
    button.title = "Direct media source only; protected/blob sources not supported";
    position();
  }, true);
  document.addEventListener("pointerdown", (event) => {
    if (button && event.target !== button && !event.target?.closest?.("video,audio")) {
      current = null;
      button.style.display = "none";
    }
  }, true);
  window.addEventListener("scroll", position, true);
  window.addEventListener("resize", position);
  chrome.storage.local.get({videoButtons:false}, (settings) => {
    enabled = !!settings.videoButtons;
  });
  chrome.storage.onChanged.addListener((changes, area) => {
    if (area !== "local" || !changes.videoButtons) return;
    enabled = !!changes.videoButtons.newValue;
    if (!enabled && button) button.style.display = "none";
  });
})();
