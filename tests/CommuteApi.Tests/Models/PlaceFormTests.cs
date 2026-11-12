using CommuteApi.Models;

namespace CommuteApi.Tests.Models;

public sealed class PlaceFormTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(" , ,, ", null)]
    [InlineData("Telstra", "Telstra")]
    [InlineData("  Telstra ,, Highpoint SC ", "Telstra, Highpoint SC")]
    [InlineData("Telstra, telstra, TELSTRA", "Telstra")]
    public void Aliases_are_trimmed_deduplicated_and_rejoined(string? input, string? expected)
    {
        Assert.Equal(expected, PlaceForm.NormaliseAliases(input));
    }

    [Fact]
    public void Applying_a_form_trims_the_name()
    {
        var form = new PlaceForm
        {
            Name = "  Highpoint  ",
            RouteType = TransportMode.Bus,
            StopId = 1,
            RouteId = 2,
        };
        var place = new Place { Name = "" };

        form.ApplyTo(place);

        Assert.Equal("Highpoint", place.Name);
    }
}
