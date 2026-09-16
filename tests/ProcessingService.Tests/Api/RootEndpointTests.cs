using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using Xunit;

namespace ProcessingService.Tests.Api;

/// <summary>
/// SCRUM-105: staging root URL returned 404 because no route was mapped at "/".
/// Proves: GET / is anonymous, returns 200 and a service descriptor pointing at /health.
/// </summary>
public sealed class RootEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RootEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Root_without_token_returns_200_with_service_descriptor()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Wonrich Processing Service", body);
        Assert.Contains("/health", body);
    }
}
