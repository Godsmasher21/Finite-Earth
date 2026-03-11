# Finite Earth Itch Demo Publish Guide

## Goal

Ship a browser-playable demo build (no backend required), then switch back to online multiplayer later with minimal changes.

## Demo mode behavior (implemented)

In WebGL builds, demo mode is enabled by default:

1. Local/offline simulation starts automatically.
2. No backend/gateway required.
3. Login popup is auto-hidden for demo mode.
4. Universal cycle timer still runs and updates global counters locally.

## Multiplayer override without code changes

You can force multiplayer at runtime using URL query:

1. `?mode=multi` (or `?mode=online`)

Example:

1. `https://yourhost/game/index.html?mode=multi`

Force demo explicitly:

1. `?mode=demo`

## Unity build steps for Itch

1. Open `File -> Build Settings -> WebGL`.
2. Set Compression Format:
   - `Gzip` with decompression fallback on, or `Disabled` for easiest compatibility.
3. Build to a folder like `Builds/itch-demo-webgl`.
4. Zip the WebGL build output contents (must include `index.html` at zip root).
5. Upload zip to Itch.io as an HTML game.
6. In Itch embed settings:
   - Enable “This file will be played in the browser”.
   - Set viewport to match your target (e.g., 1280x720 or 1600x900).

## Reverting to online multiplayer later

Option A (recommended for production):

1. In `WalletSessionController`, set `webGlDemoMode = false`.
2. Keep `allowWebGlModeQueryOverride = true`.
3. Deploy backend + chain + web bridge.

Option B (quick test, no rebuild):

1. Keep current build.
2. Launch with `?mode=multi` and a running backend stack.

## If you want login popup visible in demo

In `AsciiLoginPopupPresenter`:

1. Set `hidePopupInDemoMode = false`.
