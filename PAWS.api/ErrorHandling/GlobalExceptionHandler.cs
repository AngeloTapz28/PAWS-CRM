using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace PAWS.api.ErrorHandling;

// Catches every error that no other code handled, logs the details privately,
// and sends the caller a short, safe message instead.
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // The unique ID of this request. It appears in the log AND in the response,
        // so you can match a caller's error to the full details in the log.
        var traceId = httpContext.TraceIdentifier;

        // FULL details go to the log only (Output window / console on your PC).
        _logger.LogError(exception,
            "Unhandled error. TraceId: {TraceId}, Path: {Path}",
            traceId, httpContext.Request.Path);

        // The caller gets ONLY this. No exception message, no stack trace.
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Detail = "Please try again. If the problem continues, contact support and give them the trace ID."
        };
        problem.Extensions["traceId"] = traceId;

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);

        // true = "I handled it, stop looking for other handlers."
        return true;
    }
}
