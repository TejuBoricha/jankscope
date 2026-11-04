namespace Jankscope.Core.Tests;

/// <summary>The weakest claim wins when data from two sources is combined (HC-9; review 1a, finding 6).</summary>
public class ProvenanceCombineTests
{
    [Theory]
    [InlineData(DataProvenance.Measured, DataProvenance.Simulated, DataProvenance.Simulated)]
    [InlineData(DataProvenance.Simulated, DataProvenance.Measured, DataProvenance.Simulated)]
    [InlineData(DataProvenance.Measured, DataProvenance.Fixture, DataProvenance.Fixture)]
    [InlineData(DataProvenance.Measured, DataProvenance.Assumed, DataProvenance.Assumed)]
    [InlineData(DataProvenance.Measured, DataProvenance.Inferred, DataProvenance.Inferred)]
    [InlineData(DataProvenance.Inferred, DataProvenance.Assumed, DataProvenance.Assumed)]
    [InlineData(DataProvenance.Fixture, DataProvenance.Simulated, DataProvenance.Simulated)]
    [InlineData(DataProvenance.Measured, DataProvenance.Measured, DataProvenance.Measured)]
    [InlineData(DataProvenance.None, DataProvenance.Measured, DataProvenance.Measured)]
    [InlineData(DataProvenance.Simulated, DataProvenance.None, DataProvenance.Simulated)]
    [InlineData(DataProvenance.None, DataProvenance.None, DataProvenance.None)]
    public void The_weakest_claim_wins(DataProvenance first, DataProvenance second, DataProvenance expected)
    {
        Assert.Equal(expected, ProvenanceRules.Combine(first, second));
    }

    [Fact]
    public void Combining_never_produces_something_that_can_be_exported_as_a_measurement_when_an_input_could_not()
    {
        foreach (var a in Enum.GetValues<DataProvenance>())
        {
            foreach (var b in Enum.GetValues<DataProvenance>())
            {
                var combined = ProvenanceRules.Combine(a, b);
                if ((!a.MayBeExportedAsMeasurement() && a != DataProvenance.None) || (!b.MayBeExportedAsMeasurement() && b != DataProvenance.None))
                {
                    Assert.False(combined.MayBeExportedAsMeasurement(), $"{a} + {b} gave {combined}");
                }
            }
        }
    }

    [Fact]
    public void A_value_this_code_does_not_know_is_treated_as_the_weakest_claim()
    {
        var unknown = (DataProvenance)99;
        Assert.Equal(unknown, ProvenanceRules.Combine(DataProvenance.Measured, unknown));
        Assert.False(ProvenanceRules.Combine(DataProvenance.Measured, unknown).MayBeExportedAsMeasurement());
    }
}
