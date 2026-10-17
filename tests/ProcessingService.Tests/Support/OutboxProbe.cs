using NSubstitute;
using ProcessingService.Application.Outbox;
using Xunit;

namespace ProcessingService.Tests.Support;

/// <summary>One recorded call to IOutboxWriter.WriteAsync(topic, key, event, correlationId, ct).</summary>
public sealed record OutboxCall(string Topic, string Key, object? Event, string CorrelationId);
public static class OutboxProbe
{
    public static IReadOnlyList<OutboxCall> Calls(IOutboxWriter writer) =>
        writer.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IOutboxWriter.WriteAsync))
            .Select(call => call.GetArguments())
            .Where(args => args.Length == 5) // the generic overload: topic, key, event, correlationId, ct
            .Select(args => new OutboxCall((string)args[0]!, (string)args[1]!, args[2], (string)args[3]!))
            .ToList();

    public static IReadOnlyList<T> Events<T>(IOutboxWriter writer) where T : class =>
        Calls(writer).Select(call => call.Event).OfType<T>().ToList();

    public static void AssertNothingWritten(IOutboxWriter writer) => Assert.Empty(Calls(writer));
}
