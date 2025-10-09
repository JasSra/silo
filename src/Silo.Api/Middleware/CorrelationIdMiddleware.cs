using Serilog.Context;

namespace Silo.Api.Middleware;

/// <summary>
/// Middleware to add correlation IDs to all requests for distributed tracing
/// </summary>
public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private const string CorrelationIdHeader = "X-Correlation-ID";

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Get or create correlation ID
        var correlationId = context.Request.Headers[CorrelationIdHeader].FirstOrDefault()
            ?? Guid.NewGuid().ToString();

        // Add to response headers
        context.Response.Headers.Append(CorrelationIdHeader, correlationId);

        // Add to Serilog context for all logs in this request
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}

/// <summary>
/// Extension method to register the correlation ID middleware
/// </summary>
public static class CorrelationIdMiddlewareExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<CorrelationIdMiddleware>();
    }
}
