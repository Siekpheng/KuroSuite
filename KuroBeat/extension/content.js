(() => {
  const WS_URL = "ws://127.0.0.1:8765/ws?role=extension";
  let ws = null;
  let reconnectTimer = null;
  let isConnected = false;
  let lastSentKey = "";

  // --- Inject Main-World Page Bridge ---
  function injectPageBridge() {
    try {
      const script = document.createElement("script");
      script.src = chrome.runtime.getURL("page-bridge.js");
      script.onload = () => script.remove();
      (document.head || document.documentElement).appendChild(script);
    } catch (_) {}
  }
  injectPageBridge();

  const pendingBridgeReqs = new Map();
  let bridgeReqSeq = 0;

  window.addEventListener("message", (event) => {
    if (event.source !== window) return;
    const msg = event.data;
    if (!msg || msg.source !== "ytm-remote-page" || !msg.reqId) return;
    const resolver = pendingBridgeReqs.get(msg.reqId);
    if (resolver) {
      pendingBridgeReqs.delete(msg.reqId);
      resolver(msg);
    }
  });

  function callPageBridge(type, payload = {}, timeoutMs = 1500) {
    return new Promise((resolve) => {
      const reqId = "b" + (++bridgeReqSeq);
      const timer = setTimeout(() => {
        pendingBridgeReqs.delete(reqId);
        resolve(null);
      }, timeoutMs);
      pendingBridgeReqs.set(reqId, (resp) => {
        clearTimeout(timer);
        resolve(resp);
      });
      window.postMessage(
        Object.assign({ source: "ytm-remote-ext", type, reqId }, payload),
        "*"
      );
    });
  }

  async function getPageConfig() {
    const resp = await callPageBridge("getConfig");
    if (resp && resp.config) return resp.config;
    return {
      apiKey: "",
      clientVersion: "1.20250101.01.00",
      hl: document.documentElement.lang || "en",
      gl: "US",
      sessionIndex: "0",
      delegatedSessionId: "",
      visitorData: "",
      videoId: ""
    };
  }

  function getCookie(name) {
    const m = document.cookie.match(new RegExp("(?:^|;\\s*)" + name + "=([^;]+)"));
    return m ? m[1] : "";
  }

  async function computeSapisidHash(origin = "https://music.youtube.com") {
    const sapisid = getCookie("SAPISID") || getCookie("__Secure-3PAPISID") || getCookie("__Secure-1PAPISID");
    if (!sapisid || !crypto || !crypto.subtle) return "";
    try {
      const ts = Math.floor(Date.now() / 1000);
      const input = `${ts} ${sapisid} ${origin}`;
      const data = new TextEncoder().encode(input);
      const hashBuf = await crypto.subtle.digest("SHA-1", data);
      const hashHex = Array.from(new Uint8Array(hashBuf))
        .map((b) => b.toString(16).padStart(2, "0"))
        .join("");
      return `SAPISIDHASH ${ts}_${hashHex}`;
    } catch (_) {
      return "";
    }
  }

  async function innertubePost(endpoint, payload) {
    const cfg = await getPageConfig();
    const auth = await computeSapisidHash("https://music.youtube.com");

    const headers = {
      "Content-Type": "application/json",
      "X-Origin": "https://music.youtube.com",
      "X-YouTube-Client-Name": "67",
      "X-YouTube-Client-Version": cfg.clientVersion || "1.20250101.01.00",
      "X-Goog-AuthUser": cfg.sessionIndex || "0"
    };
    if (auth) headers["Authorization"] = auth;
    if (cfg.visitorData) headers["X-Goog-Visitor-Id"] = cfg.visitorData;
    if (cfg.delegatedSessionId) headers["X-Goog-PageId"] = cfg.delegatedSessionId;

    const url =
      "https://music.youtube.com/youtubei/v1/" +
      endpoint +
      "?prettyPrint=false" +
      (cfg.apiKey ? "&key=" + encodeURIComponent(cfg.apiKey) : "");

    const body = Object.assign(
      {
        context: {
          client: {
            clientName: "WEB_REMIX",
            clientVersion: cfg.clientVersion || "1.20250101.01.00",
            hl: cfg.hl || "en",
            gl: cfg.gl || "US"
          }
        }
      },
      payload
    );

    const res = await fetch(url, {
      method: "POST",
      credentials: "include",
      headers,
      body: JSON.stringify(body)
    });
    if (!res.ok) throw new Error(endpoint + " HTTP " + res.status);
    return res.json();
  }

  // --- Playback State ---

  function getVideo() {
    return document.querySelector("video.html5-main-video") || document.querySelector("video");
  }

  function upgradeArtworkUrl(url, size = 544) {
    if (!url || typeof url !== "string") return "";
    return url.replace(/w\d+-h\d+/g, `w${size}-h${size}`).replace(/=s\d+/g, `=s${size}`);
  }

  function parseTimeString(str) {
    if (!str) return 0;
    const parts = str.trim().split(":").map(Number);
    if (parts.some(isNaN)) return 0;
    if (parts.length === 3) return parts[0] * 3600 + parts[1] * 60 + parts[2];
    if (parts.length === 2) return parts[0] * 60 + parts[1];
    return parts[0] || 0;
  }

  function isCounterpartQueueNode(el) {
    if (!el) return true;
    if (el.hasAttribute("hidden")) return true;
    if (el.getAttribute("slot") === "counterpart-renderer") return true;
    if (el.closest("[hidden]")) return true;
    const wrapper = el.closest("ytmusic-playlist-panel-video-wrapper-renderer");
    if (wrapper) {
      const primary =
        wrapper.querySelector("ytmusic-player-queue-item:not([hidden]):not([slot='counterpart-renderer'])") ||
        wrapper.querySelector("ytmusic-player-queue-item");
      if (primary && primary !== el) return true;
    }
    return false;
  }

  function getDomQueueItems() {
    const nodes = Array.from(document.querySelectorAll("ytmusic-player-queue-item"));
    if (nodes.length === 0) return [];
    const items = [];
    for (let idx = 0; idx < nodes.length; idx++) {
      const el = nodes[idx];
      if (isCounterpartQueueNode(el)) continue;
      const titleEl = el.querySelector(".song-title");
      const bylineEl = el.querySelector(".byline");
      const durEl = el.querySelector(".duration");
      const imgEl = el.querySelector("img");
      const title = titleEl ? titleEl.textContent.trim() : "";
      if (!title) continue;
      const rawSrc = imgEl && imgEl.src && imgEl.src.startsWith("http") ? imgEl.src : "";
      items.push({
        queueIndex: idx,
        videoId: "",
        playlistId: "",
        title,
        artist: bylineEl ? bylineEl.textContent.replace(/\s+/g, " ").trim() : "",
        duration: durEl ? durEl.textContent.trim() : "",
        thumbnail: rawSrc ? upgradeArtworkUrl(rawSrc, 160) : "",
        isCurrent: el.hasAttribute("selected")
      });
    }
    return items;
  }

  function getNextTrackPreview() {
    const items = getDomQueueItems();
    if (items.length === 0) return { nextTitle: "", nextArtist: "" };
    const curIdx = items.findIndex((x) => x.isCurrent);
    if (curIdx >= 0 && curIdx + 1 < items.length) {
      return {
        nextTitle: items[curIdx + 1].title,
        nextArtist: items[curIdx + 1].artist
      };
    }
    return { nextTitle: "", nextArtist: "" };
  }

  function getPlaybackState() {
    const video = getVideo();
    const playerBar = document.querySelector("ytmusic-player-bar");
    const meta = (navigator.mediaSession && navigator.mediaSession.metadata) || null;

    let title = "";
    let artist = "";
    let artwork = "";

    if (playerBar) {
      const titleEl = playerBar.querySelector(".title.ytmusic-player-bar") || playerBar.querySelector(".title");
      const bylineEl = playerBar.querySelector(".byline.ytmusic-player-bar") || playerBar.querySelector(".byline");
      const imgEl = playerBar.querySelector("img.image") || playerBar.querySelector(".thumbnail-image-wrapper img");

      if (titleEl && titleEl.textContent) title = titleEl.textContent.trim();
      if (bylineEl && bylineEl.textContent) {
        artist = bylineEl.textContent.replace(/\s+/g, " ").trim();
      }
      if (imgEl && imgEl.src) artwork = imgEl.src;
    }

    if (!title && meta && meta.title) title = meta.title;
    if (!artist && meta && meta.artist) artist = meta.artist;
    if (!artwork && meta && meta.artwork && meta.artwork.length > 0) {
      artwork = meta.artwork[meta.artwork.length - 1].src || "";
    }

    artwork = upgradeArtworkUrl(artwork, 544);

    let currentTime = 0;
    let duration = 0;
    let isPlaying = false;

    if (video) {
      currentTime = isFinite(video.currentTime) ? video.currentTime : 0;
      duration = isFinite(video.duration) ? video.duration : 0;
      isPlaying = !video.paused && !video.ended && video.readyState >= 2;
    }

    if ((!duration || isNaN(duration)) && playerBar) {
      const timeEl = playerBar.querySelector(".time-info");
      if (timeEl && timeEl.textContent && timeEl.textContent.includes("/")) {
        const [curStr, durStr] = timeEl.textContent.split("/");
        currentTime = parseTimeString(curStr);
        duration = parseTimeString(durStr);
      }
    }

    const nextPreview = getNextTrackPreview();

    return {
      type: "playbackState",
      title,
      artist,
      artwork,
      isPlaying,
      currentTime: Math.max(0, Number(currentTime.toFixed(2))),
      duration: Math.max(0, Number(duration.toFixed(2))),
      nextTitle: nextPreview.nextTitle,
      nextArtist: nextPreview.nextArtist
    };
  }

  function sendJson(obj) {
    if (!ws || ws.readyState !== WebSocket.OPEN) return;
    try {
      ws.send(JSON.stringify(obj));
    } catch (_) {}
  }

  function sendState(force = false) {
    if (!ws || ws.readyState !== WebSocket.OPEN) return;
    const state = getPlaybackState();
    const key = JSON.stringify([
      state.title,
      state.artist,
      state.artwork,
      state.isPlaying,
      Math.floor(state.currentTime),
      Math.floor(state.duration),
      state.nextTitle
    ]);
    if (!force && key === lastSentKey) return;
    lastSentKey = key;
    sendJson(state);
  }

  function clickSelector(selectors) {
    for (const sel of selectors) {
      const el = document.querySelector(sel);
      if (el) {
        el.click();
        return true;
      }
    }
    return false;
  }

  // --- Shared JSON Parsing Helpers ---

  function runsToText(obj) {
    if (!obj) return "";
    if (typeof obj === "string") return obj;
    if (obj.simpleText) return obj.simpleText;
    if (Array.isArray(obj.runs)) {
      return obj.runs.map((r) => r.text || "").join("");
    }
    return "";
  }

  function findPlayableEndpoint(node, depth = 0) {
    if (!node || typeof node !== "object" || depth > 8) return null;
    if (node.watchEndpoint && (node.watchEndpoint.videoId || node.watchEndpoint.playlistId)) {
      return {
        videoId: node.watchEndpoint.videoId || "",
        playlistId: node.watchEndpoint.playlistId || ""
      };
    }
    if (node.watchPlaylistEndpoint && node.watchPlaylistEndpoint.playlistId) {
      return {
        videoId: "",
        playlistId: node.watchPlaylistEndpoint.playlistId
      };
    }
    for (const key of Object.keys(node)) {
      const val = node[key];
      if (val && typeof val === "object") {
        const found = findPlayableEndpoint(val, depth + 1);
        if (found) return found;
      }
    }
    return null;
  }

  function extractThumbnail(renderer, size = 226) {
    const thumbs =
      (renderer.thumbnail &&
        renderer.thumbnail.musicThumbnailRenderer &&
        renderer.thumbnail.musicThumbnailRenderer.thumbnail &&
        renderer.thumbnail.musicThumbnailRenderer.thumbnail.thumbnails) ||
      (renderer.thumbnailRenderer &&
        renderer.thumbnailRenderer.musicThumbnailRenderer &&
        renderer.thumbnailRenderer.musicThumbnailRenderer.thumbnail &&
        renderer.thumbnailRenderer.musicThumbnailRenderer.thumbnail.thumbnails) ||
      (renderer.thumbnail && renderer.thumbnail.thumbnails) ||
      [];
    if (thumbs.length === 0) return "";
    return upgradeArtworkUrl(thumbs[thumbs.length - 1].url || "", size);
  }

  // --- Up Next Queue ---

  async function fetchQueue(requestId) {
    const bridgeResp = await callPageBridge("getQueue");
    if (bridgeResp && Array.isArray(bridgeResp.items) && bridgeResp.items.length > 0) {
      sendJson({
        type: "queueState",
        requestId,
        source: "page",
        items: bridgeResp.items.slice(0, 60)
      });
      return;
    }

    const domItems = getDomQueueItems();
    if (domItems.length > 0) {
      sendJson({
        type: "queueState",
        requestId,
        source: "dom",
        items: domItems.slice(0, 60)
      });
      return;
    }

    const cfg = await getPageConfig();
    const videoId = cfg.videoId || "";
    if (!videoId) {
      sendJson({
        type: "queueState",
        requestId,
        source: "none",
        items: []
      });
      return;
    }

    try {
      const json = await innertubePost("next", {
        videoId,
        isAudioOnly: true
      });
      const items = [];

      function walkQueue(node) {
        if (!node || typeof node !== "object") return;
        if (node.playlistPanelVideoWrapperRenderer) {
          walkQueue(node.playlistPanelVideoWrapperRenderer.primaryRenderer);
          return;
        }
        if (node.playlistPanelVideoRenderer) {
          const r = node.playlistPanelVideoRenderer;
          items.push({
            queueIndex: -1,
            videoId: r.videoId || "",
            playlistId:
              (r.navigationEndpoint &&
                r.navigationEndpoint.watchEndpoint &&
                r.navigationEndpoint.watchEndpoint.playlistId) ||
              "",
            title: runsToText(r.title),
            artist: runsToText(r.shortBylineText) || runsToText(r.longBylineText),
            duration: runsToText(r.lengthText),
            thumbnail: extractThumbnail(r, 160),
            isCurrent: Boolean(r.selected)
          });
        }
        if (Array.isArray(node)) {
          for (const el of node) walkQueue(el);
        } else {
          for (const k of Object.keys(node)) {
            if (node[k] && typeof node[k] === "object") walkQueue(node[k]);
          }
        }
      }

      walkQueue(json);
      sendJson({
        type: "queueState",
        requestId,
        source: "api",
        items: items.slice(0, 50)
      });
    } catch (err) {
      sendJson({
        type: "queueState",
        requestId,
        items: [],
        error: "Could not load Up Next queue: " + (err && err.message ? err.message : "Error")
      });
    }
  }

  function playQueueIndex(index) {
    const idx = Number(index);
    if (!isFinite(idx) || idx < 0) return;
    const nodes = document.querySelectorAll("ytmusic-player-queue-item");
    const target = nodes[idx];
    if (!target) return;

    const clickEl =
      target.querySelector("ytmusic-play-button-renderer") ||
      target.querySelector(".thumbnail-overlay") ||
      target.querySelector(".song-title") ||
      target;
    clickEl.click();
    setTimeout(() => {
      sendState(true);
      fetchQueue("");
    }, 350);
  }

  // --- Home Feed ---

  function parseHomeFeed(json) {
    const shelves = [];

    function parseListItem(item) {
      const ep =
        findPlayableEndpoint(item.overlay) ||
        findPlayableEndpoint(item.playlistItemData) ||
        findPlayableEndpoint(item.flexColumns) ||
        findPlayableEndpoint(item.navigationEndpoint);

      const videoId =
        (ep && ep.videoId) ||
        (item.playlistItemData && item.playlistItemData.videoId) ||
        "";
      const playlistId = (ep && ep.playlistId) || "";
      if (!videoId && !playlistId) return null;

      const cols = Array.isArray(item.flexColumns) ? item.flexColumns : [];
      const colText = (i) => {
        const c = cols[i];
        if (!c || !c.musicResponsiveListItemFlexColumnRenderer) return "";
        return runsToText(c.musicResponsiveListItemFlexColumnRenderer.text);
      };

      return {
        videoId,
        playlistId,
        title: (colText(0) || "Untitled").trim(),
        subtitle: colText(1).replace(/\s+/g, " ").trim(),
        thumbnail: extractThumbnail(item, 180)
      };
    }

    function parseTwoRowItem(item) {
      const ep =
        findPlayableEndpoint(item.thumbnailOverlay) ||
        findPlayableEndpoint(item.navigationEndpoint) ||
        findPlayableEndpoint(item.title);

      if (!ep || (!ep.videoId && !ep.playlistId)) return null;

      return {
        videoId: ep.videoId || "",
        playlistId: ep.playlistId || "",
        title: (runsToText(item.title) || "Untitled").trim(),
        subtitle: runsToText(item.subtitle).replace(/\s+/g, " ").trim(),
        thumbnail: extractThumbnail(item, 280)
      };
    }

    function walkHome(node) {
      if (!node || typeof node !== "object") return;
      if (node.musicCarouselShelfRenderer) {
        const shelf = node.musicCarouselShelfRenderer;
        const header = shelf.header && shelf.header.musicCarouselShelfBasicHeaderRenderer;
        const shelfTitle = header ? runsToText(header.title) : "";
        const rawItems = Array.isArray(shelf.contents) ? shelf.contents : [];

        const listItems = [];
        const cardItems = [];

        for (const entry of rawItems) {
          if (entry.musicResponsiveListItemRenderer) {
            const parsed = parseListItem(entry.musicResponsiveListItemRenderer);
            if (parsed) listItems.push(parsed);
          } else if (entry.musicTwoRowItemRenderer) {
            const parsed = parseTwoRowItem(entry.musicTwoRowItemRenderer);
            if (parsed) cardItems.push(parsed);
          }
        }

        if (listItems.length > 0) {
          shelves.push({
            title: shelfTitle || "Quick picks",
            layout: "list",
            items: listItems.slice(0, 16)
          });
        } else if (cardItems.length > 0) {
          shelves.push({
            title: shelfTitle || "Recommended",
            layout: "cards",
            items: cardItems.slice(0, 16)
          });
        }
        return;
      }

      if (Array.isArray(node)) {
        for (const el of node) walkHome(el);
      } else {
        for (const k of Object.keys(node)) {
          if (node[k] && typeof node[k] === "object") walkHome(node[k]);
        }
      }
    }

    walkHome(json);
    return shelves.slice(0, 10);
  }

  async function fetchHomeFeed(requestId) {
    try {
      const json = await innertubePost("browse", {
        browseId: "FEmusic_home"
      });
      const shelves = parseHomeFeed(json);
      sendJson({
        type: "homeFeed",
        requestId,
        shelves
      });
    } catch (err) {
      sendJson({
        type: "homeFeed",
        requestId,
        shelves: [],
        error: "Could not load Home feed: " + (err && err.message ? err.message : "Unknown error")
      });
    }
  }

  // --- Search ---

  function parseSearchResults(json) {
    const results = [];
    const seen = new Set();

    function addItem(videoId, playlistId, title, subtitle, thumbnail) {
      if (!videoId || seen.has(videoId)) return;
      seen.add(videoId);
      results.push({
        videoId,
        playlistId: playlistId || "",
        title: (title || "Untitled").trim(),
        subtitle: (subtitle || "").replace(/\s+/g, " ").trim(),
        thumbnail: thumbnail || ""
      });
    }

    function walk(node) {
      if (!node || typeof node !== "object") return;

      if (node.musicCardShelfRenderer) {
        const card = node.musicCardShelfRenderer;
        const ep =
          findPlayableEndpoint(card.title) ||
          findPlayableEndpoint(card.buttons) ||
          findPlayableEndpoint(card.thumbnailOverlay);
        if (ep && ep.videoId) {
          addItem(
            ep.videoId,
            ep.playlistId,
            runsToText(card.title),
            runsToText(card.subtitle),
            extractThumbnail(card, 180)
          );
        }
      }

      if (node.musicResponsiveListItemRenderer) {
        const item = node.musicResponsiveListItemRenderer;
        const ep =
          findPlayableEndpoint(item.overlay) ||
          findPlayableEndpoint(item.playlistItemData) ||
          findPlayableEndpoint(item.flexColumns) ||
          findPlayableEndpoint(item.navigationEndpoint);

        const videoId =
          (ep && ep.videoId) ||
          (item.playlistItemData && item.playlistItemData.videoId) ||
          "";

        if (videoId) {
          const cols = Array.isArray(item.flexColumns) ? item.flexColumns : [];
          const colText = (idx) => {
            const c = cols[idx];
            if (!c || !c.musicResponsiveListItemFlexColumnRenderer) return "";
            return runsToText(c.musicResponsiveListItemFlexColumnRenderer.text);
          };
          addItem(videoId, ep ? ep.playlistId : "", colText(0), colText(1), extractThumbnail(item, 180));
        }
      }

      if (Array.isArray(node)) {
        for (const el of node) walk(el);
      } else {
        for (const key of Object.keys(node)) {
          const val = node[key];
          if (val && typeof val === "object") walk(val);
        }
      }
    }

    walk(json);
    return results.slice(0, 25);
  }

  async function performSearch(query, filter, requestId) {
    const cleanQuery = (query || "").trim();
    if (!cleanQuery) {
      sendJson({ type: "searchResults", requestId, query: cleanQuery, results: [] });
      return;
    }

    const payload = { query: cleanQuery };
    if (filter === "songs") {
      payload.params = "EgWKAQIIAWoKEAMQBBAJEAoQBQ%3D%3D";
    } else if (filter === "videos") {
      payload.params = "EgWKAQIQAWoKEAMQBBAJEAoQBQ%3D%3D";
    }

    try {
      const json = await innertubePost("search", payload);
      const results = parseSearchResults(json);
      sendJson({
        type: "searchResults",
        requestId,
        query: cleanQuery,
        results
      });
    } catch (err) {
      sendJson({
        type: "searchResults",
        requestId,
        query: cleanQuery,
        results: [],
        error: "Search failed: " + (err && err.message ? err.message : "Unknown error")
      });
    }
  }

  // --- Play Track (SPA in-page first, URL fallback) ---

  async function playTrack(videoId, playlistId) {
    if (!videoId && !playlistId) return;

    const resp = await callPageBridge("playTrack", {
      videoId: videoId || "",
      playlistId: playlistId || ""
    });

    if (resp && resp.ok) {
      setTimeout(() => {
        sendState(true);
        fetchQueue("");
      }, 900);
      return;
    }

    // Fallback to direct navigation if bridge wasn't ready
    let url = "https://music.youtube.com/watch?";
    const params = [];
    if (videoId) params.push("v=" + encodeURIComponent(videoId));
    if (playlistId) params.push("list=" + encodeURIComponent(playlistId));
    url += params.join("&");
    window.location.href = url;
  }

  function handleCommand(msg) {
    const action = msg.action;
    const video = getVideo();

    if (action === "playPause") {
      if (video && video.readyState >= 1) {
        if (video.paused) {
          video.play().catch(() => {
            clickSelector(["ytmusic-player-bar #play-pause-button", "#play-pause-button"]);
          });
        } else {
          video.pause();
        }
      } else {
        clickSelector(["ytmusic-player-bar #play-pause-button", "#play-pause-button"]);
      }
      sendState(true);
    } else if (action === "next") {
      clickSelector([
        "ytmusic-player-bar .next-button",
        ".next-button"
      ]);
      setTimeout(() => sendState(true), 80);
      setTimeout(() => {
        sendState(true);
        fetchQueue("");
      }, 450);
    } else if (action === "prev") {
      clickSelector([
        "ytmusic-player-bar .previous-button",
        ".previous-button"
      ]);
      setTimeout(() => sendState(true), 80);
      setTimeout(() => {
        sendState(true);
        fetchQueue("");
      }, 450);
    } else if (action === "seek") {
      const target = Number(msg.time);
      if (video && isFinite(target)) {
        const maxDur = isFinite(video.duration) ? video.duration : target;
        video.currentTime = Math.max(0, Math.min(maxDur, target));
        sendState(true);
      }
    } else if (action === "search") {
      performSearch(msg.query, msg.filter || "all", msg.requestId || "");
    } else if (action === "playTrack") {
      playTrack(msg.videoId, msg.playlistId);
    } else if (action === "getHome") {
      fetchHomeFeed(msg.requestId || "");
    } else if (action === "getQueue") {
      fetchQueue(msg.requestId || "");
    } else if (action === "playQueueIndex") {
      playQueueIndex(msg.index);
    }
  }

  function connect() {
    if (reconnectTimer) {
      clearTimeout(reconnectTimer);
      reconnectTimer = null;
    }

    try {
      ws = new WebSocket(WS_URL);
    } catch (_) {
      scheduleReconnect();
      return;
    }

    ws.onopen = () => {
      isConnected = true;
      lastSentKey = "";
      sendState(true);
    };

    ws.onmessage = (event) => {
      try {
        const msg = JSON.parse(event.data);
        if (msg && msg.type === "command") {
          handleCommand(msg);
        }
      } catch (_) {}
    };

    ws.onclose = () => {
      isConnected = false;
      ws = null;
      scheduleReconnect();
    };

    ws.onerror = () => {
      try { ws.close(); } catch (_) {}
    };
  }

  function scheduleReconnect() {
    if (reconnectTimer) return;
    reconnectTimer = setTimeout(() => {
      reconnectTimer = null;
      connect();
    }, 2000);
  }

  let attachedVideo = null;
  function attachVideoListeners() {
    const video = getVideo();
    if (!video || video === attachedVideo) return;
    attachedVideo = video;
    ["play", "pause", "seeked", "loadedmetadata", "durationchange", "ended"].forEach((ev) => {
      video.addEventListener(ev, () => sendState(true));
    });
  }

  let observedBar = null;
  function attachPlayerBarObserver() {
    const bar = document.querySelector("ytmusic-player-bar");
    if (!bar || bar === observedBar) return;
    observedBar = bar;
    const obs = new MutationObserver(() => sendState(false));
    obs.observe(bar, {
      subtree: true,
      childList: true,
      characterData: true,
      attributes: true,
      attributeFilter: ["src", "title", "aria-label"]
    });
  }

  setInterval(() => {
    attachVideoListeners();
    attachPlayerBarObserver();
    sendState(false);
  }, 500);

  if (typeof chrome !== "undefined" && chrome.runtime && chrome.runtime.onMessage) {
    chrome.runtime.onMessage.addListener((req, _sender, sendResponse) => {
      if (req && req.type === "getStatus") {
        sendResponse({
          connected: isConnected,
          state: getPlaybackState()
        });
      }
    });
  }

  connect();
})();
