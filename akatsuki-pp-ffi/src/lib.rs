use akatsuki_pp_1_0_1::{ // bancho.py's fork — the osu! Relax (osu_2019) formula lives here.
    osu_2019::{stars::OsuPerformanceAttributes as AkatsukiOsuPerfAttrs, OsuPP as AkatsukiOsuPP},
    Beatmap as AkatsukiBeatmap,
};
use rosu_pp::{Beatmap as RosuBeatmap, Performance as RosuPerformance};
use rosu_pp::model::mode::GameMode as RosuGameMode;
use interoptopus::{
    extra_type, ffi_function, ffi_type, function,
    patterns::{option::FFIOption, slice::FFISlice},
    Inventory, InventoryBuilder,
};
use std::panic::{catch_unwind, AssertUnwindSafe};

/// osu! mods bitmask: Relax. Vanilla osu! plays never carry it; Relax plays on osu! use akatsuki's
/// osu_2019 formula rather than ppy's.
const MODS_RELAX: u32 = 128;

#[ffi_type]
#[repr(C)]
#[derive(Clone, Default, PartialEq)]
pub struct CalculatePerformanceResult {
    pub pp: f64,
    pub stars: f64,
}

impl std::fmt::Display for CalculatePerformanceResult {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(f, "pp: {}, stars: {}", self.pp, self.stars)
    }
}

impl From<AkatsukiOsuPerfAttrs> for CalculatePerformanceResult {
    fn from(attrs: AkatsukiOsuPerfAttrs) -> Self {
        CalculatePerformanceResult { pp: attrs.pp, stars: attrs.difficulty.stars }
    }
}

/// Runs `f`, and turns a panic into the default result. These functions are called straight from
/// osu!: a panic unwinding into C# (or aborting at the `extern "C"` boundary, Rust ≥ 1.81) kills the
/// game with no error at all. A pp of 0 for one frame is the better failure.
fn guarded<T: Default>(f: impl FnOnce() -> T) -> T {
    catch_unwind(AssertUnwindSafe(f)).unwrap_or_default()
}

fn rosu_mode(mode: u32) -> Option<RosuGameMode> {
    match mode {
        0 => Some(RosuGameMode::Osu),
        1 => Some(RosuGameMode::Taiko),
        2 => Some(RosuGameMode::Catch),
        3 => Some(RosuGameMode::Mania),
        _ => None,
    }
}

fn finite(pp: f64, stars: f64) -> CalculatePerformanceResult {
    let ok = |v: f64| if v.is_finite() && v >= 0.0 { v } else { 0.0 };
    CalculatePerformanceResult { pp: ok(pp), stars: ok(stars) }
}

/// Vanilla osu!/taiko/catch/mania from accuracy only (the old export's inputs). ppy's current pp
/// algorithm via rosu-pp; maps that are too suspicious to calculate give 0.
fn calculate_ppy_performance(
    beatmap_bytes: &[u8],
    mode: u32,
    mods: u32,
    max_combo: u32,
    accuracy: f32,
    miss_count: u32,
    passed_objects: Option<u32>,
) -> CalculatePerformanceResult {
    let beatmap = match RosuBeatmap::from_bytes(beatmap_bytes) {
        Ok(b) => b,
        Err(_) => return CalculatePerformanceResult::default(),
    };
    if beatmap.check_suspicion().is_err() {
        return CalculatePerformanceResult::default();
    }
    let Some(game_mode) = rosu_mode(mode) else { return CalculatePerformanceResult::default() };
    let mut calc = RosuPerformance::new(&beatmap)
        .mode_or_ignore(game_mode)
        .mods(mods)
        .lazer(false)
        .combo(max_combo)
        .misses(miss_count)
        .accuracy(accuracy as f64);
    if let Some(passed) = passed_objects {
        calc = calc.passed_objects(passed);
    }
    match calc.checked_calculate() {
        Ok(attrs) => finite(attrs.pp(), attrs.stars()),
        Err(_) => CalculatePerformanceResult::default(),
    }
}

