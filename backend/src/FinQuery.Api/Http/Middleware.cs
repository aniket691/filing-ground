using System.Diagnostics;
using System.Text.Json;
using FinQuery.Api.Auth;
using FinQuery.Application.Auth;
using FinQuery.Domain.Errors;

namespace FinQuery.Api.Http;

public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            logger.LogInformation(
                "{Method} {Path} → {StatusCode} ({ElapsedMs:0}ms)",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                elapsedMs);
        }
    }
}

public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (DomainError error)
        {
            await WriteAsync(context, ApiJson.StatusCodeFor(error), new ErrorBody
            {
                Detail = error.Message,
                Error = error.ErrorCode
            });
        }
        catch (AccessDeniedException denied)
        {
            await WriteAsync(context, denied.StatusCode, new ErrorBody { Detail = denied.Message });
        }
        catch (BodyValidationException invalid)
        {
            await WriteRawAsync(context, StatusCodes.Status422UnprocessableEntity, new { detail = invalid.Errors });
        }
        catch (JsonException)
        {
            await WriteAsync(context, StatusCodes.Status422UnprocessableEntity, new ErrorBody
            {
                Detail = "Request body is invalid."
            });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);
            await WriteAsync(context, StatusCodes.Status500InternalServerError, new ErrorBody
            {
                Detail = "Internal Server Error"
            });
        }
    }

    private static async Task WriteAsync(HttpContext context, int statusCode, ErrorBody body)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(context.Response.Body, body, ApiJson.Serializer);
    }

    private static async Task WriteRawAsync(HttpContext context, int statusCode, object body)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(context.Response.Body, body, ApiJson.Serializer);
    }
}
