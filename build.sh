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

# --- 4. UI (single patcher-ui.exe) -------------------------------------------
"$DOTNET" build patcher-ui/OsuPatcher.UI/OsuPatcher.UI.csproj -c Release -p:SomtumServer="${SomtumServer:-somtum}"

echo
echo "Done: patcher-ui/OsuPatcher.UI/bin/Release/patcher-ui.exe"
