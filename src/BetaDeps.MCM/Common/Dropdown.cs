// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// MCM.Common.Dropdown<T> -- alias for DropdownDefault<T>. Several mainline
// MCM consumers (FluidCombat, FasterTime, BetterSmithing, IDontCare,
// XorberaxLegacy, etc.) reference `MCM.Common.Dropdown<T>` directly. Both
// type names exist in upstream BUTR-MCM and are interchangeable in practice.
// We provide Dropdown<T> here as a thin subclass of DropdownDefault<T> so
// consumer typerefs against the bare name resolve to our DLL.

using System.Collections.Generic;

namespace MCM.Common;

public class Dropdown<T> : DropdownDefault<T>
{
    public Dropdown() : base() { }
    public Dropdown(IEnumerable<T> items, int selectedIndex = 0) : base(items, selectedIndex) { }
}
