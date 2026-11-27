const dot = document.getElementById("dot");
const statusText = document.getElementById("statusText");
const trackEl = document.getElementById("track");
const openBtn = document.getElementById("openConnect");

openBtn.addEventListener("click", () => {
  chrome.tabs.create({ url: "http://127.0.0.1:8765/connect" });
});

async function checkStatus() {
  try {
    const res = await fetch("http://127.0.0.1:8765/api/info");
    if (!res.ok) throw new Error("Server unreachable");
    const info = await res.json();
    if (info.extensionConnected) {
      dot.classList.add("ok");
      statusText.textContent = "Connected to PC server";
      trackEl.textContent = "Phone URL: " + info.phoneUrl;
    } else {
      dot.classList.remove("ok");
      statusText.textContent = "Server running — open YT Music";
      trackEl.textContent = "Reload music.youtube.com if already open";
    }
  } catch (_) {
    dot.classList.remove("ok");
    statusText.textContent = "PC server not running";
    trackEl.textContent = "Open KuroBeat to start the server";
  }
}

checkStatus();
