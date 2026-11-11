using System.Net;
using System.Text.Json.Serialization;
using MeetingActionTracker.Api;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<ActionStatus>(allowIntegerValues: false)));
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = DocumentStore.MaxSize + 1024 * 1024);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = DocumentStore.MaxSize + 1024 * 1024);
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
var dataPath = Path.GetFullPath(builder.Configuration["DataDirectory"] ?? Path.Combine(builder.Environment.ContentRootPath, "data"));
Directory.CreateDirectory(dataPath);
builder.Services.AddDbContext<TrackerDb>(options => options.UseSqlite(new SqliteConnectionStringBuilder
{
    DataSource = Path.Combine(dataPath, "tracker.db"),
    ForeignKeys = true
}.ToString()));
builder.Services.AddSingleton(new DocumentStore(Path.Combine(dataPath, "documents")));

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages(async statusContext =>
{
    var status = statusContext.HttpContext.Response.StatusCode;
    await Results.Problem(statusCode: status, title: status == 404 ? "Resource not found" : "Request failed",
        detail: status == 404 ? "This resource or operation does not exist." : "Check the request and try again.").ExecuteAsync(statusContext.HttpContext);
});
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    var address = context.Connection.RemoteIpAddress;
    Check.Require(address is not null && IPAddress.IsLoopback(address), "This prototype accepts loopback connections only.", 403);
    if (context.Request.Headers.TryGetValue("Origin", out var origin))
    {
        Check.Require(Uri.TryCreate(origin.ToString(), UriKind.Absolute, out var uri)
            && uri.Scheme == "http" && uri.Host is "localhost" or "127.0.0.1" or "[::1]"
            && uri.Port is 5080 or 5173, "The request origin is not allowed for this local prototype.", 403);
    }
    if (context.Request.Headers["Sec-Fetch-Site"] == "cross-site")
        throw new ApiException(403, "Cross-site requests are not allowed.");
    await next(context);
});

using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<TrackerDb>().Database.MigrateAsync();

app.MapGet("/api/openapi.yaml", () => Results.File(Path.Combine(AppContext.BaseDirectory, "openapi.yaml"), "application/yaml"));
app.MapTrackerEndpoints();
app.Run();
