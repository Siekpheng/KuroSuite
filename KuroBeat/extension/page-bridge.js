(() => {
  if (window.__ytmRemoteBridgeLoaded) return;
  window.__ytmRemoteBridgeLoaded = true;

  function getConfig() {
    const cfg = window.ytcfg;
    const get = (k, def) => {
      try {
        if (cfg && typeof cfg.get === "function") {
          const v = cfg.get(k);
          return v !== undefined && v !== null ? v : def;
        }
      } catch (_) {}
      return def;
    };

    let videoId = "";
    try {
      const mp = document.getElementById("movie_player") || document.querySelector(".html5-video-player");
      if (mp && typeof mp.getVideoData === "function") {
        const vd = mp.getVideoData();
        if (vd && vd.video_id) videoId = vd.video_id;
      }
    } catch (_) {}

    if (!videoId) {
      try {
        const u = new URL(window.location.href);
        videoId = u.searchParams.get("v") || "";
      } catch (_) {}
    }

    return {
      apiKey: get("INNERTUBE_API_KEY", ""),
      clientVersion: get("INNERTUBE_CLIENT_VERSION", "1.20250101.01.00"),
      hl: get("HL", document.documentElement.lang || "en"),
      gl: get("GL", "US"),
      sessionIndex: String(get("SESSION_INDEX", "0")),
      delegatedSessionId: get("DELEGATED_SESSION_ID", ""),
      visitorData: get("VISITOR_DATA", ""),
      videoId
    };
  }

  function playTrackInPage(videoId, playlistId) {
    if (!videoId && !playlistId) return false;

    const endpoint = videoId
      ? {
          watchEndpoint: Object.assign(
            { videoId },
            playlistId ? { playlistId } : {}
          )
        }
      : {
          watchPlaylistEndpoint: { playlistId }
        };

    // Method 1: Native YouTube Music SPA navigation (zero page reload, keeps audio & queue!)
    const app = document.querySelector("ytmusic-app");
    if (app) {
      try {
        app.dispatchEvent(
          new CustomEvent("yt-navigate", {
            bubbles: true,
            composed: true,
            detail: { endpoint }
          })
        );
        // Ensure playback starts if paused
        setTimeout(() => {
          const video = document.querySelector("video.html5-main-video") || document.querySelector("video");
          if (video && video.paused) {
            video.play().catch(() => {});
          }
        }, 500);
        return true;
      } catch (_) {}
    }

    // Method 2: Direct YouTube HTML5 player API
    try {
      const mp = document.getElementById("movie_player") || document.querySelector(".html5-video-player");
      if (videoId && mp && typeof mp.loadVideoById === "function") {
        mp.loadVideoById(videoId);
        return true;
      }
    } catch (_) {}

    return false;
  }

  function upgradeThumbUrl(url, size) {
    if (!url || typeof url !== "string" || !url.startsWith("http")) return "";
    return url.replace(/w\d+-h\d+/g, "w" + size + "-h" + size).replace(/=s\d+/g, "=s" + size);
  }

  function runsText(obj) {
    if (!obj) return "";
    if (typeof obj === "string") return obj;
    if (obj.simpleText) return obj.simpleText;
    if (Array.isArray(obj.runs)) return obj.runs.map((r) => r.text || "").join("");
    return "";
  }

  function isCounterpartOrHidden(el) {
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

  function getQueueFromPage() {
    const nodes = Array.from(document.querySelectorAll("ytmusic-player-queue-item"));
    if (nodes.length === 0) return [];
    const items = [];
    for (let idx = 0; idx < nodes.length; idx++) {
      const el = nodes[idx];
      if (isCounterpartOrHidden(el)) continue;

      const d = el.data || (el.__data && el.__data.data) || null;
      const titleEl = el.querySelector(".song-title");
      const bylineEl = el.querySelector(".byline");
      const durEl = el.querySelector(".duration");
      const imgEl = el.querySelector("img");

      const title = (d && runsText(d.title)) || (titleEl ? titleEl.textContent.trim() : "");
      if (!title) continue;

      const artist =
        (d && (runsText(d.shortBylineText) || runsText(d.longBylineText))) ||
        (bylineEl ? bylineEl.textContent.replace(/\s+/g, " ").trim() : "");
      const duration = (d && runsText(d.lengthText)) || (durEl ? durEl.textContent.trim() : "");
      const videoId =
        (d && d.videoId) ||
        (d && d.navigationEndpoint && d.navigationEndpoint.watchEndpoint && d.navigationEndpoint.watchEndpoint.videoId) ||
        "";
      const playlistId =
        (d && d.navigationEndpoint && d.navigationEndpoint.watchEndpoint && d.navigationEndpoint.watchEndpoint.playlistId) ||
        "";

      let thumb = "";
      if (d && d.thumbnail && Array.isArray(d.thumbnail.thumbnails) && d.thumbnail.thumbnails.length > 0) {
        thumb = upgradeThumbUrl(d.thumbnail.thumbnails[d.thumbnail.thumbnails.length - 1].url, 160);
      }
      if (!thumb && imgEl && imgEl.src && imgEl.src.startsWith("http")) {
        thumb = upgradeThumbUrl(imgEl.src, 160);
      }
      if (!thumb && videoId) {
        thumb = "https://i.ytimg.com/vi/" + videoId + "/hqdefault.jpg";
      }

      items.push({
        queueIndex: idx,
        videoId,
        playlistId,
        title,
        artist,
        duration,
        thumbnail: thumb,
        isCurrent: el.hasAttribute("selected") || Boolean(d && d.selected)
      });
    }
    return items.slice(0, 60);
  }

  window.addEventListener("message", (event) => {
    if (event.source !== window) return;
    const msg = event.data;
    if (!msg || msg.source !== "ytm-remote-ext") return;

    if (msg.type === "getConfig") {
      window.postMessage(
        {
          source: "ytm-remote-page",
          type: "configResponse",
          reqId: msg.reqId,
          config: getConfig()
        },
        "*"
      );
    } else if (msg.type === "getQueue") {
      window.postMessage(
        {
          source: "ytm-remote-page",
          type: "queueResponse",
          reqId: msg.reqId,
          items: getQueueFromPage()
        },
        "*"
      );
    } else if (msg.type === "playTrack") {
      const ok = playTrackInPage(msg.videoId || "", msg.playlistId || "");
      window.postMessage(
        {
          source: "ytm-remote-page",
          type: "playTrackResponse",
          reqId: msg.reqId,
          ok
        },
        "*"
      );
    }
  });
})();
