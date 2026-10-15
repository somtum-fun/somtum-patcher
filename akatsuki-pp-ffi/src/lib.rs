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

/// Vanilla osu!/taiko/catch/mania: ppy's current pp algorithm via rosu-pp (community port kept in
/// sync with lazer's `osu-performance`). This is what the website shows for a submitted play.
fn calculate_ppy_performance(
    beatmap_bytes: &[u8],
    mode: u32,
    mods: u32,
    max_combo: u32,
    accuracy: f32,
    miss_count: u32,
    passed_objects: Option<u32>,
) -> CalculatePerformanceResult {
    let mut beatmap = match RosuBeatmap::from_bytes(beatmap_bytes) {
        Ok(b) => b,
        Err(_) => return CalculatePerformanceResult::default(),
    };
    let game_mode = match mode {
        0 => RosuGameMode::Osu,
        1 => RosuGameMode::Taiko,
        2 => RosuGameMode::Catch,
        3 => RosuGameMode::Mania,
        _ => return CalculatePerformanceResult::default(),
    };
    // Convert the map in place when the mode asked for isn't the map's native one (osu! → taiko,
    // etc.). ConvertStatus::Incompatible leaves the map alone — pp from a vanilla osu! map in taiko
    // mode is still produced; it just won't match the game's own numbers for an invalid combo.
    beatmap.convert_in_place(game_mode);
    let mut calc = RosuPerformance::new(&beatmap)
        .mods(mods)
        .combo(max_combo)
        .misses(miss_count)
        .accuracy(accuracy as f64);
    if let Some(passed) = passed_objects {
        calc = calc.passed_objects(passed);
    }
    let attrs = calc.calculate();
    CalculatePerformanceResult { pp: attrs.pp(), stars: attrs.stars() }
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
) -> CalculatePerformanceResult {
    let mut calc = AkatsukiOsuPP::new(beatmap)
        .mods(mods)
        .combo(max_combo as usize)
        .misses(miss_count as usize)
        .accuracy(accuracy);
    if let Some(passed) = passed_objects {
        calc = calc.passed_objects(passed as usize);
    }
    calc.calculate().into()
}

/// The patcher's in-game pp counter. Routes vanilla plays through ppy's rosu-pp (what the website
/// uses) and osu! Relax plays through akatsuki's osu_2019 (what the stable Relax leaderboard uses).
/// Function name kept for ABI compatibility with existing patcher-ui builds; the dispatch inside is
/// the only behaviour change.
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
    let bytes = beatmap_bytes.as_slice();
    let passed = passed_objects.into_option();

    // osu! + Relax → akatsuki's osu_2019 (vanilla osu! doesn't carry RX, so this is the only path
    // that stays on akatsuki). Everything else — vanilla osu!/taiko/catch/mania — goes through
    // ppy's rosu-pp so the counter matches the website.
    if mode == 0 && (mods & MODS_RELAX) != 0 {
        match AkatsukiBeatmap::from_bytes(bytes) {
            Ok(bm) => calculate_akatsuki_relax_performance(&bm, mods, max_combo, accuracy, miss_count, passed),
            Err(_) => CalculatePerformanceResult::default(),
        }
    } else {
        calculate_ppy_performance(bytes, mode, mods, max_combo, accuracy, miss_count, passed)
    }
}

pub fn my_inventory() -> Inventory {
    InventoryBuilder::new()
        .register(extra_type!(CalculatePerformanceResult))
        .register(function!(calculate_akatsuki_from_bytes))
        .inventory()
}
