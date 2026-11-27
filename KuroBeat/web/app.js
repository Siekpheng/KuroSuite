(() => {
  const statusDot = document.getElementById("statusDot");
  const statusText = document.getElementById("statusText");
  const ambientBg = document.getElementById("ambientBg");

  // Tabs & Views
  const navBtns = Array.from(document.querySelectorAll(".nav-btn"));
  const views = {
    home: document.getElementById("viewHome"),
    player: document.getElementById("viewPlayer"),
    queue: document.getElementById("viewQueue"),
    search: document.getElementById("viewSearch")
  };

  // Player elements
  const artFrame = document.getElementById("artFrame");
  const albumArt = document.getElementById("albumArt");
  const artPlaceholder = document.getElementById("artPlaceholder");
  const trackTitle = document.getElementById("trackTitle");
  const trackArtist = document.getElementById("trackArtist");
  const seekBar = document.getElementById("seekBar");
  const currentTimeEl = document.getElementById("currentTime");
  const durationTimeEl = document.getElementById("durationTime");
  const prevBtn = document.getElementById("prevBtn");
  const playPauseBtn = document.getElementById("playPauseBtn");
  const nextBtn = document.getElementById("nextBtn");
  const playIcon = document.getElementById("playIcon");
  const pauseIcon = document.getElementById("pauseIcon");
  const upNextPreviewBtn = document.getElementById("upNextPreviewBtn");
  const upNextPreviewText = document.getElementById("upNextPreviewText");

  // Mini player elements
  const miniPlayer = document.getElementById("miniPlayer");
  const miniPlayerTap = document.getElementById("miniPlayerTap");
  const miniArt = document.getElementById("miniArt");
  const miniTitle = document.getElementById("miniTitle");
  const miniArtist = document.getElementById("miniArtist");
  const miniPlayPauseBtn = document.getElementById("miniPlayPauseBtn");
  const miniPlayIcon = document.getElementById("miniPlayIcon");
  const miniPauseIcon = document.getElementById("miniPauseIcon");
  const miniNextBtn = document.getElementById("miniNextBtn");

  // Volume elements
  const muteBtn = document.getElementById("muteBtn");
  const volHighIcon = document.getElementById("volHighIcon");
  const volMutedIcon = document.getElementById("volMutedIcon");
  const volDownBtn = document.getElementById("volDownBtn");
  const volUpBtn = document.getElementById("volUpBtn");
  const volumeSlider = document.getElementById("volumeSlider");
  const volumeValue = document.getElementById("volumeValue");

  // Home elements
  const refreshHomeBtn = document.getElementById("refreshHomeBtn");
  const homeStatus = document.getElementById("homeStatus");
  const homeShelves = document.getElementById("homeShelves");

  // Queue elements
  const refreshQueueBtn = document.getElementById("refreshQueueBtn");
  const queueStatus = document.getElementById("queueStatus");
  const queueList = document.getElementById("queueList");

  // Search elements
  const searchForm = document.getElementById("searchForm");
  const searchInput = document.getElementById("searchInput");
  const clearSearchBtn = document.getElementById("clearSearchBtn");
  const filterChips = Array.from(document.querySelectorAll(".filter-chip"));
  const searchStatus = document.getElementById("searchStatus");
  const searchResultsEl = document.getElementById("searchResults");

  const toastEl = document.getElementById("toast");

  let ws = null;
  let reconnectTimer = null;
  let toastTimer = null;
  let activeTab = "player";

  let playback = {
    title: "",
    artist: "",
    artwork: "",
    isPlaying: false,
    currentTime: 0,
    duration: 0,
    nextTitle: "",
    nextArtist: "",
    syncedAt: performance.now()
  };

  let extensionConnected = false;
  let isSeeking = false;
  let isDraggingVolume = false;
  let lastVolumeSend = 0;
  let pendingVolumeTimer = null;
  let lastArtwork = "";
  let lastTrackTitle = "";
  let optimisticPlayUntil = 0;

  let homeLoaded = false;
  let activeHomeReqId = "";
  let activeQueueReqId = "";
  let activeSearchReqId = "";
  let activeFilter = "all";
  let homeTimeout = null;
  let queueTimeout = null;
  let searchTimeout = null;

  function makeReqId(prefix) {
    return prefix + "-" + Date.now() + "-" + Math.random().toString(36).slice(2, 6);
  }

  function showToast(text) {
    toastEl.textContent = text;
    toastEl.classList.remove("hidden");
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => {
      toastEl.classList.add("hidden");
    }, 2200);
  }

  function formatTime(sec) {
    if (!isFinite(sec) || sec < 0) sec = 0;
    const total = Math.floor(sec);
    const hrs = Math.floor(total / 3600);
    const mins = Math.floor((total % 3600) / 60);
    const secs = total % 60;
    const pad = (n) => (n < 10 ? "0" + n : String(n));
    return hrs > 0 ? `${hrs}:${pad(mins)}:${pad(secs)}` : `${mins}:${pad(secs)}`;
  }

  function updateSliderFill(el, percent, color = "#ffffff") {
    const pct = Math.max(0, Math.min(100, percent));
    el.style.background = `linear-gradient(90deg, ${color} ${pct}%, rgba(255,255,255,0.2) ${pct}%)`;
  }

  function sendCommand(action, extra = {}) {
    if (!ws || ws.readyState !== WebSocket.OPEN) return false;
    ws.send(JSON.stringify({ type: "command", action, ...extra }));
    return true;
  }

  function setStatus(mode, text) {
    statusDot.classList.remove("ok", "warn");
    if (mode === "ok") statusDot.classList.add("ok");
    else if (mode === "warn") statusDot.classList.add("warn");
    statusText.textContent = text;
  }

  // --- Navigation ---

  function switchTab(tab) {
    if (!views[tab]) return;
    activeTab = tab;
    Object.keys(views).forEach((k) => {
      views[k].classList.toggle("active", k === tab);
    });
    navBtns.forEach((btn) => {
      btn.classList.toggle("active", btn.dataset.tab === tab);
    });

    updateMiniPlayerVisibility();

    if (tab === "home" && !homeLoaded && extensionConnected) {
      requestHomeFeed();
    } else if (tab === "queue" && extensionConnected) {
      requestQueue();
    } else if (tab === "search") {
      setTimeout(() => searchInput.focus(), 60);
    }
  }

  function updateMiniPlayerVisibility() {
    const show = activeTab !== "player" && Boolean(playback.title);
    miniPlayer.classList.toggle("hidden", !show);
  }

  navBtns.forEach((btn) => {
    btn.addEventListener("click", () => switchTab(btn.dataset.tab));
  });

  miniPlayerTap.addEventListener("click", () => switchTab("player"));
  upNextPreviewBtn.addEventListener("click", () => switchTab("queue"));

  // --- Playback Rendering ---

  function renderPlayback() {
    if (!extensionConnected) {
      setStatus("warn", "Open YouTube Music");
    } else {
      setStatus("ok", "Connected");
    }

    const titleText = playback.title || "Nothing Playing";
    const artistText = playback.artist || (extensionConnected ? "Pick a song from Home or Search" : "Open music.youtube.com on PC");

    trackTitle.textContent = titleText;
    trackArtist.textContent = artistText;
    miniTitle.textContent = titleText;
    miniArtist.textContent = artistText;

    if (playback.artwork !== lastArtwork) {
      lastArtwork = playback.artwork;
      if (playback.artwork) {
        albumArt.src = playback.artwork;
        miniArt.src = playback.artwork;
        albumArt.onload = () => {
          albumArt.classList.add("loaded");
          artPlaceholder.classList.add("hidden");
        };
        albumArt.onerror = () => {
          albumArt.classList.remove("loaded");
          artPlaceholder.classList.remove("hidden");
        };
        ambientBg.style.backgroundImage = `url("${playback.artwork}")`;
      } else {
        albumArt.classList.remove("loaded");
        artPlaceholder.classList.remove("hidden");
        ambientBg.style.backgroundImage = "none";
      }
    }

    if (playback.isPlaying) {
      playIcon.classList.add("hidden");
      pauseIcon.classList.remove("hidden");
      miniPlayIcon.classList.add("hidden");
      miniPauseIcon.classList.remove("hidden");
      artFrame.classList.remove("paused");
    } else {
      playIcon.classList.remove("hidden");
      pauseIcon.classList.add("hidden");
      miniPlayIcon.classList.remove("hidden");
      miniPauseIcon.classList.add("hidden");
      artFrame.classList.add("paused");
    }

    if (playback.nextTitle) {
      upNextPreviewText.textContent = playback.nextArtist
        ? `${playback.nextTitle} · ${playback.nextArtist}`
        : playback.nextTitle;
      upNextPreviewBtn.classList.remove("hidden");
    } else {
      upNextPreviewBtn.classList.add("hidden");
    }

    const dur = playback.duration || 0;
    durationTimeEl.textContent = formatTime(dur);
    seekBar.max = dur > 0 ? dur : 100;

    updateMiniPlayerVisibility();

    // Auto-refresh queue when track changes while viewing Up Next
    if (playback.title && playback.title !== lastTrackTitle) {
      lastTrackTitle = playback.title;
      if (activeTab === "queue" && extensionConnected) {
        requestQueue();
      }
    }
  }

  function renderVolume(vol, muted) {
    const clamped = Math.max(0, Math.min(100, Number(vol) || 0));
    if (!isDraggingVolume) {
      volumeSlider.value = clamped;
      updateSliderFill(volumeSlider, muted ? 0 : clamped, "#ff2e55");
    }
    volumeValue.textContent = muted ? "Muted" : `${clamped}%`;
    if (muted || clamped === 0) {
      volHighIcon.classList.add("hidden");
      volMutedIcon.classList.remove("hidden");
      muteBtn.classList.add("muted");
    } else {
      volHighIcon.classList.remove("hidden");
      volMutedIcon.classList.add("hidden");
      muteBtn.classList.remove("muted");
    }
  }

  function tickProgress() {
    if (!isSeeking) {
      let cur = playback.currentTime || 0;
      if (playback.isPlaying && playback.duration > 0) {
        const elapsed = (performance.now() - playback.syncedAt) / 1000;
        cur = Math.min(playback.duration, cur + elapsed);
      }
      currentTimeEl.textContent = formatTime(cur);
      seekBar.value = cur;
      const pct = playback.duration > 0 ? (cur / playback.duration) * 100 : 0;
      updateSliderFill(seekBar, pct, "#ffffff");
    }
    requestAnimationFrame(tickProgress);
  }

  // --- Shared Track Row Helper ---

  function createTrackRow(item, onClick) {
    const btn = document.createElement("button");
    btn.type = "button";
    btn.className = "track-row" + (item.isCurrent ? " current" : "");

    const img = document.createElement("img");
    img.className = "track-thumb";
    img.alt = "";
    img.loading = "lazy";
    if (item.thumbnail) img.src = item.thumbnail;

    const meta = document.createElement("div");
    meta.className = "track-meta";

    const title = document.createElement("div");
    title.className = "track-row-title";
    title.textContent = item.title || "Untitled";

    const sub = document.createElement("div");
    sub.className = "track-row-sub";
    sub.textContent = item.subtitle || item.artist || "";

    meta.appendChild(title);
    meta.appendChild(sub);

    btn.appendChild(img);
    btn.appendChild(meta);

    if (item.isCurrent) {
      const badge = document.createElement("span");
      badge.className = "now-badge";
      badge.textContent = "Playing";
      btn.appendChild(badge);
    } else if (item.duration) {
      const dur = document.createElement("span");
      dur.className = "track-duration";
      dur.textContent = item.duration;
      btn.appendChild(dur);
    }

    btn.addEventListener("click", onClick);
    return btn;
  }

  // --- Home Feed ---

  function requestHomeFeed() {
    activeHomeReqId = makeReqId("home");
    homeStatus.textContent = "Loading your YouTube Music Home...";
    homeStatus.classList.remove("hidden");
    const sent = sendCommand("getHome", { requestId: activeHomeReqId });
    if (!sent) {
      homeStatus.textContent = "Not connected to PC server.";
      return;
    }
    clearTimeout(homeTimeout);
    const reqCopy = activeHomeReqId;
    homeTimeout = setTimeout(() => {
      if (activeHomeReqId === reqCopy) {
        homeStatus.textContent = "Home timed out. Reload the browser extension (v1.2.0) and refresh music.youtube.com, then tap Refresh.";
        homeStatus.classList.remove("hidden");
      }
    }, 8000);
  }

  function renderHomeFeed(msg) {
    if (msg.requestId && msg.requestId !== activeHomeReqId) return;
    clearTimeout(homeTimeout);
    if (msg.error) {
      homeStatus.textContent = msg.error;
      homeStatus.classList.remove("hidden");
      return;
    }

    const shelves = Array.isArray(msg.shelves) ? msg.shelves : [];
    if (shelves.length === 0) {
      homeStatus.textContent = "No Home recommendations found yet. Try refreshing.";
      homeStatus.classList.remove("hidden");
      homeShelves.innerHTML = "";
      return;
    }

    homeLoaded = true;
    homeStatus.classList.add("hidden");
    homeShelves.innerHTML = "";

    shelves.forEach((shelf) => {
      const section = document.createElement("div");
      const h3 = document.createElement("h3");
      h3.className = "shelf-title";
      h3.textContent = shelf.title || "Recommended";
      section.appendChild(h3);

      const items = Array.isArray(shelf.items) ? shelf.items : [];

      if (shelf.layout === "list") {
        const list = document.createElement("div");
        list.className = "track-list";
        items.forEach((item) => {
          list.appendChild(
            createTrackRow(item, () => {
              sendCommand("playTrack", {
                videoId: item.videoId || "",
                playlistId: item.playlistId || ""
              });
              showToast("Playing: " + (item.title || "Track"));
              switchTab("player");
            })
          );
        });
        section.appendChild(list);
      } else {
        const row = document.createElement("div");
        row.className = "shelf-cards";
        items.forEach((item) => {
          const card = document.createElement("button");
          card.type = "button";
          card.className = "home-card";

          const img = document.createElement("img");
          img.className = "home-card-thumb";
          img.alt = "";
          img.loading = "lazy";
          if (item.thumbnail) img.src = item.thumbnail;

          const title = document.createElement("div");
          title.className = "home-card-title";
          title.textContent = item.title || "Untitled";

          const sub = document.createElement("div");
          sub.className = "home-card-sub";
          sub.textContent = item.subtitle || "";

          card.appendChild(img);
          card.appendChild(title);
          card.appendChild(sub);

          card.addEventListener("click", () => {
            sendCommand("playTrack", {
              videoId: item.videoId || "",
              playlistId: item.playlistId || ""
            });
            showToast("Playing: " + (item.title || "Mix"));
            switchTab("player");
          });

          row.appendChild(card);
        });
        section.appendChild(row);
      }

      homeShelves.appendChild(section);
    });
  }

  refreshHomeBtn.addEventListener("click", requestHomeFeed);

  // --- Up Next Queue ---

  function requestQueue() {
    activeQueueReqId = makeReqId("queue");
    queueStatus.textContent = "Loading Up Next queue...";
    queueStatus.classList.remove("hidden");
    const sent = sendCommand("getQueue", { requestId: activeQueueReqId });
    if (!sent) {
      queueStatus.textContent = "Not connected to PC server.";
      return;
    }
    clearTimeout(queueTimeout);
    const reqCopy = activeQueueReqId;
    queueTimeout = setTimeout(() => {
      if (activeQueueReqId === reqCopy) {
        queueStatus.textContent = "Up Next timed out. Reload the browser extension (v1.2.0) and refresh music.youtube.com, then tap Refresh.";
        queueStatus.classList.remove("hidden");
      }
    }, 8000);
  }

  function renderQueue(msg) {
    if (msg.requestId && activeQueueReqId && msg.requestId !== activeQueueReqId) return;
    clearTimeout(queueTimeout);
    if (msg.error) {
      queueStatus.textContent = msg.error;
      queueStatus.classList.remove("hidden");
      return;
    }

    const items = Array.isArray(msg.items) ? msg.items : [];
    queueList.innerHTML = "";

    if (items.length === 0) {
      queueStatus.textContent = "No songs in Up Next yet. Play any song from Home or Search.";
      queueStatus.classList.remove("hidden");
      return;
    }

    // Update Up Next preview pill if we have a next item
    const curIdx = items.findIndex((i) => i.isCurrent);
    if (curIdx >= 0 && curIdx + 1 < items.length) {
      playback.nextTitle = items[curIdx + 1].title || "";
      playback.nextArtist = items[curIdx + 1].artist || "";
      upNextPreviewText.textContent = playback.nextArtist
        ? `${playback.nextTitle} · ${playback.nextArtist}`
        : playback.nextTitle;
      upNextPreviewBtn.classList.remove("hidden");
    }

    queueStatus.classList.add("hidden");

    items.forEach((item) => {
      queueList.appendChild(
        createTrackRow(item, () => {
          if (typeof item.queueIndex === "number" && item.queueIndex >= 0) {
            sendCommand("playQueueIndex", { index: item.queueIndex });
          } else if (item.videoId || item.playlistId) {
            sendCommand("playTrack", {
              videoId: item.videoId || "",
              playlistId: item.playlistId || ""
            });
          }
          showToast("Playing: " + (item.title || "Track"));
        })
      );
    });
  }

  refreshQueueBtn.addEventListener("click", requestQueue);

  // --- Search ---

  function triggerSearch() {
    const query = searchInput.value.trim();
    if (!query) {
      searchResultsEl.innerHTML = "";
      searchStatus.textContent = "Type a song or artist to search YouTube Music";
      searchStatus.classList.remove("hidden");
      return;
    }

    activeSearchReqId = makeReqId("search");
    searchStatus.textContent = "Searching YouTube Music...";
    searchStatus.classList.remove("hidden");
    searchResultsEl.innerHTML = "";

    const sent = sendCommand("search", {
      query,
      filter: activeFilter,
      requestId: activeSearchReqId
    });

    if (!sent) {
      searchStatus.textContent = "Not connected to PC server.";
      return;
    }

    clearTimeout(searchTimeout);
    const reqCopy = activeSearchReqId;
    searchTimeout = setTimeout(() => {
      if (activeSearchReqId === reqCopy) {
        searchStatus.textContent = "Search timed out. Make sure music.youtube.com is open and refreshed.";
      }
    }, 10000);
  }

  function renderSearchResults(msg) {
    if (msg.requestId && msg.requestId !== activeSearchReqId) return;
    clearTimeout(searchTimeout);

    if (msg.error) {
      searchStatus.textContent = msg.error;
      searchStatus.classList.remove("hidden");
      searchResultsEl.innerHTML = "";
      return;
    }

    const list = Array.isArray(msg.results) ? msg.results : [];
    if (list.length === 0) {
      searchStatus.textContent = "No playable results found.";
      searchStatus.classList.remove("hidden");
      searchResultsEl.innerHTML = "";
      return;
    }

    searchStatus.classList.add("hidden");
    searchResultsEl.innerHTML = "";

    list.forEach((item) => {
      searchResultsEl.appendChild(
        createTrackRow(item, () => {
          sendCommand("playTrack", {
            videoId: item.videoId,
            playlistId: item.playlistId || ""
          });
          showToast("Playing: " + (item.title || "Track"));
          switchTab("player");
        })
      );
    });
  }

  searchInput.addEventListener("input", () => {
    clearSearchBtn.classList.toggle("hidden", searchInput.value.length === 0);
  });

  clearSearchBtn.addEventListener("click", () => {
    searchInput.value = "";
    clearSearchBtn.classList.add("hidden");
    searchResultsEl.innerHTML = "";
    searchStatus.textContent = "Type a song or artist to search YouTube Music";
    searchStatus.classList.remove("hidden");
    searchInput.focus();
  });

  searchForm.addEventListener("submit", (e) => {
    e.preventDefault();
    searchInput.blur();
    triggerSearch();
  });

  filterChips.forEach((chip) => {
    chip.addEventListener("click", () => {
      activeFilter = chip.dataset.filter || "all";
      filterChips.forEach((c) => c.classList.toggle("active", c === chip));
      if (searchInput.value.trim()) {
        triggerSearch();
      }
    });
  });

  // --- Playback & Volume Controls ---

  function handlePlayPauseClick() {
    // Optimistic instant toggle so UI responds with 0ms lag
    playback.isPlaying = !playback.isPlaying;
    playback.syncedAt = performance.now();
    optimisticPlayUntil = performance.now() + 450;
    renderPlayback();
    sendCommand("playPause");
  }

  playPauseBtn.addEventListener("click", handlePlayPauseClick);
  prevBtn.addEventListener("click", () => sendCommand("prev"));
  nextBtn.addEventListener("click", () => sendCommand("next"));
  miniPlayPauseBtn.addEventListener("click", handlePlayPauseClick);
  miniNextBtn.addEventListener("click", () => sendCommand("next"));

  seekBar.addEventListener("pointerdown", () => { isSeeking = true; });
  seekBar.addEventListener("touchstart", () => { isSeeking = true; }, { passive: true });

  seekBar.addEventListener("input", () => {
    const val = Number(seekBar.value);
    currentTimeEl.textContent = formatTime(val);
    const pct = playback.duration > 0 ? (val / playback.duration) * 100 : 0;
    updateSliderFill(seekBar, pct, "#ffffff");
  });

  function commitSeek() {
    if (!isSeeking) return;
    isSeeking = false;
    const target = Number(seekBar.value);
    playback.currentTime = target;
    playback.syncedAt = performance.now();
    sendCommand("seek", { time: target });
  }

  seekBar.addEventListener("change", commitSeek);
  seekBar.addEventListener("pointerup", commitSeek);
  seekBar.addEventListener("touchend", commitSeek);

  // PC Master Volume (2% step)
  muteBtn.addEventListener("click", () => sendCommand("toggleMute"));
  volDownBtn.addEventListener("click", () => sendCommand("volumeStep", { delta: -2 }));
  volUpBtn.addEventListener("click", () => sendCommand("volumeStep", { delta: 2 }));

  volumeSlider.addEventListener("pointerdown", () => { isDraggingVolume = true; });
  volumeSlider.addEventListener("touchstart", () => { isDraggingVolume = true; }, { passive: true });

  function pushVolumeThrottled(val) {
    const now = performance.now();
    if (now - lastVolumeSend >= 45) {
      lastVolumeSend = now;
      sendCommand("setVolume", { value: val });
    } else {
      clearTimeout(pendingVolumeTimer);
      pendingVolumeTimer = setTimeout(() => {
        lastVolumeSend = performance.now();
        sendCommand("setVolume", { value: Number(volumeSlider.value) });
      }, 50);
    }
  }

  volumeSlider.addEventListener("input", () => {
    const val = Number(volumeSlider.value);
    volumeValue.textContent = `${val}%`;
    updateSliderFill(volumeSlider, val, "#ff2e55");
    pushVolumeThrottled(val);
  });

  function endVolumeDrag() {
    if (!isDraggingVolume) return;
    isDraggingVolume = false;
    clearTimeout(pendingVolumeTimer);
    sendCommand("setVolume", { value: Number(volumeSlider.value) });
  }

  volumeSlider.addEventListener("change", endVolumeDrag);
  volumeSlider.addEventListener("pointerup", endVolumeDrag);
  volumeSlider.addEventListener("touchend", endVolumeDrag);

  // --- WebSocket Connection ---

  function connect() {
    if (reconnectTimer) {
      clearTimeout(reconnectTimer);
      reconnectTimer = null;
    }

    const proto = location.protocol === "https:" ? "wss:" : "ws:";
    const wsUrl = `${proto}//${location.host}/ws?role=phone`;
    setStatus("err", "Connecting...");

    try {
      ws = new WebSocket(wsUrl);
    } catch (_) {
      scheduleReconnect();
      return;
    }

    ws.onmessage = (event) => {
      try {
        const msg = JSON.parse(event.data);
        if (msg.type === "state") {
          const wasExt = extensionConnected;
          extensionConnected = Boolean(msg.extensionConnected);
          if (msg.playback) {
            const keepOptimistic = performance.now() < optimisticPlayUntil;
            playback = {
              title: msg.playback.title || "",
              artist: msg.playback.artist || "",
              artwork: msg.playback.artwork || "",
              isPlaying: keepOptimistic ? playback.isPlaying : Boolean(msg.playback.isPlaying),
              currentTime: Number(msg.playback.currentTime) || 0,
              duration: Number(msg.playback.duration) || 0,
              nextTitle: msg.playback.nextTitle || "",
              nextArtist: msg.playback.nextArtist || "",
              syncedAt: performance.now()
            };
          }
          renderPlayback();
          if (msg.systemVolume) {
            renderVolume(msg.systemVolume.volume, msg.systemVolume.muted);
          }
          if (extensionConnected && !wasExt) {
            if (activeTab === "home") requestHomeFeed();
            if (activeTab === "queue") requestQueue();
          }
        } else if (msg.type === "systemVolume") {
          renderVolume(msg.volume, msg.muted);
        } else if (msg.type === "searchResults") {
          renderSearchResults(msg);
        } else if (msg.type === "homeFeed") {
          renderHomeFeed(msg);
        } else if (msg.type === "queueState") {
          renderQueue(msg);
        }
      } catch (_) {}
    };

    ws.onclose = () => {
      setStatus("err", "Reconnecting...");
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
    }, 1500);
  }

  document.addEventListener("visibilitychange", () => {
    if (!document.hidden && (!ws || ws.readyState !== WebSocket.OPEN)) {
      connect();
    }
  });

  updateSliderFill(seekBar, 0, "#ffffff");
  updateSliderFill(volumeSlider, 50, "#ff2e55");
  requestAnimationFrame(tickProgress);
  connect();
})();
