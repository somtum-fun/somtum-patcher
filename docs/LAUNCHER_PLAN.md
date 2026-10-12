# Somtum Launcher — build plan for a successor AI

Target audience: an AI agent asked to build this launcher. Humans can also read it. This document is the *contract* — read it before writing code, follow it when writing code, update it when the spec drifts.

Nothing in this document is urgent. The current single-exe `somtum-patcher.exe` ships RX/AP + pkms for stable only, which already covers the vast majority of installs. The launcher is a UX improvement; ship it when you have time to do it carefully, not under pressure.

## Status (2026-10-11): 1.0.0 built

Built in `~/somtum-dev/somtum-launcher` (its own repo, not this one). Where it differs from the plan below:

- **No pkms inside the launcher.** Stable downloads the Somtum Patcher from `manifest.patcher` when its version
  changes, unpacks `pkms.dll` + `pkms_injector.exe` from its resources into `%LocalAppData%\somtum\stable\`
  next to it (as `somtum-patcher.exe`, the name the injector looks for), and runs
  `pkms_injector.exe "<osu!.exe>" [--with-patcher]`. pkms updates through the manifest that way; the launcher
  is 175 KB and never needs rebuilding for a pkms release. (No offline fallback for stable: pkms has to be
  current anyway.)
- **Legacy runs the patched builds directly**, not through the hook: they connect to somtum.fun and load their
  pkms by themselves. An update extracts over the build's folder, so songs, scores and settings stay.
- **Lazer** uses TumThai's portable zip (it self-updates with Velopack); a TumThai installed with its own
  installer is used instead.
- **Everything in `%LocalAppData%\somtum\`** (not roaming `%AppData%`: the clients are hundreds of MB).
- **No run on startup** (decided).
- Not done yet: publishing (`self` entry in `publish.py`), testing on Windows, the Linux version.

## What exists today

- `somtum-patcher.exe` — single-exe WPF launcher that embeds pkms.dll + pkms_injector.exe + the RX/AP patcher runtime, extracts the native payload next to itself on first run, and launches **modern stable osu! (b20260924 et seq.)** with pkms + optional RX/AP injected.
- `osu-tumthai` (lazer) — downloaded separately as `TumThai-<version>-win-Setup.exe` / `-win-Portable.zip` / `-x86_64.AppImage` / `.apk`. Published to `dl.blueskychan.dev/lazer/`.
- `hook/osu!somtum.exe` — launcher for the 75 patched *legacy* decompiled builds (2008–2015). Published to `dl.blueskychan.dev/hook/`.
- 75 patched legacy build zips at `dl.blueskychan.dev/legacy/<build>-<release>.zip`.
- `dl.blueskychan.dev/manifest.json` — authoritative index of everything above. Written by `~/.cache/somtum-dist/tools/publish.py`. The launcher reads this to discover versions and URLs.

## What the launcher must do

One `somtum.exe` that any player downloads once and never has to re-download. It should:

1. **Pick what to launch.** The player chooses between:
   - **Lazer (TumThai)** — the modern osu! client. One row in the UI, one Launch button.
   - **Stable (modern)** — the current obfuscated build (`b20260924.1` as of writing). One row with an RX/AP toggle (checkbox). Launch starts osu!.exe through pkms_injector.
   - **Legacy** — the dropdown of 75 patched decompiled builds. Years 2008–2015. Launch starts the hook. No RX/AP toggle (not supported on legacy).

2. **Keep itself up to date.** Check `https://dl.blueskychan.dev/manifest.json` on open (cached for 30 min in `%AppData%\somtum\manifest.json`). If `self.version > current_version`, offer "Update launcher" that downloads the new `somtum.exe` to the current path and relaunches. **The launcher's own version is a separate `self` entry in manifest.json — add it as part of this work.**

