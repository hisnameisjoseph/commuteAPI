using System.Net;
using CommuteApi.Tests.Infrastructure;

namespace CommuteApi.Tests.Web;

public sealed class HealthEndpointTests(CommuteApiFactory factory) : IClassFixture<CommuteApiFactory>
{
    [Fact]
    public async Task Health_returns_200_without_an_api_key_when_the_database_is_reachable()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
