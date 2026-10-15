using System;
using System.Threading;
using OsuPatcher.Runtime.Graphics;
using OsuPatcher.Runtime.Resolver;
using OsuPatcher.Runtime.Rosu;
using OsuPatcher.Runtime.Utils;

namespace OsuPatcher.Runtime.Play
{
    /// <summary>
    /// The pp counter of one play (a normal play, a replay, a spectated play, a multiplayer play).
    /// Built when that Player finishes loading and only ever driven by that Player instance, so a
    /// play that fails to set up can't keep showing the previous play's numbers.
    ///
    /// Threads: Tick runs on osu!'s main thread every frame and only reads the score; the pp itself
    /// is worked out on a pool thread (at most one at a time, at most ~4 a second) and the main
    /// thread puts the result on screen: osu!'s sprites aren't touched from another thread.
    /// </summary>
    internal sealed class Performance : IDisposable
    {
        private const int MinIntervalMs = 250;

        public readonly object Owner;
        private readonly PlayState _state;
        private readonly Calculator _calculator;
        private readonly pSpriteText _text;
        private readonly uint _mods;

        private long _lastKey = -1;          // hit counts + combo of the last calculation started
        private int _lastStart = Environment.TickCount - MinIntervalMs;
        private int _busy;                   // 1 while a calculation runs
        private volatile int _pp;            // last result
        private int _shown = -1;             // what the sprite says
        private static int _errorsLogged;

        public Performance(object owner, PlayState state, Calculator calculator, pSpriteText text, uint mods)
        {
            Owner = owner;
            _state = state;
            _calculator = calculator;
            _text = text;
            _mods = mods;
        }

        /// <summary>Main thread, every frame of the owning Player.</summary>
        public void Tick()
        {
            if (_pp != _shown)
            {
                _shown = _pp;
                _text.Text = _shown.ToString();
            }

            var score = _state.Score();
            if (score == null)
                return;

            var s = new PpScore
            {
                Mode = (uint)Convert.ToInt32(_state.PlayMode.GetValue(score)),
                Mods = _mods,
                MaxCombo = (uint)Math.Max(0, (int)_state.MaxCombo.GetValue(score)),
                N300 = PlayState.U(_state.Count300, score),
                N100 = PlayState.U(_state.Count100, score),
                N50 = PlayState.U(_state.Count50, score),
                NGeki = PlayState.U(_state.CountGeki, score),
                NKatu = PlayState.U(_state.CountKatu, score),
                Misses = PlayState.U(_state.CountMiss, score),
            };

            // Recalculate when a judgement or the combo changed (spectator score syncs included),
            // throttled; a change inside the window is picked up on a later frame.
            long key = s.N300 | ((long)s.N100 << 16) | ((long)s.N50 << 32) | ((long)s.Misses << 48);
            key ^= ((long)s.NGeki << 8) ^ ((long)s.NKatu << 24) ^ ((long)s.MaxCombo << 40);
            if (key == _lastKey || Environment.TickCount - _lastStart < MinIntervalMs)
                return;
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
                return;

            _lastKey = key;
            _lastStart = Environment.TickCount;
            s.PassedObjects = PassedObjects(s);
            if (s.Mode == 0 && (s.Mods & 128) != 0)
                s.Accuracy = (float)_state.Accuracy.Invoke(score, null) * 100f;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    // Clamped: a bad value must never show as an int overflow (2147483648).
                    _pp = (int)Math.Round(Math.Min(_calculator.Calculate(s), 99999.0));
                }
                catch (Exception e)
                {
                    // An exception escaping a pool thread ends osu! (.NET 4). Log a few, show the old value.
                    if (Interlocked.Increment(ref _errorsLogged) <= 5)
                        Logger.logFile($"[pp] calculation failed: {e}");
                }
                finally
                {
                    Interlocked.Exchange(ref _busy, 0);
                }
            });
        }

        /// <summary>Objects judged so far, by how osu!stable counts each mode's judgements.</summary>
        private static uint PassedObjects(PpScore s)
        {
            switch (s.Mode)
            {
                case 1: // taiko: geki/katu are the big-note bonus on top of 300/100
                case 2: // catch: 300 fruits + 100 droplets + misses; 50 = tiny droplets, katu = missed tiny droplets
                    return s.N300 + s.N100 + s.Misses;
                case 3: // mania: geki = MAX, katu = 200, all of them notes
                    return s.NGeki + s.N300 + s.NKatu + s.N100 + s.N50 + s.Misses;
                default: // osu!: geki/katu are subsets of 300/100
                    return s.N300 + s.N100 + s.N50 + s.Misses;
            }
        }

        public void Dispose() => _calculator.Dispose();
    }
}
