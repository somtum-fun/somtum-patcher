using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using OsuPatcher.Runtime.Utils;

namespace OsuPatcher.Runtime.Resolver
{
    /// <summary>
    /// What the pp counter reads from osu!, resolved once against osu!'s Player class itself (the
    /// declaring type of the patched Player method) and its Score class.
    ///
    /// Never against the running instance's type: replays, spectating and autoplay run a
    /// ReplayWatcher, multiplayer a PlayerVs, the editor a PlayerTest, all subclasses of Player, and
    /// reflection on a subclass doesn't see Player's private fields (the beatmap) or its statics
    /// (the current score). That is why the counter used to stay at 0 there.
    ///
    /// Nothing here uses an obfuscated name: those differ per build and per stream.
    /// </summary>
    internal sealed class PlayState
    {
        private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static PlayState _cached;
        private static Type _cachedFor;

        public Type PlayerType;
        public FieldInfo CurrentScore;     // static Player.currentScore
        public FieldInfo Beatmap;          // Player.beatmap (private)
        public MethodInfo BeatmapStream;   // Beatmap.GetBeatmapStream()
        public FieldInfo Count300, Count100, Count50, CountGeki, CountKatu, CountMiss;
        public FieldInfo MaxCombo, PlayMode, EnabledMods;
        public MethodInfo Accuracy;        // Score.get_Accuracy (virtual: the mode's own override runs)

        /// <summary>The resolved layout for this Player type, or null (logged once) if osu! changed shape.</summary>
        public static PlayState For(Type playerType)
        {
            if (playerType == null)
                return null;
            if (ReferenceEquals(playerType, _cachedFor))
                return _cached;

            _cachedFor = playerType;
            try
            {
                _cached = Resolve(playerType);
            }
            catch (Exception e)
            {
                _cached = null;
                Logger.logFile($"[pp] resolve failed: {e.GetType().Name}: {e.Message}");
            }
            return _cached;
        }

        private static PlayState Resolve(Type playerType)
        {
            var s = new PlayState { PlayerType = playerType };

            // The current score: Player's static field of a type with the seven ushort counters.
            s.CurrentScore = playerType.GetFields(BindingFlags.Static | Declared)
                .FirstOrDefault(f => UshortFields(f.FieldType).Length >= 6);
            if (s.CurrentScore == null)
                return Fail("Player.currentScore");
            var scoreType = s.CurrentScore.FieldType;

            // The beatmap: Player's instance field whose type has the two Stream getters (audio, then
            // the .osu file). Never a Stream-typed field itself: Score has one (ExtraData).
            s.Beatmap = playerType.GetFields(BindingFlags.Instance | Declared)
                .FirstOrDefault(f => StreamGetters(f.FieldType).Length >= 2);
            if (s.Beatmap == null)
                return Fail("Player.beatmap");
            s.BeatmapStream = BeatmapStreamGetter(s.Beatmap.FieldType);
            if (s.BeatmapStream == null)
                return Fail("Beatmap.GetBeatmapStream");

            // Count300/100/50 from get_Accuracy's formula, CountMiss from get_TotalHits.
            foreach (var m in scoreType.GetMethods(BindingFlags.Instance | Declared))
            {
                if (!m.IsVirtual || !Is(m.ReturnType, typeof(float)) || m.GetParameters().Length != 0)
                    continue;
                var il = m.GetMethodBody()?.GetILAsByteArray();
                if (il != null && ScoreIL.CountsFromAccuracy(il, out var t300, out var t100, out var t50))
                {
                    s.Accuracy = m;
                    s.Count300 = FieldByToken(scoreType, t300);
                    s.Count100 = FieldByToken(scoreType, t100);
                    s.Count50 = FieldByToken(scoreType, t50);
                    break;
                }
            }
            if (s.Count300 == null || s.Count100 == null || s.Count50 == null)
                return Fail("Score counts (get_Accuracy)");

            int tok300 = s.Count300.MetadataToken, tok100 = s.Count100.MetadataToken, tok50 = s.Count50.MetadataToken;
            foreach (var m in scoreType.GetMethods(BindingFlags.Instance | Declared))
            {
                if (!Is(m.ReturnType, typeof(int)) || m.GetParameters().Length != 0)
                    continue;
                var il = m.GetMethodBody()?.GetILAsByteArray();
                int miss = il == null ? 0 : ScoreIL.MissFromTotalHits(il, tok300, tok100, tok50);
                if (miss != 0)
                {
                    s.CountMiss = FieldByToken(scoreType, miss);
                    break;
                }
            }
            if (s.CountMiss == null)
                return Fail("Score.CountMiss (get_TotalHits)");

            // Geki and katu: the next two ushort counters in declaration order (CurrentCombo follows).
            var rest = UshortFields(scoreType)
                .Where(f => f != s.Count300 && f != s.Count100 && f != s.Count50 && f != s.CountMiss)
                .ToArray();
            if (rest.Length < 2)
                return Fail("Score geki/katu");
            s.CountGeki = rest[0];
            s.CountKatu = rest[1];

            var instance = scoreType.GetFields(BindingFlags.Instance | Declared);
            s.MaxCombo = instance.FirstOrDefault(f => Is(f.FieldType, typeof(int)));
            s.PlayMode = instance.FirstOrDefault(f => f.FieldType.IsEnum);
            s.EnabledMods = instance.FirstOrDefault(f => f.FieldType.IsGenericType);
            if (s.MaxCombo == null || s.PlayMode == null || s.EnabledMods == null)
                return Fail("Score combo/mode/mods");

            Logger.logFile("[pp] play state resolved");
            return s;
        }

