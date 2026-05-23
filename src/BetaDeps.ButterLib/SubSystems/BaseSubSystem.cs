// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.

using System;

using BetaDeps.Foundation;

namespace Bannerlord.ButterLib.SubSystems;

/// <summary>
/// Convenience base for ISubSystem implementations. Concrete subsystems
/// override <see cref="OnEnable"/> and <see cref="OnDisable"/> and let this
/// base handle idempotency + diag logging.
/// </summary>
public abstract class BaseSubSystem : ISubSystem
{
    private bool _enabled;
    private readonly object _gate = new();

    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }

    public bool IsEnabled => _enabled;
    public virtual bool CanBeDisabled => true;

    public void Enable()
    {
        lock (_gate)
        {
            if (_enabled) return;
            try
            {
                OnEnable();
                _enabled = true;
                DiagLog.Log("SubSystem", $"{Id} enabled");
            }
            catch (Exception ex)
            {
                DiagLog.LogCaught("SubSystem", $"{Id}.Enable", ex);
            }
        }
    }

    public void Disable()
    {
        lock (_gate)
        {
            if (!_enabled) return;
            if (!CanBeDisabled)
            {
                DiagLog.Log("SubSystem", $"{Id} cannot be disabled at runtime");
                return;
            }
            try
            {
                OnDisable();
                _enabled = false;
                DiagLog.Log("SubSystem", $"{Id} disabled");
            }
            catch (Exception ex)
            {
                DiagLog.LogCaught("SubSystem", $"{Id}.Disable", ex);
            }
        }
    }

    protected abstract void OnEnable();
    protected abstract void OnDisable();
}
