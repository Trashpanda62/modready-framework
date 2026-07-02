// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield
// Management Group.

using System;

namespace Bannerlord.UIExtenderEx.Attributes;

/// <summary>
/// Marks a method on a ViewModelMixin class as a data source method.
/// Only methods carrying this attribute are exposed to Gauntlet bindings
/// on the target ViewModel.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class DataSourceMethodAttribute : Attribute { }
