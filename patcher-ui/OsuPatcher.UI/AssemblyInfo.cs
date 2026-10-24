using System.Reflection;
using System.Runtime.InteropServices;

// Explicit VERSIONINFO/RT_VERSION metadata so the shipped patcher-ui.exe
// doesn't look like a bare 0.0.0.0 unknown-publisher binary to Defender ML.
// A missing or blank FileDescription/Company/Product is a textbook benign-vs-
// malware signal — stock Defender heuristics ("Wacatac.B!ml" and friends)
// weight "has reasonable VERSIONINFO" heavily toward the benign class.
// Values here show up under File Properties → Details in Explorer and in
// `signtool verify /v /pa` output; keep them stable across releases so the
// SmartScreen reputation signal associates with the same identity string.
//
// GenerateAssemblyInfo stays false in the csproj so this file is the single
// source of truth; a second SDK-generated AssemblyInfo would produce a
// duplicate-attribute compile error.

[assembly: AssemblyTitle("Somtum Patcher")]
[assembly: AssemblyProduct("Somtum Patcher")]
[assembly: AssemblyDescription("osu! anti-cheat loader and gameplay patcher for blueskychan.dev / somtum.fun")]
[assembly: AssemblyCompany("blueskychan.dev / osu!somtum")]
[assembly: AssemblyCopyright("Copyright (C) blueskychan.dev / osu!somtum contributors")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: ComVisible(false)]

// 1.5.x track = Patcher releases. Bumped per ship; see publish.py PATCHER_VERSION
// and ~/.cache/somtum-dist/tools/publish.py for the authoritative counter.
[assembly: AssemblyVersion("1.5.30.0")]
[assembly: AssemblyFileVersion("1.5.30.0")]
[assembly: AssemblyInformationalVersion("1.5.30-dev")]
