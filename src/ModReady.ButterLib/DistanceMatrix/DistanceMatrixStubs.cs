// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// Stub declarations for Bannerlord.ButterLib.DistanceMatrix.*. Some
// consumer mods (e.g. battlefield-AI mods, certain economy mods)
// reference these types at type-load. We declare them as empty contracts;
// the CLR's type-load step succeeds. v0.8 may add a real spatial-index
// impl if mods actually call into them.
//
// v0.7.2: added DistanceMatrix<T>, IDistanceMatrixStatic.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

using ModReady.Foundation;

namespace Bannerlord.ButterLib.DistanceMatrix
{
    /// <summary>
    /// Lazily-computed all-pairs distance matrix over a population of T.
    /// Stub. GetDistance returns NaN (NOT 0 -- returning 0 made every
    /// settlement pair "adjacent", silently corrupting any consumer's
    /// distance-based logic; NaN poisons comparisons so the gap is
    /// detectable) and each touch is reported through CompatWarn.
    /// </summary>
    public sealed class DistanceMatrix<T> where T : class
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public float GetDistance(T a, T b)
        {
            CompatWarn.Once("ButterLib.DistanceMatrix", $"DistanceMatrix<{typeof(T).Name}>.GetDistance",
                Assembly.GetCallingAssembly().GetName().Name,
                "spatial index not implemented; returns NaN (was 0, which made every pair adjacent)");
            return float.NaN;
        }

        public IReadOnlyList<T> Owners
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get
            {
                CompatWarn.Once("ButterLib.DistanceMatrix", $"DistanceMatrix<{typeof(T).Name}>.Owners",
                    Assembly.GetCallingAssembly().GetName().Name,
                    "spatial index not implemented; returns an empty list");
                return Array.Empty<T>();
            }
        }
    }

    /// <summary>Static accessor over registered DistanceMatrix instances. Stub.</summary>
    public interface IDistanceMatrixStatic
    {
        DistanceMatrix<T>? Get<T>() where T : class;
    }
}
