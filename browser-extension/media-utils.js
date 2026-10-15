"use strict";
// Shared read-only helpers for media elements. Nothing is uploaded to a remote service.
const FetchMediaUtils = (() => {
  function directMediaUrl(media, pageUrl) {
    if (!media) return null;
    const candidates = [media.currentSrc, media.src];
    if (typeof media.querySelectorAll === "function") {
      for (const source of media.querySelectorAll("source[src]")) {
        candidates.push(source.src || source.getAttribute("src"));
      }
    }
    for (const candidate of candidates) {
      if (typeof candidate !== "string" || !candidate.trim()) continue;
      try {
        const url = new URL(candidate, pageUrl);
        if ((url.protocol === "http:" || url.protocol === "https:") &&
            !url.username && !url.password) return url.href;
      } catch (_) { /* ignore invalid URL or malformed source */ }
    }
    return null; // blob:, data:, and DRM/player-internal streams are unsupported.
  }
  return Object.freeze({ directMediaUrl });
})();
