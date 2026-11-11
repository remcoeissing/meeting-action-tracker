using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

namespace MeetingActionTracker.Api;

public sealed class ApiException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken token)
    {
        var (status, detail) = exception switch
        {
            ApiException problem => (problem.Status, problem.Message),
            BadHttpRequestException bad => (bad.StatusCode, bad.StatusCode == 413 ? "The request exceeds the upload limit." : "The request body or query parameters are invalid."),
            DbUpdateException { InnerException: SqliteException { SqliteErrorCode: 19 } } =>
                (409, "This operation conflicts with existing records. Refresh the workspace and try again."),
            _ => (500, "The operation failed. Check the API logs and local data storage, then try again.")
        };
        logger.Log(status >= 500 ? LogLevel.Error : LogLevel.Warning, exception, "API request failed with {Status}: {Detail}", status, detail);
        await Results.Problem(statusCode: status, title: status switch
        {
            400 => "Invalid request", 403 => "Local access only", 404 => "Not found",
            409 => "Record conflict", 413 => "Document too large", 415 => "Unsupported document",
            _ => "Operation failed"
        }, detail: detail).ExecuteAsync(context);
        return true;
    }
}

public static class Check
{
    public static void Input(object value)
    {
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(value, new ValidationContext(value), errors, true))
            throw new ApiException(400, string.Join(" ", errors.Select(x => x.ErrorMessage)));
    }

    public static T Found<T>(T? value, string name) where T : class =>
        value ?? throw new ApiException(404, $"{name} was not found.");

    public static void Require(bool condition, string message, int status = 400)
    {
        if (!condition) throw new ApiException(status, message);
    }

    public static DateOnly Date(string value)
    {
        Require(DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            && date != DateOnly.MinValue, "A valid due date / today date in YYYY-MM-DD format is required.");
        return date;
    }

    public static DateTime Timestamp(string value)
    {
        Require(!string.IsNullOrWhiteSpace(value), "Scheduled date and time are required.");
        var hasZone = value.EndsWith('Z') || (value.Length >= 6 && (value[^6] == '+' || value[^6] == '-') && value[^3] == ':');
        Require(value.Contains('T') && hasZone
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            "Scheduled time must be an ISO 8601 timestamp with a timezone.");
        var timestamp = DateTimeOffset.Parse(value, CultureInfo.InvariantCulture).UtcDateTime;
        Require(timestamp != DateTime.MinValue, "A valid scheduled date and time are required.");
        return timestamp;
    }
}
