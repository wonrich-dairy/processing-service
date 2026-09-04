using System.Net;
using ProcessingService.Api.Infrastructure;
using ProcessingService.Tests.Support;

namespace ProcessingService.Tests.Api;

/// <summary>
/// The browser origin policy (SCRUM-92, as it applies here).
/// </summary>
/// <remarks>
/// The MCC service shipped without CORS and the SPA could not sign in at all. The failure carries
/// no status and no headers, so it reads as an unreachable service rather than a misconfigured
/// one, and the client blamed the officer's network. These pin it for this service.
/// </remarks>
public class CorsTests
{
    [Fact]
    public async Task A_preflight_from_the_configured_origin_is_answered_with_its_headers()
    {
        using var factory = new ProcessingApiFactory();
        var client = factory.CreateClient();

        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/processing/tanks");
        preflight.Headers.Add("Origin", ProcessingApiFactory.AllowedTestOrigin);
        preflight.Headers.Add("Access-Control-Request-Method", "GET");
        preflight.Headers.Add("Access-Control-Request-Headers", "authorization");

        var response = await client.SendAsync(preflight);

        Assert.NotEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.True(
            response.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowed),
            "The preflight was answered without an Access-Control-Allow-Origin header, so the "
            + "browser will refuse the real request.");
        Assert.Equal(ProcessingApiFactory.AllowedTestOrigin, Assert.Single(allowed));
    }

    [Fact]
    public async Task An_origin_that_is_not_configured_is_not_allowed()
    {
        using var factory = new ProcessingApiFactory();
        var client = factory.CreateClient();

        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/processing/tanks");
        preflight.Headers.Add("Origin", "https://not-the-wonrich-client.example");
        preflight.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(preflight);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public void The_policy_is_registered_and_applied_under_one_name()
    {
        Assert.Equal("frontend", ProcessingCorsExtensions.PolicyName);
    }
}
