#!/usr/bin/env bash
# Builds patcher-ui.exe on Linux.
#
# Requirements (Fedora package names):
#   - cargo + rust-std-static-i686-pc-windows-gnu + mingw32-gcc
#       dnf install cargo rust-std-static-i686-pc-windows-gnu mingw32-gcc
#     (with rustup instead: rustup target add i686-pc-windows-gnu)
#   - Microsoft build of the .NET SDK with WindowsDesktop support (for WPF XAML).
#     Fedora's dotnet 8 package ships it; otherwise install with:
#       curl -sL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0
set -euo pipefail
cd "$(dirname "$0")"

# --- preflight ---------------------------------------------------------------
command -v cargo >/dev/null || { echo "error: cargo not found" >&2; exit 1; }
command -v i686-w64-mingw32-gcc >/dev/null || {
    echo "error: i686-w64-mingw32-gcc not found (dnf install mingw32-gcc)" >&2; exit 1; }

# Find a dotnet whose SDK includes Microsoft.NET.Sdk.WindowsDesktop.
# realpath resolves symlink wrappers (e.g. Fedora's /usr/bin/dotnet -> /usr/lib64/dotnet)
# so we look for the SDK under the *real* install root, not next to the launcher.
DOTNET=""
for candidate in "${DOTNET_ROOT:-}/dotnet" "$HOME/.dotnet/dotnet" "$(command -v dotnet || true)"; do
    [ -x "$candidate" ] || continue
    root="$(dirname "$(realpath "$candidate")")"
    # Match the actual WPF targets file, not just the SDK dir — Fedora's system dotnet
    # ships an empty Microsoft.NET.Sdk.WindowsDesktop folder (WPF is Windows-only).
    if compgen -G "$root/sdk/*/Sdks/Microsoft.NET.Sdk.WindowsDesktop/targets/Microsoft.NET.Sdk.WindowsDesktop.targets" >/dev/null; then
        DOTNET="$candidate"
        export DOTNET_ROOT="$root"
        break
    fi
done
[ -n "$DOTNET" ] || {
    echo "error: no .NET SDK with WindowsDesktop support found." >&2
    echo "Install the Microsoft build of the .NET 8 SDK:" >&2
    echo "  curl -sL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0" >&2
    exit 1; }

# --- 1. Rust FFI DLL ---------------------------------------------------------
cargo build --release --target i686-pc-windows-gnu --manifest-path akatsuki-pp-ffi/Cargo.toml

# --- 2. Runtime DLL ----------------------------------------------------------
# Pass SomtumServer explicitly on the command line so a shell that forgot to
# `export` it (or a parent build script's subshell-env quirk) still produces
# the right target. Directory.Build.props defines SOMTUM_DEV iff this is 'dev'.
"$DOTNET" build patcher/OsuPatcher.Runtime/OsuPatcher.Runtime.csproj -c Release -p:SomtumServer="${SomtumServer:-somtum}"

# --- 3. Copy DLLs into the UI payload ----------------------------------------
cp patcher/OsuPatcher.Runtime/bin/Release/OsuPatcher.Runtime.dll patcher-ui/OsuPatcher.UI/Payload/
cp patcher/OsuPatcher.Runtime/bin/Release/0Harmony.dll           patcher-ui/OsuPatcher.UI/Payload/
cp akatsuki-pp-ffi/target/i686-pc-windows-gnu/release/akatsuki_pp_ffi.dll patcher-ui/OsuPatcher.UI/Payload/

# --- Phase gate -------------------------------------------------------------
# The UI csproj embeds pkms.dll + pkms_injector.exe (native anti-cheat) too, so the UI
# project only builds once those files have been written into Payload\ as well. The
# orchestrator (pad-krapow-mu-sap-injector/scripts/build-bundle.sh) runs this script
# with --prepare, builds pkms, drops the files, and re-runs with --ui-only. A plain
# `build.sh` without args still finishes the UI build when invoked by a dev who has
# a working pkms set staged locally.
PHASE="${1:-full}"
if [ "$PHASE" = "--prepare" ]; then
    echo
    echo "Payload prepared. Now build pkms and drop pkms.dll + pkms_injector.exe into"
    echo "  patcher-ui/OsuPatcher.UI/Payload/"
    echo "then re-run: $0 --ui-only"
    exit 0
fi

if [ "$PHASE" != "--ui-only" ] && [ "$PHASE" != "full" ]; then
    echo "unknown phase: $PHASE (expected --prepare | --ui-only | <empty>)" >&2
    exit 1
fi

for native in pkms.dll pkms_injector.exe; do
    [ -f "patcher-ui/OsuPatcher.UI/Payload/$native" ] || {
        echo "error: patcher-ui/OsuPatcher.UI/Payload/$native missing — build pkms first (see pad-krapow-mu-sap-injector/scripts/build-bundle.sh), or run '$0 --prepare' and continue from there." >&2
        exit 1
    }
done

# --- 4. UI (single patcher-ui.exe) -------------------------------------------
"$DOTNET" build patcher-ui/OsuPatcher.UI/OsuPatcher.UI.csproj -c Release -p:SomtumServer="${SomtumServer:-somtum}"

echo
echo "Done: patcher-ui/OsuPatcher.UI/bin/Release/patcher-ui.exe"
