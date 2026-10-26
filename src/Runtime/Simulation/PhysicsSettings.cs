namespace BrutalBreakout.Simulation;

/// <summary>Validated immutable tuning, replaced uniformly at owner boundaries. Clock topology changes require reset. No implicit defaults.</summary>
public sealed class PhysicsSettings
{
    public double UpdateHz { get; }
    public int Substeps { get; }
    public int MaxCatchUpSteps { get; }
    public float ContactHertz { get; }
    public float ContactDampingRatio { get; }
    public double StepSeconds { get; }

    public PhysicsSettings(double updateHz, int substeps, int maxCatchUpSteps,
        double contactHertz, double contactDampingRatio)
    {
        if (!double.IsFinite(updateHz) || updateHz <= 0)
            throw new ArgumentOutOfRangeException(nameof(updateHz));
        if (substeps <= 0) throw new ArgumentOutOfRangeException(nameof(substeps));
        if (maxCatchUpSteps <= 0) throw new ArgumentOutOfRangeException(nameof(maxCatchUpSteps));
        double step = 1 / updateHz;
        float nativeStep = (float)step;
        float substep = nativeStep / substeps;
        // Box2D computes the step, substep and their inverses in native float arithmetic.
        if (!PositiveFinite(nativeStep) || !PositiveFinite(1f / nativeStep) ||
            !PositiveFinite(substep) || !PositiveFinite(1f / substep))
            throw new ArgumentOutOfRangeException(nameof(updateHz), "Native step/substep and inverses must be positive finite floats.");
        ContactHertz = Contact(contactHertz, nameof(contactHertz));
        ContactDampingRatio = Contact(contactDampingRatio, nameof(contactDampingRatio));
        UpdateHz = updateHz;
        Substeps = substeps;
        MaxCatchUpSteps = maxCatchUpSteps;
        StepSeconds = step;
    }

    private static bool PositiveFinite(float value) => float.IsFinite(value) && value > 0;
    private static float Contact(double value, string name)
    {
        float native = (float)value;
        if (!double.IsFinite(value) || value < 0 || !float.IsFinite(native) || (value > 0 && native == 0))
            throw new ArgumentOutOfRangeException(name, "Contact tuning must be nonnegative and representable as a finite float.");
        return native;
    }
}