        private static PlayState Fail(string what)
        {
            Logger.logFile($"[pp] not found: {what}; the pp counter is off for this osu! build");
            return null;
        }

        /// <summary>The field of <paramref name="t"/> (or a base) with this metadata token, or null.</summary>
        private static FieldInfo FieldByToken(Type t, int token)
        {
            for (; t != null; t = t.BaseType)
                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Static | Declared))
                    if (f.MetadataToken == token)
                        return f;
            return null;
        }

        /// <summary>Type equality by name too: also true for types read without loading them (the offline check).</summary>
        private static bool Is(Type t, Type expected) => t == expected || (t != null && t.FullName == expected.FullName);

        private static FieldInfo[] UshortFields(Type t) =>
            t.GetFields(BindingFlags.Instance | Declared).Where(f => Is(f.FieldType, typeof(ushort))).ToArray();

        private static MethodInfo[] StreamGetters(Type t) =>
            t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(m => Is(m.ReturnType, typeof(Stream)) && m.GetParameters().Length == 0)
                .ToArray();

        /// <summary>
        /// GetBeatmapStream is `return GetFileStream(Filename);`: ldarg.0, ldarg.0, ldfld string, call, ret.
        /// Falls back to the second Stream getter (the first is the audio one), as before.
        /// </summary>
        private static MethodInfo BeatmapStreamGetter(Type beatmapType)
        {
            var getters = StreamGetters(beatmapType);
            foreach (var m in getters)
            {
                var il = m.GetMethodBody()?.GetILAsByteArray();
                if (il == null || il.Length != 13)
                    continue;
                if (il[0] != 0x02 || il[1] != 0x02 || il[2] != 0x7B || il[7] != 0x28 || il[12] != 0x2A)
                    continue;
                var field = FieldByToken(beatmapType, BitConverter.ToInt32(il, 3));
                if (field != null && Is(field.FieldType, typeof(string)))
                    return m;
            }
            return getters.Length >= 2 ? getters[1] : null;
        }

        // ---- reading the live score ----------------------------------------------------------

        public object Score() => CurrentScore.GetValue(null);

        public int Mods(object score)
        {
            var obfuscated = EnabledMods.GetValue(score);
            if (obfuscated == null)
                return 0;
            if (_modsValue == null || _modsValue.DeclaringType != obfuscated.GetType())
            {
                var t = obfuscated.GetType();
                var generic = t.GetGenericArguments()[0];
                _modsValue = t.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .FirstOrDefault(m => m.ReturnType == generic && m.GetParameters().Length == 0);
            }
            return _modsValue == null ? 0 : Convert.ToInt32(_modsValue.Invoke(obfuscated, null));
        }
        private MethodInfo _modsValue;

        public static ushort U(FieldInfo f, object o) => (ushort)f.GetValue(o);
    }
}
