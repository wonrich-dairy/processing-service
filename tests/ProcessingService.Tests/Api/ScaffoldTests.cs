using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ProcessingService.Tests.Support;

namespace ProcessingService.Tests.Api;

/// <summary>
/// The four scenarios SCRUM-56 is accepted on: the service reports healthy with its database, a
/// valid token is authorised and its claims readable, an unauthenticated call is refused, and no
/// connection string is committed.
/// </summary>
public class ScaffoldTests
{
    [Fact]
    public async Task Health_reports_200_and_the_database_connection()
    {
        using var factory = new ProcessingApiFactory();

        var response = await factory.CreateClient().GetAsync("/health");
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
        Assert.Equal("Healthy", body.GetProperty("checks").GetProperty("database").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Health_reports_unhealthy_when_the_database_cannot_be_reached()
    {
        // A process that is up but cannot reach its data is not ready to serve, and the probe has
        // to say so — otherwise the container is rolled into the load balancer regardless.
        using var factory = new ProcessingApiFactory { DatabaseReachable = false };

        var response = await factory.CreateClient().GetAsync("/health");
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Health_does_not_require_a_token()
    {
        // The runtime probes this before anyone holds a token.
        using var factory = new ProcessingApiFactory();

        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task A_token_from_the_auth_service_is_authorised_and_its_claims_are_readable()
    {
        using var factory = new ProcessingApiFactory();
        var client = factory.CreateClientAs("ProductionManager", "user-42", "p.manager");

        var caller = await client.GetFromJsonAsync<JsonElement>("/api/session/me");

        Assert.Equal("user-42", caller.GetProperty("userId").GetString());
        Assert.Equal("p.manager", caller.GetProperty("userName").GetString());
        Assert.Equal("ProductionManager", caller.GetProperty("role").GetString());
    }

    [Fact]
    public async Task An_unauthenticated_request_is_refused_with_401()
    {
        using var factory = new ProcessingApiFactory();

        var response = await factory.CreateClient().GetAsync("/api/session/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_signed_with_another_key_is_refused()
    {
        // Validation is local, so a forged token has to fail here rather than at the auth service.
        using var factory = new ProcessingApiFactory();
        var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            ProcessingApiFactory.TokenFor("ProductionManager", signingKey: "a-different-signing-key-0123456789012345"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/session/me")).StatusCode);
    }

    [Fact]
    public async Task A_token_from_another_issuer_is_refused()
    {
        using var factory = new ProcessingApiFactory();
        var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            ProcessingApiFactory.TokenFor("ProductionManager", issuer: "someone-elses-auth"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/session/me")).StatusCode);
    }

    [Fact]
    public async Task The_endpoints_are_documented_in_swagger_with_a_bearer_scheme()
    {
        using var factory = new ProcessingApiFactory();

        var document = await factory.CreateClient().GetStringAsync("/swagger/v1/swagger.json");
        var swagger = JsonDocument.Parse(document).RootElement;

        Assert.Contains("/api/session/me", document, StringComparison.Ordinal);

        var scheme = swagger.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
    }
}
