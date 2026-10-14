using CleanArchCqrs.API.Authorization;
using CleanArchCqrs.Application.DependencyInjection;
using CleanArchCqrs.Infrastructure.DependencyInjection;
using CleanArchCqrs.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using System.Security.Claims;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.RateLimiting;

namespace CleanArchCqrs.API;

/// <summary>
/// Application startup - wires up all layers, middleware, Swagger, and seeds default data.
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Host.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = true;
            options.ValidateOnBuild = true;
        });
        // Hosting's information-level request-start log includes raw GET query strings.
        builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);

        builder.Services.AddProductionSecurity(builder.Configuration, builder.Environment);

        // Add services
        builder.Services.AddControllers();
        builder.Services.AddMemoryCache();
        var searchRate = builder.Configuration.GetValue<int?>("RateLimiting:SearchPerMinute") ?? 30;
        var authRate = builder.Configuration.GetValue<int?>("RateLimiting:AuthPerMinute") ?? 10;
        var uploadRate = builder.Configuration.GetValue<int?>("RateLimiting:UploadPerMinute") ?? 10;
        if (new[] { searchRate, authRate, uploadRate }.Any(value => value is < 1 or > 10000))
            throw new InvalidOperationException("Rate limits must be between 1 and 10000 per minute.");
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return ValueTask.CompletedTask;
            };
            options.AddPolicy("file-search", context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = searchRate, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            // Existing controllers already reference these policies; register them when enabling the middleware.
            options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = authRate, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy("upload", context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = uploadRate, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });

        // CORS: restrict to configured frontend origins instead of trusting every origin.
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? Array.Empty<string>();

        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowFrontend", policy =>
            {
                policy.WithOrigins(allowedOrigins)
                      .AllowAnyMethod()
                      .AllowAnyHeader()
                      .AllowCredentials();
            });
        });


        // JWT Configuration: require a configured secret instead of silently accepting a default fallback key.
        var jwtSettings = builder.Configuration.GetSection("JwtSettings");
        var secretKey = jwtSettings["SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            throw new InvalidOperationException("JwtSettings:SecretKey must be configured via environment variables or user secrets.");
        }

        var issuer = jwtSettings["Issuer"] ?? throw new InvalidOperationException("JwtSettings:Issuer must be configured.");
        var audience = jwtSettings["Audience"] ?? throw new InvalidOperationException("JwtSettings:Audience must be configured.");

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey))
                };
            });

        builder.Services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });
        builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        builder.Services.AddEndpointsApiExplorer();

        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Cloud DMS API",
                Version = "v1",
                Description = "Enterprise Multi-Tenant Document Management System with Clean Architecture & CQRS"
            });

            // Prevent schema ID collisions across namespaces
            c.CustomSchemaIds(type => type.FullName?.Replace("+", "."));
            c.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());

            // Add JWT Bearer Auth definition to Swagger
            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey,
                Scheme = "Bearer"
            });

            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        });

        // Register application and infrastructure services
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddHttpClient();
        builder.Services.AddScoped<CleanArchCqrs.Application.Common.Interfaces.ICurrentUserService, CleanArchCqrs.API.Services.CurrentUserService>();
        builder.Services.AddInfrastructureServices(builder.Configuration);
        builder.Services.AddApplicationServices();

        var app = builder.Build();
        if (builder.Configuration.GetValue<bool>("Database:InitializeOnStartup"))
        {
            using var scope = app.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await context.Database.MigrateAsync();
            await DbInitializer.SeedDefaultDataAsync(context,
                scope.ServiceProvider.GetRequiredService<CleanArchCqrs.Application.Common.Interfaces.IPasswordHasher>(),
                scope.ServiceProvider.GetRequiredService<ILogger<Program>>());

            try
            {
                var readiness = scope.ServiceProvider.GetRequiredService<CleanArchCqrs.Application.Common.Interfaces.ISecurityReadiness>();
                var runId = await readiness.GetCurrentServerRunIdAsync(CancellationToken.None);
                var generation = Guid.NewGuid().ToString("N");
                await readiness.SetReadyAsync(generation, runId, CancellationToken.None);
                scope.ServiceProvider.GetRequiredService<ILogger<Program>>().LogInformation("Security Redis readiness initialized successfully (RunId: {RunId}, Generation: {Gen}).", runId, generation);
            }
            catch (Exception exRedis)
            {
                scope.ServiceProvider.GetRequiredService<ILogger<Program>>().LogWarning(exRedis, "Security Redis readiness initialization skipped or Redis not reachable.");
            }
        }
        // Configure pipeline
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Cloud DMS API v1");
                c.RoutePrefix = string.Empty; // Swagger at root
            });
        }

        app.UseMiddleware<CleanArchCqrs.API.Middleware.GlobalExceptionHandlerMiddleware>();
        app.UseForwardedHeaders();
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            await next();
        });
        app.UseCors("AllowFrontend");
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();

        await app.RunAsync();
    }

}
