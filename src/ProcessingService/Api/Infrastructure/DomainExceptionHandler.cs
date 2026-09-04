using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ProcessingService.Domain.Common;

namespace ProcessingService.Api.Infrastructure;

/// <summary>
/// The problem body every refusal is served as. <c>code</c> is what a consumer branches on.
/// </summary>
public sealed class ProcessingProblemDetails : ProblemDetails
{
    /// <summary>Stable identifier for the rule that was broken.</summary>
    public string? Code { get; set; }
}

/// <summary>
/// Turns domain rule violations into ProblemDetails, so controllers stay free of try/catch and
/// every refusal reaches the caller with the code it is identified by.
/// </summary>
public sealed class DomainExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;
    private readonly ILogger<DomainExceptionHandler> _logger;

    public DomainExceptionHandler(
        IProblemDetailsService problemDetails,
        ILogger<DomainExceptionHandler> logger)
    {
        _problemDetails = problemDetails;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not DomainException domain)
        {
            return false;
        }

        var (status, title) = Describe(domain);

        _logger.LogInformation(
            "Rejected {Method} {Path}: {Code} - {Message}",
            httpContext.Request.Method,
            httpContext.Request.Path,
            domain.Code,
            domain.Message);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = domain.Message,
            Type = $"https://wonrich.dev/problems/{domain.Code}"
        };

        problem.Extensions["code"] = domain.Code;

        if (domain is DuplicateCodeException duplicate)
        {
            problem.Extensions["conflictingCode"] = duplicate.ConflictingCode;
        }

        httpContext.Response.StatusCode = status;

        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem
        });
    }

    private static (int Status, string Title) Describe(DomainException exception) => exception switch
    {
        DuplicateCodeException => (StatusCodes.Status409Conflict, "Code already in use"),

        DispatchAlreadyUnloadedException => (StatusCodes.Status409Conflict, "Dispatch already unloaded"),

        // Something the request body points at does not exist. A resource addressed by the route
        // answers 404 instead, which the controllers handle themselves.
        EntityNotFoundException => (StatusCodes.Status422UnprocessableEntity, "Referenced record does not exist"),

        _ => (StatusCodes.Status400BadRequest, "Request could not be completed")
    };
}

/// <summary>Builds the refusals a controller writes itself, in the handler's shape.</summary>
public static class ProcessingProblemResults
{
    public static ObjectResult ProcessingProblem(
        this ControllerBase controller,
        int statusCode,
        string code,
        string title,
        string detail)
    {
        var result = controller.Problem(
            statusCode: statusCode,
            title: title,
            detail: detail,
            type: $"https://wonrich.dev/problems/{code}");

        if (result.Value is ProblemDetails problem)
        {
            problem.Extensions["code"] = code;
        }

        return result;
    }
}
