// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
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

namespace Bannerlord.ButterLib.DistanceMatrix
{
    /// <summary>
    /// Lazily-computed all-pairs distance matrix over a population of T.
    /// Stub -- consumer-mod calls return zero / empty.
    /// </summary>
    public sealed class DistanceMatrix<T> where T : class
    {
        public float GetDistance(T a, T b) => 0f;
        public IReadOnlyList<T> Owners => Array.Empty<T>();
    }

    /// <summary>Static accessor over registered DistanceMatrix instances. Stub.</summary>
    public interface IDistanceMatrixStatic
    {
        DistanceMatrix<T>? Get<T>() where T : class;
    }
}
