using System.Net;
using CommuteApi.Controllers;
using CommuteApi.Models;
using CommuteApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace CommuteApi.Tests.Web;

public sealed class PlacesPagesTests(CommuteApiFactory factory) : IClassFixture<CommuteApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // Tests share one database per class, so every test uses its own place names.
    private static string UniqueName(string prefix) => $"{prefix} {Guid.NewGuid():N}";

    private static Dictionary<string, string> ValidFields(string name) => new()
    {
        ["Name"] = name,
        ["Aliases"] = " Telstra ,, Highpoint SC ",
        ["RouteType"] = "2",
        ["StopId"] = "12345",
        ["RouteId"] = "678",
        ["DirectionId"] = "",
        ["LeadMinutes"] = "25",
    };

    private Task<Place?> FindAsync(string name) =>
        factory.WithDbAsync(db => db.Places.AsNoTracking().SingleOrDefaultAsync(p => p.Name == name));

    [Fact]
    public async Task Creating_a_place_saves_it_and_redirects_to_the_list()
    {
        var name = UniqueName("Highpoint");

        using var response = await _client.PostFormAsync("/Places/Create", "/Places/Create", ValidFields(name));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Places", response.Headers.Location?.OriginalString);

        var saved = await FindAsync(name);
        Assert.NotNull(saved);
        Assert.Equal(TransportMode.Bus, saved.RouteType);
        Assert.Equal(12345, saved.StopId);
        Assert.Equal(678, saved.RouteId);
        Assert.Null(saved.DirectionId);
        Assert.Equal(25, saved.LeadMinutes);
        Assert.Equal("Telstra, Highpoint SC", saved.Aliases);

        var list = await _client.GetStringAsync(new Uri("/Places", UriKind.Relative));
        Assert.Contains(name, list, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_duplicate_name_in_a_different_case_is_rejected_with_a_message()
    {
        var name = UniqueName("Uni");
        using var first = await _client.PostFormAsync("/Places/Create", "/Places/Create", ValidFields(name));
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);

        using var second = await _client.PostFormAsync(
            "/Places/Create", "/Places/Create", ValidFields(name.ToUpperInvariant()));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Contains(PlacesController.DuplicateNameMessage, await second.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var names = await factory.WithDbAsync(db => db.Places.Select(p => p.Name).ToListAsync());
        Assert.Single(names, n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("1")] // tram
    [InlineData("3")] // V/Line
    [InlineData("")]  // not chosen
    public async Task Only_train_and_bus_are_accepted(string routeType)
    {
        var name = UniqueName("Tram stop");
        var fields = ValidFields(name);
        fields["RouteType"] = routeType;

        using var response = await _client.PostFormAsync("/Places/Create", "/Places/Create", fields);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(await FindAsync(name));
    }

    [Fact]
    public async Task Missing_stop_and_route_ids_are_rejected()
    {
        var name = UniqueName("No ids");
        var fields = ValidFields(name);
        fields["StopId"] = "";
        fields["RouteId"] = "0";

        using var response = await _client.PostFormAsync("/Places/Create", "/Places/Create", fields);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(await FindAsync(name));
    }

    [Fact]
    public async Task A_post_without_an_antiforgery_token_is_refused()
    {
        using var content = new FormUrlEncodedContent(ValidFields(UniqueName("Forged")));

        using var response = await _client.PostAsync(new Uri("/Places/Create", UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Editing_a_place_updates_it()
    {
        var name = UniqueName("Work");
        using var created = await _client.PostFormAsync("/Places/Create", "/Places/Create", ValidFields(name));
        var id = (await FindAsync(name))!.Id;

        var fields = ValidFields(name);
        fields["RouteType"] = "0";
        fields["StopId"] = "1072";
        fields["DirectionId"] = "1";

        using var response = await _client.PostFormAsync($"/Places/Edit/{id}", $"/Places/Edit/{id}", fields);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var saved = await FindAsync(name);
        Assert.Equal(TransportMode.Train, saved!.RouteType);
        Assert.Equal(1072, saved.StopId);
        Assert.Equal(1, saved.DirectionId);
    }

    [Fact]
    public async Task Renaming_a_place_to_another_places_name_is_rejected()
    {
        var keep = UniqueName("Gym");
        var other = UniqueName("Library");
        using var a = await _client.PostFormAsync("/Places/Create", "/Places/Create", ValidFields(keep));
        using var b = await _client.PostFormAsync("/Places/Create", "/Places/Create", ValidFields(other));
        var otherId = (await FindAsync(other))!.Id;

        using var response = await _client.PostFormAsync(
            $"/Places/Edit/{otherId}", $"/Places/Edit/{otherId}", ValidFields(keep));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(PlacesController.DuplicateNameMessage, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.NotNull(await FindAsync(other));
    }

    [Fact]
    public async Task Deleting_a_place_removes_it()
    {
        var name = UniqueName("Old job");
        using var created = await _client.PostFormAsync("/Places/Create", "/Places/Create", ValidFields(name));
        var id = (await FindAsync(name))!.Id;

        using var response = await _client.PostFormAsync($"/Places/Delete/{id}", $"/Places/Delete/{id}", new Dictionary<string, string>());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null(await FindAsync(name));
    }

    [Theory]
    [InlineData("/Places/Edit/999999")]
    [InlineData("/Places/Delete/999999")]
    public async Task Unknown_ids_return_404(string url)
    {
        using var response = await _client.GetAsync(new Uri(url, UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
