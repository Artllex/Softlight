# Firefox integration — Softlight 2.1.0

Stable application baseline tested on Firefox and YouTube. The companion extension is unsigned and currently loaded temporarily.

## Setup

1. Install Softlight, or extract the complete portable ZIP and run `Register-Firefox.ps1` in PowerShell. The installer registers the native host automatically. Portable users must register again after moving the folder.
2. Exit any older instance, start `Softlight.exe` and enable the filter.
3. In Firefox 142+, open `about:debugging#/runtime/this-firefox`, choose **Load Temporary Add-on**, and select `firefox/manifest.json`.
4. Reload the video page. The Windows list shows **Player** and **Page** only when a player is detected.

The extension must be loaded again after Firefox restarts. It is not signed or published on Mozilla Add-ons. Its toolbar badge indicates host connectivity, not successful player detection.

## Behavior

The largest visible HTML video/player is measured separately from the rest of its Firefox window. Video pixels are excluded from page analysis, and each region receives one uniform dimming factor. YouTube uses the player container including controls; other sites use the video bounds.

Video cuts above 22 brightness percentage points in either direction immediately apply target dimming on detection. Detection also considers the last 120 ms, capturing cuts spread across multiple frames; a short 150 ms follow-through tracks the end of the transition. Fluctuations within +/-10 pp are smoothed slowly. Speed scales that response (2.5-second time constant at 2x); Sudden change controls ordinary windows only. Manual strength changes apply immediately to video. Capture and presentation latency still apply.

**Flash protection** in the tray menu displays captured Page/Player pixels with their respective dimming already applied, keeping the preceding processed frame visible until the next one is ready. This reduces leaks during scene and tab changes within already covered areas, but adds image latency and can reduce visible frame rate. Turn it off to compare with the live transparent-mask mode. No extension update is required for this desktop rendering option. See [pipeline details and limitations](docs/PIPELINE.md).

Expand **Live graph** for ten seconds of Brightness (yellow, pre-filter average) and Dim (mint). The panel redraws approximately 30 times per second while visible, displaying batches of measurements timestamped by the engine. Auto follows the active application, selecting its detected player when available or the whole window otherwise. Clicking the Softlight panel retains the last observed application. History is retained across window, Firefox tab and player changes; a vertical dashed line marks each context transition using its original event time rather than the next UI refresh. Use Freeze to inspect transitions. Missing measurements create gaps. These are relative image measurements, not hardware brightness or nits.

## Limitations and privacy

HTTP/HTTPS pages only. Cross-origin iframe players, picture-in-picture, multiple simultaneous players, shadow DOM, irregular/rotated video and protected content are not comprehensively supported. Only the visible viewport contributes; the center-point occlusion check does not reconstruct arbitrary CSS clipping. Scroll updates follow animation frames; stationary geometry uses a heartbeat. Disconnected geometry expires after 500 ms.

The extension sends geometry, visibility, a generation number, activation timing, browser window ID and the tab title to a local native host. Titles establish the browser-window binding; no frames or URLs are sent. Communication uses a pipe restricted to the current Windows user. No network service is used.

To remove integration, remove the extension and run `Register-Firefox.ps1 -Remove`. Existing settings remain compatible with the previous app.

## Validation

Native shader/HDR/settings and video-response tests passed. Content tests cover coordinates, clipping, hidden tabs, update coalescing and cleanup. Live user-profile YouTube check: Player brightness 35%, dimming 52%; Page brightness 6%, dimming 0%. Other sites and edge cases have not been comprehensively validated.
