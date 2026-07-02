// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// ElegantPairHelper -- the Szudzik elegant pairing function. Used by
// DistanceMatrix and a few other ButterLib subsystems to produce a stable
// integer key from a pair of integer IDs (e.g. two MBObjectManager ids).
// The pairing is symmetric: ElegantPair(a, b) != ElegantPair(b, a) unless
// the symmetric overload is used.
//
// Pairing function: f(x, y) = x >= y ? x*x + x + y : y*y + x   (Szudzik)

namespace Bannerlord.ButterLib.Common.Helpers;

public static class ElegantPairHelper
{
    /// <summary>
    /// Szudzik's elegant pairing. Maps two non-negative integers to a
    /// unique non-negative integer.
    /// </summary>
    public static long Pair(int x, int y)
    {
        return x >= y ? (long)x * x + x + y : (long)y * y + x;
    }

    /// <summary>
    /// Symmetric pairing -- order of (x, y) doesn't matter. Useful when the
    /// caller doesn't want to sort the inputs explicitly.
    /// </summary>
    public static long SymmetricPair(int x, int y)
    {
        var (a, b) = x <= y ? (x, y) : (y, x);
        return Pair(a, b);
    }
}
