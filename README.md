<div align="center">
  <img src="https://osu.somtum.fun/images/layout/somtum-logo.png" alt="somtum logo" width="220"/>
  <h1>Somtum patcher</h1>
  <p>A single-exe injector that patches osu! to connect to <a href="https://somtum.fun">somtum.fun</a>.</p>
</div>

---

## Features

- Single `patcher-ui.exe` — no extra files needed
- Dark borderless UI with auto-inject: detects running osu!, or launches it for you
- In-game settings panel (magic wand icon) with toggles for:
  - Relax patch
  - Autopilot patch
  - Faster transition time
  - Akatsuki performance calculator

## Usage

Download `patcher-ui.exe` from [Releases](../../releases) and run it. That's it.

- If osu! is already running on `somtum.fun`, it injects immediately and closes.
- If not, it launches osu! and injects automatically.

## Build from source

The `.csproj` projects are SDK-style (`net472`), so `dotnet` restores NuGet packages and
builds them on both Windows and Linux — no Visual Studio or `nuget.exe` required.

### Windows

**Requirements**

- .NET SDK 8.0+
- Rust with `i686-pc-windows-msvc` target (`rustup target add i686-pc-windows-msvc`)

```powershell
# 1. Build the Rust FFI DLL
cargo build --release --target i686-pc-windows-msvc --manifest-path akatsuki-pp-ffi\Cargo.toml

# 2. Build the runtime DLL (dotnet restores PackageReferences automatically)
dotnet build patcher\OsuPatcher.Runtime\OsuPatcher.Runtime.csproj -c Release

# 3. Copy DLLs into the UI payload (they get embedded into the single exe)
Copy-Item patcher\OsuPatcher.Runtime\bin\Release\OsuPatcher.Runtime.dll patcher-ui\OsuPatcher.UI\Payload\ -Force
Copy-Item patcher\OsuPatcher.Runtime\bin\Release\0Harmony.dll            patcher-ui\OsuPatcher.UI\Payload\ -Force
Copy-Item akatsuki-pp-ffi\target\i686-pc-windows-msvc\release\akatsuki_pp_ffi.dll patcher-ui\OsuPatcher.UI\Payload\ -Force

# 4. Build the UI (produces a single patcher-ui.exe)
dotnet build patcher-ui\OsuPatcher.UI\OsuPatcher.UI.csproj -c Release
```

Output: `patcher-ui\OsuPatcher.UI\bin\Release\patcher-ui.exe`

### Linux

Run `./build.sh`. It cross-compiles the Rust FFI DLL for Windows and builds the .NET
projects with `dotnet`. **Requirements** (Fedora package names):

- `cargo`, `rust-std-static-i686-pc-windows-gnu`, `mingw32-gcc`
- The **Microsoft build** of the .NET 8 SDK — distro/source-built SDKs strip the
  WindowsDesktop SDK needed to compile WPF. Install with:
  `curl -sL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0`

```bash
./build.sh
```

Output: `patcher-ui/OsuPatcher.UI/bin/Release/patcher-ui.exe`

## Credits

- Patcher runtime source — [remeliah/osu-patcher](https://github.com/remeliah/osu-patcher)
