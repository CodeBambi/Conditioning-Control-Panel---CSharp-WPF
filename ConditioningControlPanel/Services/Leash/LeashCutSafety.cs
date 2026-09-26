using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Leash;

/// <summary>
/// STUB. Owned by the "cutsafety" lane, which replaces this file at merge. Cutting the leash turns
/// Strict Lock off, the panic key on, ends any remote session and ends Lockdown, locally and before
/// the server answers. <see cref="LeashService.CutAsync"/> calls <see cref="Apply"/> first thing.
/// </summary>
public static class LeashCutSafety
{
    // TODO(cutsafety): the real safety steps. Same shape as the real one: what it released.
    public static IReadOnlyList<string> Apply() => Array.Empty<string>();
}
