using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Http.Logging;
using Microsoft.Extensions.Logging;

namespace AUBDigitalAcademy.Services.Implementations;

/// <summary>
/// Custom <see cref="IHttpClientAsyncLogger"/> for Telegram Bot API calls.
/// Replaces the default HttpClient logging handlers so full bot-token URIs, OTP payloads,
/// and raw authorization headers are never written to logs or logging scopes, while preserving
/// HTTP method, redacted URI, operation name, status code, latency, and sanitized exceptions.
/// </summary>
public sealed class TelegramHttpClientLogger : IHttpClientAsyncLogger
{
    private sealed record RequestLogContext(string Method, string RedactedUri, string Operation);

    private readonly ILogger _logger;
    private readonly IConfiguration _configuration;

    public TelegramHttpClientLogger(ILoggerFactory loggerFactory, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(configuration);

        // Use the standard HttpClient category namespace so appsettings log-level controls apply consistently.
        _logger = loggerFactory.CreateLogger("System.Net.Http.HttpClient.ITelegramService.ClientHandler");
        _configuration = configuration;
    }

    public object? LogRequestStart(HttpRequestMessage request)
    {
        var context = CreateContext(request);
        _logger.LogInformation(
            "Telegram HTTP request started: {Method} {RedactedUri} (Operation={Operation})",
            context.Method,
            context.RedactedUri,
            context.Operation);

        return context;
    }

    public ValueTask<object?> LogRequestStartAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(LogRequestStart(request));

    public void LogRequestStop(object? context, HttpRequestMessage request, HttpResponseMessage response, TimeSpan elapsed)
    {
        var logContext = context as RequestLogContext ?? CreateContext(request);
        var statusCode = (int)response.StatusCode;

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation(
                "Telegram HTTP request completed: {Method} {RedactedUri} -> {StatusCode} in {ElapsedMs:F1}ms (Operation={Operation})",
                logContext.Method,
                logContext.RedactedUri,
                statusCode,
                elapsed.TotalMilliseconds,
                logContext.Operation);
        }
        else
        {
            _logger.LogWarning(
                "Telegram HTTP request returned non-success status {StatusCode}: {Method} {RedactedUri} in {ElapsedMs:F1}ms (Operation={Operation})",
                statusCode,
                logContext.Method,
                logContext.RedactedUri,
                elapsed.TotalMilliseconds,
                logContext.Operation);
        }
    }

    public ValueTask LogRequestStopAsync(
        object? context,
        HttpRequestMessage request,
        HttpResponseMessage response,
        TimeSpan elapsed,
        CancellationToken cancellationToken = default)
    {
        LogRequestStop(context, request, response, elapsed);
        return ValueTask.CompletedTask;
    }

    public void LogRequestFailed(
        object? context,
        HttpRequestMessage request,
        HttpResponseMessage? response,
        Exception exception,
        TimeSpan elapsed)
    {
        var logContext = context as RequestLogContext ?? CreateContext(request);
        var knownToken = TelegramLogSanitizer.ResolveBotToken(_configuration);
        var safeEx = TelegramLogSanitizer.SanitizeException(exception, knownToken);
        int? statusCode = response != null
            ? (int)response.StatusCode
            : (exception is HttpRequestException httpEx && httpEx.StatusCode.HasValue
                ? (int)httpEx.StatusCode.Value
                : null);

        if (exception is OperationCanceledException)
        {
            _logger.LogWarning(
                safeEx,
                "Telegram HTTP request canceled: {Method} {RedactedUri} (Operation={Operation}, StatusCode={StatusCode}) after {ElapsedMs:F1}ms",
                logContext.Method,
                logContext.RedactedUri,
                logContext.Operation,
                statusCode,
                elapsed.TotalMilliseconds);
        }
        else
        {
            _logger.LogError(
                safeEx,
                "Telegram HTTP request failed: {Method} {RedactedUri} (Operation={Operation}, StatusCode={StatusCode}) after {ElapsedMs:F1}ms",
                logContext.Method,
                logContext.RedactedUri,
                logContext.Operation,
                statusCode,
                elapsed.TotalMilliseconds);
        }
    }

    public ValueTask LogRequestFailedAsync(
        object? context,
        HttpRequestMessage request,
        HttpResponseMessage? response,
        Exception exception,
        TimeSpan elapsed,
        CancellationToken cancellationToken = default)
    {
        LogRequestFailed(context, request, response, exception, elapsed);
        return ValueTask.CompletedTask;
    }

    private RequestLogContext CreateContext(HttpRequestMessage request)
    {
        var knownToken = TelegramLogSanitizer.ResolveBotToken(_configuration);
        var redactedUri = TelegramLogSanitizer.SanitizeUri(request.RequestUri, knownToken);
        var operation = TelegramLogSanitizer.GetOperationName(request.RequestUri);
        return new RequestLogContext(request.Method.Method, redactedUri, operation);
    }
}
