const GITHUB_REPO = "sadabx/TOST";
const API_URL = `https://api.github.com/repos/${GITHUB_REPO}/releases/latest`;
const RELEASES_PAGE = `https://github.com/${GITHUB_REPO}/releases/latest`;

// Asset pattern matching
const assetPatterns = {
  appimage: /^TOST-[\w.-]+-x86_64\.AppImage$/i,
  portable: /^TOST-[\w.-]+-linux-x64\.tar\.gz$/i,
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

// Fetch latest release and wire download buttons
async function initReleases() {
  const statusEl = document.querySelector("[data-release-status]");
  const versionEl = document.querySelector("[data-release-version]");
  const recommendedBtn = document.querySelector("[data-recommended-download]");

  try {
    const response = await fetch(API_URL, {
      headers: { Accept: "application/vnd.github+json" },
    });

    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const release = await response.json();
    const assets = Array.isArray(release.assets) ? release.assets : [];

    if (versionEl) {
      versionEl.textContent = release.tag_name || "v2.0";
      versionEl.href = release.html_url || RELEASES_PAGE;
    }

    const resolved = {};
    Object.entries(assetPatterns).forEach(([key, pattern]) => {
      const matched = assets.find((a) => pattern.test(a.name));
      if (matched && matched.browser_download_url) {
        resolved[key] = matched.browser_download_url;
        const link = document.querySelector(`[data-asset="${key}"]`);
        if (link) {
          link.href = matched.browser_download_url;
        }
      }
    });

    // Default recommended link to AppImage if found, else latest release page
    if (recommendedBtn) {
      recommendedBtn.href = resolved.appimage || release.html_url || RELEASES_PAGE;
    }

    if (statusEl) {
      statusEl.textContent = "Verified from GitHub Releases";
    }
  } catch (err) {
    console.warn("Could not load latest GitHub release:", err);
    if (statusEl) {
      statusEl.textContent = "Direct downloads available on GitHub Releases";
    }
  }
}

document.addEventListener("DOMContentLoaded", () => {
  initClipboard();
  initReleases();
});