/// osu! Relax: akatsuki's osu_2019 formula (what Akatsuki's own server uses). Vanilla Relax doesn't
/// exist on the website; this is for the in-game Relax panel on osu!stable.
fn calculate_akatsuki_relax_performance(
    beatmap: &AkatsukiBeatmap,
    mods: u32,
    max_combo: u32,
    accuracy: f32,
    miss_count: u32,
    passed_objects: Option<u32>,
    hits: Option<(u32, u32, u32)>,
) -> CalculatePerformanceResult {
    // Order matters: accuracy() turns the accuracy into 300/100/50 counts over the objects it knows
    // of, so passed_objects and misses must be set first. Setting them after (as before 1.5.2) spread
    // the whole map's objects over a partial play: pp ran to inf / 1e20 until the replay ended.
    let mut calc = AkatsukiOsuPP::new(beatmap).mods(mods).combo(max_combo as usize);
    if let Some(passed) = passed_objects {
        calc = calc.passed_objects(passed as usize);
    }
    calc = calc.misses(miss_count as usize);
    // The real hit counts when we have them. akatsuki's accuracy() guesses them, and on a short
    // partial play its guess has more 300s + 100s than objects: the usize n50 wraps to ~4 billion
    // and pp goes to inf.
    calc = match hits {
        Some((n300, n100, n50)) => calc.n300(n300 as usize).n100(n100 as usize).n50(n50 as usize),
        None => calc.accuracy(accuracy),
    };
    let r: CalculatePerformanceResult = calc.calculate().into();
    finite(r.pp, r.stars)
}

/// The patcher's old in-game pp entry point (accuracy only, re-parses the map on every call). Kept
/// for ABI compatibility with older Runtime builds; 1.5.1+ uses `pp_map_new` / `pp_calculate`.
#[ffi_function]
#[no_mangle]
pub unsafe extern "C" fn calculate_akatsuki_from_bytes(
    beatmap_bytes: FFISlice<u8>,
    mode: u32,
    mods: u32,
    max_combo: u32,
    accuracy: f32,
    miss_count: u32,
    passed_objects: FFIOption<u32>,
) -> CalculatePerformanceResult {
    guarded(|| {
        let bytes = beatmap_bytes.as_slice();
        let passed = passed_objects.into_option();
        if mode == 0 && (mods & MODS_RELAX) != 0 {
            match AkatsukiBeatmap::from_bytes(bytes) {
                Ok(bm) => calculate_akatsuki_relax_performance(&bm, mods, max_combo, accuracy, miss_count, passed, None),
                Err(_) => CalculatePerformanceResult::default(),
            }
        } else {
            calculate_ppy_performance(bytes, mode, mods, max_combo, accuracy, miss_count, passed)
        }
    })
}

/// A beatmap parsed once per play (1.5.1). The counter recalculates after hits; parsing the whole
/// .osu file every time was most of the cost and memory churn of the old export.
pub struct PpMap {
    bytes: Vec<u8>,
    rosu: Option<RosuBeatmap>,
    akatsuki: Option<Option<AkatsukiBeatmap>>, // parsed on first Relax calculation
}

/// One score, in osu!stable's own terms. Hit counts are the score's Count fields as stable keeps
/// them per mode (catch: 300 = fruits, 100 = droplets, 50 = tiny droplets, katu = missed tiny
/// droplets; mania: geki = MAX, katu = 200).
#[repr(C)]
#[derive(Clone, Copy)]
pub struct PpScore {
    pub mode: u32,
    pub mods: u32,
    pub max_combo: u32,
    pub n300: u32,
    pub n100: u32,
    pub n50: u32,
    pub n_geki: u32,
    pub n_katu: u32,
    pub misses: u32,
    /// Objects judged so far; `u32::MAX` = the whole map (a finished play).
    pub passed_objects: u32,
    /// osu!stable's total score (ScoreV1); 0 = unknown. ppy uses it for osu! scores.
    pub legacy_total_score: u32,
    /// 0..100, only used for Relax (akatsuki's formula takes accuracy, not hit counts).
    pub accuracy: f32,
}

