// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// MCM.Common.MCMException. Consumer mods (e.g. some setting wrappers)
// reference this type at type-load. v0.7.2 adds the empty exception class
// so the CLR's type-load step doesn't throw -- mods that actually throw
// or catch this exception just see standard Exception-derived behavior.

using System;

namespace MCM.Common
{
    /// <summary>
    /// MCM-specific exception used by the upstream BUTR MCM. Stub.
    /// </summary>
    public class MCMException : Exception
    {
        public MCMException() { }
        public MCMException(string message) : base(message) { }
        public MCMException(string message, Exception inner) : base(message, inner) { }
    }
}
