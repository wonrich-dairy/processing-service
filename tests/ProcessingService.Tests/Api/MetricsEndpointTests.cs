using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using Xunit;

namespace ProcessingService.Tests.Api;

/// <summary>
/// /metrics used to print fixed zeros, so the dashboard's request, error and latency panels stayed flat and the
/// HighErrorRate alert could never fire. These tests prove it now reports what the service really handled.
/// </summary>
public sealed class MetricsEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public MetricsEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<string> ScrapeAsync(HttpClient client)
    {
        var response = await client.GetAsync("/metrics");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Metrics_is_anonymous_and_counts_the_requests_the_service_handled()
    {
        var client = _factory.CreateClient();
        await client.GetAsync("/");

        var body = await ScrapeAsync(client);

        Assert.Contains("processing_http_requests_total{method=\"GET\",endpoint=\"/\",status=\"200\"}", body);
        Assert.DoesNotContain("processing_http_requests_total 0", body); // the old hard-coded placeholder
        Assert.Contains("processing_http_request_duration_seconds_bucket", body);
    }

    [Fact]
    public async Task A_request_matching_no_route_is_labelled_unmatched_and_is_not_a_server_error()
    {
        var client = _factory.CreateClient();
        await client.GetAsync("/no-such-path-" + Guid.NewGuid().ToString("N"));

        var body = await ScrapeAsync(client);

        // One shared label, so a scanner probing random URLs cannot create a time series per URL.
        Assert.Contains("endpoint=\"unmatched\",status=\"404\"", body);
        // A 404 is the caller's mistake: it must not count towards the error rate that HighErrorRate watches.
        Assert.DoesNotContain("processing_http_request_errors_total{method=\"GET\",endpoint=\"unmatched\",status=\"404\"}", body);
    }

    [Fact]
    public async Task Scraping_metrics_does_not_count_the_scrape_itself()
    {
        var client = _factory.CreateClient();
        await ScrapeAsync(client);
        var body = await ScrapeAsync(client);

        Assert.DoesNotContain("endpoint=\"/metrics\"", body);
    }

    [Fact]
    public async Task The_outbox_backlog_gauge_always_exists_so_the_panel_shows_zero_not_no_data()
    {
        var client = _factory.CreateClient();

        var body = await ScrapeAsync(client);

        Assert.Contains("processing_outbox_backlog{status=\"Pending\"}", body);
        Assert.Contains("processing_outbox_backlog{status=\"Poisoned\"}", body);
    }
}
