rosu-pp, vendored for the Somtum Patcher's pp counter.

- Source: https://github.com/MaxOhn/rosu-pp branch `pp-update`, commit
  072ed255fa043aba495212c441030dc7a5e61469 (2026-09-22): ppy's 2026 Q2 SR & PP release.
  Only `src/`, `Cargo.toml` and `LICENSE` (MIT); benches, tests and dev-dependencies dropped.
- Local change: `src/osu/performance/calculator.rs`, the reading value's miss penalty uses
  `effective_miss_count + aim_estimated_slider_breaks`, as ppy's `computeReadingValue` does
  (ppy.osu.Game.Rulesets.Osu 2026.730.0, what TomYum computes with). Without it, scores with
  misses came out up to 15% high, most with HD/EZ.
- Drop this folder for the crates.io release once it carries the 2026 Q2 update and the fix.
