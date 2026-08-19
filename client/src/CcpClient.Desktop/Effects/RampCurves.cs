namespace CcpClient.Desktop.Effects;

/// <summary>
/// The shape applied to a 0..1 intensity-ramp progress value — WPF's <c>RampCurve</c>
/// (<c>Helpers/RampCurves.cs:17-25</c>, declared in the <c>Models</c> namespace beside it).
///
/// <para><b>Persisted by ordinal</b>, exactly as upstream persists it: "Stored by ordinal like the
/// other enum settings here; missing = Linear = unchanged legacy behaviour"
/// (<c>CCP.Core/Models/AppSettings.cs:2631-2639</c>). <see cref="System.Text.Json"/> writes an enum
/// as its numeric value by default, so a document written by this build and a document written by
/// WPF agree on the wire, and a document with no field at all deserializes to
/// <see cref="Linear"/> — which is the value that changes nothing.</para>
///
/// <para><b>An ordinal outside this set is not an error and is not corrected.</b>
/// <see cref="RampCurves.ApplyCurve"/> falls through to linear for anything it does not recognise,
/// which is upstream's own <c>default:</c> arm (<c>Helpers/RampCurves.cs:69-71</c>) and its combo
/// box's <c>_ =&gt; 0</c> (<c>Features/IntensityRampFeatureControl.xaml.cs:54</c>). Preserving the
/// unknown value rather than rewriting it is the persistence contract's unknown-member rule applied
/// to a value instead of a member: a newer build's curve survives a round trip through this one.</para>
/// </summary>
public enum RampCurve
{
    /// <summary>Straight line. The default, and the only value that leaves progress untouched.</summary>
    Linear = 0,

    /// <summary>Slow start, strong finish (<c>p³</c>).</summary>
    EaseIn = 1,

    /// <summary>Fast start, gentle finish.</summary>
    EaseOut = 2,

    /// <summary>Smoothstep: gentle at both ends, quickest through the middle.</summary>
    SCurve = 3,

    /// <summary>Very back-loaded: almost nothing until late, then a hard surge.</summary>
    Exponential = 4,
}

/// <summary>
/// Re-shapes a linear intensity-ramp progress value — a direct port of
/// <c>Helpers/RampCurves.cs:47-73</c>, formula for formula.
///
/// <para><b>Why the arithmetic is copied rather than reinterpreted.</b> This is one of the places
/// the constitution's "behaviour-visible formulas keep parity" rule bites literally: the curve is
/// the difference between a ramp that creeps for fifty minutes and then surges, and one that climbs
/// evenly, and a user who picked <c>Exponential</c> upstream must get the same climb here. The
/// endpoints are preserved by every arm (0 → 0, 1 → 1), which is what lets the ramp still reach
/// exactly its configured maximum at completion no matter which curve is chosen — upstream states
/// that property in as many words at <c>Helpers/RampCurves.cs:39-42</c>.</para>
///
/// <para><b>Upstream shares this helper between two ramp systems</b> — the manual Intensity Ramp
/// ported here and the scripted preset session's own ramp
/// (<c>Services/Session/SessionEngine.UpdateRampingValues</c>). The port has only the first: the
/// scripted session is explicitly not ported (see <see cref="Session.SessionEngine"/>'s remarks),
/// so this class has exactly one caller here where it has two upstream.</para>
/// </summary>
public static class RampCurves
{
    /// <summary>
    /// Map a linear 0..1 <paramref name="progress"/> to an eased 0..1 value. Input is clamped to
    /// 0..1 first (<c>Helpers/RampCurves.cs:49</c>), so a caller that has not clamped its own
    /// progress cannot drive the curve past its endpoints.
    /// </summary>
    public static double ApplyCurve(double progress, RampCurve curve)
    {
        var p = Math.Clamp(progress, 0.0, 1.0);
        switch (curve)
        {
            case RampCurve.EaseIn:
                // Slow start, strong finish (RampCurves.cs:53).
                return p * p * p;

            case RampCurve.EaseOut:
                // Fast start, gentle finish (RampCurves.cs:56-58).
                var inv = 1.0 - p;
                return 1.0 - (inv * inv * inv);

            case RampCurve.SCurve:
                // Smoothstep (RampCurves.cs:61-62).
                return p * p * (3.0 - (2.0 * p));

            case RampCurve.Exponential:
                // Very back-loaded (RampCurves.cs:65-67). k is upstream's, and it is what decides
                // how late the surge lands, so it is a constant with a citation rather than a knob.
                const double k = 3.0;
                return (Math.Exp(k * p) - 1.0) / (Math.Exp(k) - 1.0);

            case RampCurve.Linear:
            default:
                return p;
        }
    }
}
