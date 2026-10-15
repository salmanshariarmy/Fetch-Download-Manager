"use strict";
// On-demand DOM and Resource Timing detection, not webRequest interception.
// No cookies, request headers, or page contents are copied to Fetch.
const FetchMediaUtils = (() => {
  const streamPath = /\.(m3u8|mpd)(?:$|[?#])/i;
  function safeUrl(candidate, pageUrl) {
    if (typeof candidate !== "string" || !candidate.trim()) return null;
    try {
      const url = new URL(candidate, pageUrl);
      if (url.href.length > 4096 || (url.protocol !== "http:" && url.protocol !== "https:") ||
          url.username || url.password) return null;
      return url.href;
    } catch (_) { return null; }
  }
  function directMediaUrl(media, pageUrl) {
    if (!media) return null;
    const candidates = [media.currentSrc, media.src];
    if (typeof media.querySelectorAll === "function") {
      for (const source of media.querySelectorAll("source[src]")) {
        candidates.push(source.src || source.getAttribute("src"));
      }
    }
    for (const value of candidates) {
      const safe = safeUrl(value, pageUrl);
      if (safe) return safe;
    }
    return null;
  }
  function discoverMedia(doc, perf, pageUrl) {
    const found = new Map();
    function add(value, label, streamsOnly = false) {
      const url = safeUrl(value, pageUrl);
      if (!url || (streamsOnly && !streamPath.test(url))) return;
      if (found.has(url) || found.size >= 25) return;
      found.set(url, {url, label, media:streamPath.test(url)});
    }
    // Read only video/audio source attributes and manifest links.
    for (const element of doc.querySelectorAll("video, audio")) {
      const url = directMediaUrl(element, pageUrl);
      if (url) add(url, "HTML5 media");
    }
    for (const element of doc.querySelectorAll("a[href], link[href]")) {
      add(element.href || element.getAttribute("href"), "Stream link", true);
    }
    // Resource Timing exposes already-loaded URL *names*, not response bodies,
    // authorization headers, cookies, or encrypted media data.
    if (perf && typeof perf.getEntriesByType === "function") {
      for (const item of perf.getEntriesByType("resource")) {
        add(item.name, "Observed manifest", true);
      }
    }
    return [...found.values()];
  }
  return Object.freeze({ directMediaUrl, discoverMedia, safeUrl });
})();
