// ModReady.Harmony -- PlaybackPool
//
// The per-context track list plus its selection mode and cursor. Two
// consumers:
//   - SoundtrackXmlGenerator (PSAI path) enumerates Tracks to emit one
//     PSAI Segment per file; PSAI then does its own variance selection
//     within the generated theme, so the cursor is unused there.
//   - SettlementMusicManager (Engine.Music path, workstream C3) calls
//     Next() to advance the loose-file channel track by track.
//
// Sequential walks the list in order, wrapping at the end. Shuffle yields
// a random permutation, reshuffled each time the cursor wraps (so every
// track plays once per cycle, no immediate repeats across a cycle boundary
// beyond chance). Empty pool -> Next() returns null and callers no-op,
// which is the "vanilla plays" graceful path.
//
// Original work. MIT, copyright 2026 Maxfield Management Group.

using System;
using System.Collections.Generic;

namespace ModReady.Harmony.Music;

public enum PlaybackMode
{
    Shuffle = 0,
    Sequential,
}

public sealed class PlaybackPool
{
    private readonly List<string> _tracks;
    private readonly Random _rng;
    private int[] _order;
    private int _cursor;

    public MusicContext Context { get; }
    public PlaybackMode Mode { get; set; }

    /// <summary>Absolute paths to the audio files in this pool (load order).</summary>
    public IReadOnlyList<string> Tracks => _tracks;

    public int Count => _tracks.Count;
    public bool IsEmpty => _tracks.Count == 0;

    public PlaybackPool(MusicContext context, IEnumerable<string> tracks, PlaybackMode mode = PlaybackMode.Shuffle, int seed = 0)
    {
        Context = context;
        Mode = mode;
        _tracks = new List<string>(tracks ?? Array.Empty<string>());
        // Seed defaults to a context-stable value so shuffle is reproducible
        // within a session without depending on wall-clock (handy for logs and
        // for the eventual deterministic-replay tests). Caller may override.
        _rng = new Random(seed != 0 ? seed : 0x5EED ^ (int)context);
        _order = BuildOrder();
        _cursor = 0;
    }

    private int[] BuildOrder()
    {
        var n = _tracks.Count;
        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;
        if (Mode == PlaybackMode.Shuffle)
        {
            // Fisher-Yates.
            for (int i = n - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
        }
        return order;
    }

    /// <summary>
    /// Next track's absolute path, or null when the pool is empty. Advances
    /// the cursor; reshuffles on wrap when in Shuffle mode. Thread-safety is
    /// the caller's concern (settlement manager calls this from the main tick).
    /// </summary>
    public string? Next()
    {
        if (_tracks.Count == 0) return null;
        var idx = _order[_cursor++];
        // Wrap AFTER consuming the last index so _cursor is always a valid
        // "next" position that Peek() and the following Next() agree on. The
        // old lead-wrap reshuffled at start-of-cycle, so Peek (which can't
        // reshuffle) and Next disagreed exactly at the boundary.
        if (_cursor >= _order.Length)
        {
            _cursor = 0;
            if (Mode == PlaybackMode.Shuffle) _order = BuildOrder();
        }
        return _tracks[idx];
    }

    /// <summary>Peek the track Next() would return without advancing.</summary>
    public string? Peek()
    {
        if (_tracks.Count == 0) return null;
        return _tracks[_order[_cursor]];
    }

    /// <summary>Reset the cursor to the start of the current order.</summary>
    public void Reset() => _cursor = 0;

    /// <summary>Switch play mode at runtime (the Options > Sound UI calls this so a
    /// Shuffle/Sequential change takes effect on the next pick). Rebuilds the play
    /// order and rewinds the cursor. No-op if the mode is unchanged. PSAI contexts
    /// ignore this (their cursor is unused); it matters for the settlement path.</summary>
    public void ApplyMode(PlaybackMode mode)
    {
        if (Mode == mode) return;
        Mode = mode;
        _order = BuildOrder();
        _cursor = 0;
    }
}