3. **Install client payloads on demand.** The launcher is small (~10 MB — the WPF shell + embedded pkms + patcher runtime). Lazer / hook / legacy builds are downloaded *when the player picks them*, extracted into `%AppData%\somtum\<kind>\`, kept across launches. First launch of Lazer downloads the installer and runs it in `--install` mode; subsequent launches skip the download.

4. **Not break the current flow.** Players who already have the current `somtum-patcher.exe` keep working: embed the modern-stable launch path verbatim from `somtum-patcher.exe` into the launcher so launching Stable from the new UI is identical to the current `patcher-ui.exe` → `pkms_injector.exe --with-patcher` flow.

5. **Choose RX/AP for stable only.** The RX/AP toggle is only visible when "Stable" is selected. Both Lazer and Legacy hide it.

## Not in scope

- A settings page beyond "RX/AP on/off" and "run on login". No skin picker, no mod picker, no account manager.
- Downloading and launching ppy.sh client builds (nothing on dl. is ppy's binary; we have decompiled rebuilds).
- Hosting beatmaps / replays. The launcher launches the game; the game handles content.
- Any osu!-trademark surface. The window title, taskbar entry, app name are all "Somtum" or "Somtum Launcher", never "osu!somtum" / "osu!TumThai". See [the trademark memory](~/.claude/projects/-home-mindthenerdy-somtum-dev/memory/osu-trademark-cd.md).

## Architecture

Keep it tight. One WPF project. One MVVM screen with a left-side rail (Lazer / Stable / Legacy / Settings) and a right-side panel per selection. Fluent-ish styling, consistent with the current `somtum-patcher.exe` MainWindow.

```
somtum-launcher/
├── Somtum.Launcher.csproj      # net472, WpfApp, embeds everything
├── App.xaml, App.xaml.cs       # Startup; extracts embedded resources on first run
├── MainWindow.xaml, .cs        # Shell with 4 tabs
├── Tabs/
│   ├── LazerTab.xaml, .cs      # Install state + Launch
│   ├── StableTab.xaml, .cs     # RX/AP toggle + Launch (hosts the existing patcher-ui launch flow)
│   ├── LegacyTab.xaml, .cs     # Build picker (groups by year), Launch, download-on-demand
│   └── SettingsTab.xaml, .cs   # Launcher auto-start, update channel, download path
├── Services/
│   ├── Manifest.cs             # Fetches + parses manifest.json; cached
│   ├── Downloader.cs           # Streams HTTP with resume + sha-check against manifest size
│   ├── Installer.cs            # Per-kind install into %AppData%\somtum\{lazer,legacy,hook}
│   ├── Launcher.cs             # Spawns the right process per selection
│   └── SelfUpdater.cs          # Compares self.version, downloads new exe, atomic swap + restart
└── Payload/                    # Build-time: pkms.dll + pkms_injector.exe + the 3 patcher DLLs
```

**Reuse, don't rewrite:**
- The `InjectorCore` + `App.ExtractNativePayload` from `osu-somtum-patcher/patcher-ui/OsuPatcher.UI/` is the Stable launch flow. Copy it in verbatim (same package, same embedded resource names).
- The RX/AP toggle wiring from the current MainWindow.xaml.cs.
- The `OsuPatcher.Runtime` project is unchanged — it's embedded the same way.

**New code to write:**
- `Manifest.cs` — 150 lines, HTTP + System.Text.Json.
- `Downloader.cs` — 200 lines, HttpClient streaming + a small progress reporter.
- `SelfUpdater.cs` — see §5 below. Trickiest piece: can't overwrite running exe on Windows, so download beside it and move the running exe aside at relaunch.
- The 4 tab views + ViewModels. Each < 300 lines XAML + code-behind.

Total new code: ~2500 lines plus XAML. Should take a focused 2–3 days.

## Dependencies

- `net472` target (same as current patcher — WPF single-file support is simpler on Framework than Core).
- No new NuGet packages beyond what the current patcher-ui uses. Specifically: **do not** pull in a packaging framework like Squirrel or Velopack for self-update — the one-exe swap is simpler to write by hand than to configure. Velopack is already used for lazer; keep it there, don't adopt it here.
- Keep the single-exe property: everything embedded as `EmbeddedResource`, extracted at startup. See `OsuPatcher.UI.csproj` for the pattern.

## manifest.json additions

Current shape at the time of writing — do not reflow unless you know what you're doing. Add a new top-level `self` key:

```json
{
  "self": {
    "version": "1.0",
    "windows_x64": { "url": "https://dl.blueskychan.dev/launcher/somtum-1.0.exe", "size": 10240000 }
  },
  "lazer": { ... existing shape ... },
  "patcher": { ... existing shape ... },
  "anticheat": { ... existing shape ... },
  "hook": { ... existing shape ... },
  "legacy": [ ... existing shape ... ]
}
```

Add the publish step in `~/.cache/somtum-dist/tools/publish.py`: `put` the launcher exe and populate `manifest['self']`.

## Install locations

```
%AppData%\somtum\
├── manifest.json              # 30-min-cached copy of the server manifest
├── lazer\                     # Full TumThai install (unpacked Velopack installer)
├── stable\                    # EMPTY — Stable uses the embedded flow + the player's own osu! folder
├── legacy\
│   ├── b20151228.3\           # One subdir per legacy build the player has opened
│   │   └── osu!.exe + the patched DLLs from the dl zip
│   └── (etc.)
└── hook\                      # The hook binary used by Legacy launches
```

Stable **does not** install osu! for the player — the player installs osu! normally (ppy's installer) and the launcher just drives their existing `osu!.exe`. The current `patcher-ui.exe` does this via `Config.OsuPath` lookup; keep the same mechanism.

## Self-update, specifically

Windows won't let a running `.exe` overwrite itself. Mechanism:

1. On update: download `somtum-<new>.exe` to `%LocalAppData%\somtum\pending\somtum.exe`.
2. Verify size matches `manifest.self.windows_x64.size`.
3. Spawn a tiny helper (write a sibling `somtum-updater.exe` next to the running launcher, or do it as a `cmd /c` one-liner) that:
   a. waits for the current launcher PID to exit,
   b. moves the pending exe over the running exe's path,
   c. relaunches the new exe.
4. The current launcher exits immediately after spawning the helper.

The one-liner approach keeps the launcher install footprint small (no updater exe shipped). Pattern:
```
cmd /c "timeout /t 2 >nul & move /y pending\somtum.exe <launcher-path> & start <launcher-path>"
```

Test this on a locked file (Explorer has the exe open) before shipping — the move will fail silently if someone's got the launcher's icon locked for preview.

## UX details that matter

- **Window chrome.** Match the current patcher: borderless, drag-anywhere title bar, close/minimise only. Rounded corners if WPF effort is low; square if not.
- **Dark theme by default.** Light theme optional. Use the existing patcher's color palette (`MainWindow.xaml` resource dictionary) so launcher + patcher feel like one product.
- **Progress during download.** Show percentage + MB/s + ETA. Don't hide behind a spinner; players downloading a 300 MB lazer install want to see it moving.
- **Error states inline.** If manifest fetch fails, show "Can't reach dl.blueskychan.dev — using cached entries from <date>" and let the player launch whatever's already installed. Don't modal-error on startup.
- **No telemetry.** No analytics, no "report problem" button that uploads anything. Players share screenshots on Discord; that's how we hear about problems.

## Testing checklist (hand this to the human after the AI finishes)

- [ ] First-run install: fresh Windows VM, download `somtum.exe`, launch, see all 4 tabs populated from manifest.
- [ ] Stable launch: install osu! normally, click Launch (RX off), game starts with pkms attached, server sees handshake + flag.
- [ ] Stable launch with RX: toggle on, Launch, verify RX patcher applies.
- [ ] Lazer first-run: Lazer tab shows "Install" button; click → downloads + runs installer; subsequent launches show "Launch".
- [ ] Legacy picker: open Legacy tab, pick a 2013 build, Launch → downloads the zip to `%AppData%\somtum\legacy\<build>\`, extracts, launches via the hook.
- [ ] Self-update: push a `self.version` bump in manifest.json, open the launcher, confirm the "Update" button appears, click → launcher relaunches on the new version.
- [ ] Offline: block dl.blueskychan.dev with a hosts entry, open launcher, confirm cached fallback works.
- [ ] Trademark audit: nothing in the running UI or taskbar reads "osu!somtum" / "osu!TumThai"; the app is Somtum Launcher (naming the lazer client "TumThai" is fine). See [the C&D memory](~/.claude/projects/-home-mindthenerdy-somtum-dev/memory/osu-trademark-cd.md).

## Decisions (from the human, 2026-10-11)

- **No run on startup.** The launcher never starts with Windows.
- **Legacy remembers the last build** the player launched, and preselects it next time (store it in
  `%AppData%\somtum\settings.json`). The player can still pick another.
- **pkms updates through the manifest**, not only with new launcher releases. `manifest.json` already has an
  `anticheat` entry (`version`, `url`, `size`, written by `publish.py` as `anticheat/pkms-<version>.zip`). On
  open, if `anticheat.version` is newer than the installed one, the launcher downloads the zip, checks its
  size, and swaps it into `%AppData%\somtum\anticheat\` before the next Stable launch. The launcher still
  embeds a pkms as the fallback for a first run without the network. Same hard rules: no pkms-specific
  errors or logs.

## Linux

Players on Linux get a launcher too. Build it on [osu-winello](https://github.com/NelloKudo/osu-winello)
(GPL-3.0) for everything that needs Wine, rather than shipping our own Wine setup: it installs a patched
wine-osu, a ready osu! prefix and the `osu-wine` command, and already supports private servers
(`osu-wine --devserver <address>`, or `POST_LAUNCH_ARGS` in its config).

What the Linux launcher does per tab:

- **Lazer (TumThai):** native, no Wine. Download the AppImage from `manifest.lazer.linux_x64` into
  `~/.local/share/somtum/lazer/`, mark it executable, run it. TumThai updates itself afterwards (Velopack).
- **Stable (modern):** needs osu-winello. If `osu-wine` isn't on `PATH`, show how to install it (link + the
  one command for the player's distro) instead of installing it ourselves. Launch with
  `osu-wine --devserver somtum.fun`. pkms + RX/AP: start `pkms_injector.exe` inside the same prefix with
  `osu-wine --wine <path>\pkms_injector.exe --with-patcher`. **Untested:** whether pkms's injection and checks
  work under wine-osu at all, and whether the server should treat Wine sessions differently. Test this
  first; if it doesn't work, Stable on Linux ships without RX/AP until it does, and the server's pkms
  requirement needs a decision for Linux players.
- **Legacy (2008-2015):** the hook and the patched builds are .NET Framework 2/4 programs. Run them with
  `osu-wine --wine <path>\osu!somtum.exe` in osu-winello's prefix if it has the .NET they need, otherwise
  in a separate prefix (`~/.local/share/somtum/legacy-prefix`, set up with winetricks `dotnet20`/`dotnet40`).
  **Untested** too; check a 2008, a 2012 and a 2015 build.

Shape: one native app, not a Windows .exe under Wine. Either a small GTK/Avalonia app or, simplest, a
`somtum` shell script with a TUI (`somtum lazer`, `somtum stable`, `somtum legacy b20130303`, `somtum update`)
that reads the same `manifest.json`. Same rules as Windows: `~/.local/share/somtum/` only, no telemetry,
self-update from a `self.linux_x64` entry in the manifest. Don't fork osu-winello or bundle it; call it.
Optionally, later: ask osu-winello upstream whether they'd take a `--somtum` option like their `--akatsuki`.

## Hard rules

- **No files under osu!'s install directory** unless the player clicks Launch and we need them there for injection. The launcher's install footprint is `%AppData%\somtum\` and nothing else.
- **Never ship a config change that affects an existing player silently.** New defaults only take effect on fresh installs.
- **The launcher never shows pkms-specific errors to the player.** If pkms rejected the session, show "Launch failed — please try again" and point them at Discord. The launcher does not know which pkms module fired. See [the memory on this](~/.claude/projects/-home-mindthenerdy-somtum-dev/memory/anticheat-layout.md).
- **The launcher never writes pkms-related log files.** pkms.dll already compiles log calls out in release — don't add a C# shim that writes "pkms attached ok" or similar. Zero disk evidence.