/// Parses a .osu file. Returns null when it can't be parsed or is too suspicious to calculate
/// (aspire-style maps that would stall the counter). Free with `pp_map_free`.
#[no_mangle]
pub unsafe extern "C" fn pp_map_new(bytes: *const u8, len: usize) -> *mut PpMap {
    guarded(|| {
        if bytes.is_null() || len == 0 {
            return std::ptr::null_mut();
        }
        let bytes = std::slice::from_raw_parts(bytes, len).to_vec();
        let rosu = match RosuBeatmap::from_bytes(&bytes) {
            Ok(b) if b.check_suspicion().is_ok() => Some(b),
            _ => None,
        };
        if rosu.is_none() {
            return std::ptr::null_mut();
        }
        Box::into_raw(Box::new(PpMap { bytes, rosu, akatsuki: None }))
    })
}

#[no_mangle]
pub unsafe extern "C" fn pp_map_free(map: *mut PpMap) {
    if !map.is_null() {
        guarded(|| drop(Box::from_raw(map)));
    }
}

/// pp and stars for one score on a parsed map. Never panics into the caller; 0 on any failure.
#[no_mangle]
pub unsafe extern "C" fn pp_calculate(map: *mut PpMap, score: *const PpScore) -> CalculatePerformanceResult {
    guarded(|| {
        if map.is_null() || score.is_null() {
            return CalculatePerformanceResult::default();
        }
        let map = &mut *map;
        let s = *score;
        let passed = if s.passed_objects == u32::MAX { None } else { Some(s.passed_objects) };

        if s.mode == 0 && (s.mods & MODS_RELAX) != 0 {
            let bytes = &map.bytes;
            let bm = map.akatsuki.get_or_insert_with(|| AkatsukiBeatmap::from_bytes(bytes).ok());
            return match bm {
                Some(bm) => {
                    let judged = s.n300 as u64 + s.n100 as u64 + s.n50 as u64 + s.misses as u64;
                    let hits = (judged > 0).then_some((s.n300, s.n100, s.n50));
                    // A partial play is the objects judged so far: keep the two in step.
                    let passed = passed.map(|p| p.min(judged.max(1) as u32).max(1));
                    calculate_akatsuki_relax_performance(bm, s.mods, s.max_combo, s.accuracy, s.misses, passed, hits)
                }
                None => CalculatePerformanceResult::default(),
            };
        }

        let (Some(beatmap), Some(mode)) = (map.rosu.as_ref(), rosu_mode(s.mode)) else {
            return CalculatePerformanceResult::default();
        };
        let mut calc = RosuPerformance::new(beatmap)
            .mode_or_ignore(mode)
            .mods(s.mods)
            .lazer(false)
            .combo(s.max_combo)
            .n300(s.n300)
            .n100(s.n100)
            .n50(s.n50)
            .n_geki(s.n_geki)
            .n_katu(s.n_katu)
            .misses(s.misses);
        if s.legacy_total_score > 0 {
            calc = calc.legacy_total_score(s.legacy_total_score);
        }
        if let Some(passed) = passed {
            calc = calc.passed_objects(passed);
        }
        match calc.checked_calculate() {
            Ok(attrs) => finite(attrs.pp(), attrs.stars()),
            Err(_) => CalculatePerformanceResult::default(),
        }
    })
}

pub fn my_inventory() -> Inventory {
    InventoryBuilder::new()
        .register(extra_type!(CalculatePerformanceResult))
        .register(function!(calculate_akatsuki_from_bytes))
        .inventory()
}
