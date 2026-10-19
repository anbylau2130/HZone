using System.Net;
using System.Net.Http.Json;
using HZone.Tests.Helpers;

namespace HZone.Tests;

public class HealthEndpointTests : IClassFixture<HZoneApiFactory>
{
    private readonly HZoneApiFactory _factory;

    public HealthEndpointTests(HZoneApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Health_returns_200_ok()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.Equal("ok", body!.Status);
    }

    private sealed record HealthResponse(string Status);
}
