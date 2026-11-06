const GITHUB_REPO = "sadabx/TOST";
const TOST_RELEASE_API = `https://api.github.com/repos/${GITHUB_REPO}/releases/latest`;
const TOST_RELEASE_PAGE = `https://github.com/${GITHUB_REPO}/releases/latest`;

// Extended patterns to dynamically handle Windows and Linux assets
const assetPatterns = {
  "win-setup": /^TOST-.*win.*-Setup\.exe$/i,
  "win-portable": /^TOST-.*win.*-Portable\.zip$/i,
  appimage: /^TOST-[\w.-]+-x86_64\.AppImage$/i,
  "linux-portable": /^TOST-[\w.-]+-linux-x64\.tar\.gz$/i,
  arch: /^tost-[\w.-]+-1-x86_64\.pkg\.tar\.zst$/i,
  deb: /^tost_[\w.-]+_amd64\.deb$/i,
};

// Clipboard copy helper
function initClipboard() {
  const copyButtons = document.querySelectorAll("[data-copy]");
  copyButtons.forEach((btn) => {
    btn.addEventListener("click", async () => {
      const textToCopy = btn.getAttribute("data-copy");
      if (!textToCopy) return;

      try {
        await navigator.clipboard.writeText(textToCopy);
        const originalText = btn.innerHTML;
        btn.classList.add("copied");
        btn.innerHTML = `<i class="fa-solid fa-check"></i> Copied!`;
        setTimeout(() => {
          btn.classList.remove("copied");
          btn.innerHTML = originalText;
        }, 2200);
      } catch (err) {
        console.error("Clipboard copy failed:", err);
      }
    });
  });
}

// Platform Detection
async function detectPlatform() {
  if (navigator.userAgentData) {
    try {
      const hints = await navigator.userAgentData.getHighEntropyValues(["architecture"]);
      if (hints.architecture === "arm") return "unsupported";
      const platform = navigator.userAgentData.platform.toLowerCase();
      if (platform.includes("win")) return "windows";
      if (platform.includes("linux")) return "linux";
    } catch {
      /* fallback to User Agent */
    }
  }

  const ua = navigator.userAgent.toLowerCase();
  if (/arm|aarch64/.test(ua)) return "unsupported";
  if (ua.includes("windows")) return "windows";
  if (ua.includes("linux") && !ua.includes("android")) return "linux";

  return "unsupported";
}

function updateRecommendation(platform, resolvedAssets) {
  const copy = document.querySelector("[data-recommendation-copy]");
  const link = document.querySelector("[data-recommended-download]");
  const label = document.querySelector("[data-recommended-label]");

  if (!copy || !link || !label) return;

  // Highlight platform card
  document.querySelectorAll("[data-platform-card]").forEach((card) => {
    const isDetected = card.dataset.platformCard === platform;
    card.classList.toggle("is-detected", isDetected);
    const badge = card.querySelector("[data-platform-badge]");
    if (badge) badge.hidden = !isDetected;
  });

  if (platform === "windows") {
    copy.textContent = "Recommended for your Windows device:";
    label.textContent = "Download Windows Setup";
    link.href = resolvedAssets["win-setup"] || TOST_RELEASE_PAGE;
    return;
  }

  if (platform === "linux") {
    copy.textContent = "Recommended for your Linux device:";
    label.textContent = "Download Linux AppImage";
    link.href = resolvedAssets.appimage || TOST_RELEASE_PAGE;
    return;
  }

  copy.textContent = "TOST provides native x64 builds for Windows and Linux:";
  label.textContent = "View Available Downloads";
  link.href = TOST_RELEASE_PAGE;
}

// Fetch latest release and wire download buttons
async function loadRelease() {
  const platform = await detectPlatform();
  const statusEl = document.querySelector("[data-release-status]");
  const releaseLink = document.querySelector("[data-release-link]");

  try {
    const response = await fetch(TOST_RELEASE_API, {
      headers: { Accept: "application/vnd.github+json" },
    });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);

    const release = await response.json();
    const assets = Array.isArray(release.assets) ? release.assets : [];
    const resolved = {};

    Object.entries(assetPatterns).forEach(([key, pattern]) => {
      const asset = assets.find((candidate) => pattern.test(candidate.name));
      if (asset?.browser_download_url) {
        resolved[key] = asset.browser_download_url;
        const link = document.querySelector(`[data-release-asset="${key}"]`);
        if (link) link.href = asset.browser_download_url;
      }
    });

    if (releaseLink && release.html_url) {
      releaseLink.href = release.html_url;
      releaseLink.textContent = release.tag_name || "Latest release";
    }

    if (statusEl) statusEl.textContent = "Verified from GitHub Releases";
    updateRecommendation(platform, resolved);
  } catch (error) {
    console.warn("Release fetch failed:", error);
    if (statusEl) statusEl.textContent = "Direct downloads on GitHub Releases";
    updateRecommendation(platform, {});
  }
}

document.addEventListener("DOMContentLoaded", () => {
  initClipboard();
  loadRelease();
});
