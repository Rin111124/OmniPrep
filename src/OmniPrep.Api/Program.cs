using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using System.Text.Json.Serialization;
using OmniPrep.Infrastructure;
using OmniPrep.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// Bound request buffering for endpoints accessed without the Next.js BFF as well.
builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = 3 * 1024 * 1024);

// ============================================================
// DỊCH VỤ (Services)
// ============================================================

// Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Infrastructure (EF Core, Gemini, Repositories, Services)
builder.Services.AddInfrastructure(builder.Configuration);

// CORS: Cho phép Next.js frontend gọi API theo môi trường (Staging / Production / Local)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        var allowedOrigins = (builder.Configuration["Cors:AllowedOrigins"] ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var frontendUrl = builder.Configuration["Frontend:Url"];
        if (!string.IsNullOrWhiteSpace(frontendUrl) && !allowedOrigins.Contains(frontendUrl))
            allowedOrigins.Add(frontendUrl);

        if (allowedOrigins.Count == 0)
        {
            allowedOrigins.Add("http://localhost:3000");
            allowedOrigins.Add("https://omniprep.vercel.app");
        }

        policy
            .WithOrigins(allowedOrigins.ToArray())
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// Authentication: Xác thực JWT từ Supabase
// Supabase access tokens are validated through the project OIDC discovery and JWKS endpoint.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var supabaseUrl = builder.Configuration["Supabase:Url"]
            ?? throw new InvalidOperationException("Thiếu Supabase:Url trong configuration");
        var issuer = $"{supabaseUrl.TrimEnd('/')}/auth/v1";
        options.Authority = issuer;
        options.MetadataAddress = $"{issuer}/.well-known/openid-configuration";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = "authenticated",
            ValidateLifetime = true
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString();
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { error = "TUTOR_RATE_LIMITED", message = "Please wait before sending another tutor request." },
            cancellationToken);
    };
    options.AddPolicy("tutor-per-user", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.FindFirst("sub")?.Value ?? "unauthenticated",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

// ============================================================
// BUILD APP
// ============================================================
var app = builder.Build();

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("GlobalException");
        logger.LogError(ex, "Unhandled exception on {Path}", context.Request.Path);
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = ex.GetType().FullName,
                message = ex.Message,
                stackTrace = ex.StackTrace
            });
        }
    }
});

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

// ============================================================
// MINIMAL API ENDPOINTS
// ============================================================
app.MapGroup("/api")
   .MapHealthApi()
   .MapSubjectsApi()
   .MapExamsApi()
   .MapGradingApi()
   .MapTutorApi()
   .MapDashboardApi()
   .MapIngestionApi()
   .MapQuestionModerationApi()
   .MapUsersApi()
   .MapMistakesApi()
   .MapExamSourcesApi();

app.Run();
