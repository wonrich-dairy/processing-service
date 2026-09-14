using Xunit;

namespace ProcessingService.Tests.Api;

/// <summary>
/// Deliberately failing test, pushed to prove the SCRUM-72 acceptance criterion that a test
/// failure blocks deployment. Both deploy jobs declare `needs: build-and-test`, so this failing
/// assertion should turn the run red and stop it before any deploy job starts.
///
/// This file is temporary and is removed once the red run is recorded on the ticket.
/// </summary>
public sealed class DeliberateFailureTest
{
    [Fact]
    public void This_test_fails_on_purpose_to_prove_the_pipeline_gate()
    {
        Assert.Equal("deployment should not happen", "deployment happened anyway");
    }
}
