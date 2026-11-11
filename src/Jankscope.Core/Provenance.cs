namespace Jankscope.Core;

/// <summary>Where a value came from. Fixture and simulated data can never be exported as a measurement (HC-9).</summary>
public enum DataProvenance
{
    /// <summary>No data: the entry only states availability or a reason.</summary>
    None,
    Measured,
    Inferred,
    Assumed,
    Simulated,
    Fixture,
}

public static class ProvenanceRules
{
    public static bool MayBeExportedAsMeasurement(this DataProvenance provenance) =>
        provenance is DataProvenance.Measured or DataProvenance.Inferred;

    /// <summary>
    /// The provenance of data made from two sources: the weakest claim wins, so a series that mixes real and simulated frames is simulated.
    /// <see cref="DataProvenance.None"/> ("no data") gives way to anything. Order, weakest last: Measured, Inferred, Assumed, Fixture, Simulated.
    /// </summary>
    public static DataProvenance Combine(DataProvenance first, DataProvenance second) => Strength(first) >= Strength(second) ? first : second;

    private static int Strength(DataProvenance provenance) => provenance switch
    {
        DataProvenance.None => 0,
        DataProvenance.Measured => 1,
        DataProvenance.Inferred => 2,
        DataProvenance.Assumed => 3,
        DataProvenance.Fixture => 4,
        DataProvenance.Simulated => 5,
        _ => 5, // a value this code does not know is treated as the weakest claim, never as a measurement
    };

    /// <summary>Throws unless the data may be presented as a measurement. Call at every export boundary.</summary>
    public static void RequireExportableAsMeasurement(this DataProvenance provenance)
    {
        if (!provenance.MayBeExportedAsMeasurement())
        {
            throw new InvalidOperationException(
                $"Data with provenance '{provenance}' cannot be exported as a measurement.");
        }
    }
}
