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
using System.Threading.RateLimiting;
using System.Threading.Tasks;

namespace CleanArchCqrs.API;

/// <summary>
/// Application startup - wires up all layers, middleware, Swagger, and seeds default data.
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services
        builder.Services.AddControllers();

        // Add CORS (P3-07 / BUG-38: Cấu hình nguồn từ AllowedOrigins, loại bỏ AllowAnyOrigin)
        builder.Services.AddCors(options =>
        {
            var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                ?? new[] { "http://localhost:3000", "http://localhost:5173" };

            options.AddPolicy("AllowFrontend", policy =>
            {
                policy.WithOrigins(allowedOrigins)
                      .AllowAnyMethod()
                      .AllowAnyHeader()
                      .AllowCredentials();
            });
        });

        // JWT Configuration
        var jwtSettings = builder.Configuration.GetSection("JwtSettings");
        var secretKey = jwtSettings["SecretKey"] ?? "DefaultSuperSecretKey12345678901234567890";
        var issuer = jwtSettings["Issuer"] ?? "CloudDMS";
        var audience = jwtSettings["Audience"] ?? "CloudDMSUsers";

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
            // Mọi endpoint PHẢI đăng nhập, trừ khi khai báo [AllowAnonymous]
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
        builder.Services.AddScoped<CleanArchCqrs.Application.Common.Interfaces.ICurrentUserService, CleanArchCqrs.API.Services.CurrentUserService>();
        builder.Services.AddInfrastructureServices(builder.Configuration);
        builder.Services.AddApplicationServices();
        builder.Services.AddHealthChecks();

        // Rate Limiter configuration (P3-01 / BUG-17)
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Policy "auth": Tối đa 5 requests / 1 phút theo Client IP (chống brute force đăng nhập / kích hoạt)
            options.AddPolicy("auth", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown_ip",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            // Policy "upload": Tối đa 30 requests / 1 phút theo User hoặc Client IP (chống spam upload tệp tin)
            options.AddPolicy("upload", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                                  ?? httpContext.Connection.RemoteIpAddress?.ToString()
                                  ?? "unknown_ip",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));
        });

        var app = builder.Build();
        using (var scope = app.Services.CreateScope())
        {
            var services = scope.ServiceProvider;
            var logger = services.GetRequiredService<ILogger<Program>>();
            try
            {
                var context = services.GetRequiredService<AppDbContext>();
                try
                {
                    await context.Database.MigrateAsync();
                }
                catch (Exception mex)
                {
                    logger.LogError(mex, "Database migration failed.");
                    if (!app.Environment.IsDevelopment())
                    {
                        throw;
                    }
                }

                var passwordHasher = services.GetRequiredService<CleanArchCqrs.Application.Common.Interfaces.IPasswordHasher>();
                await DbInitializer.SeedDefaultDataAsync(context, passwordHasher, logger, isDevelopment: app.Environment.IsDevelopment());
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred while initializing/seeding the database.");
            }
        }
        // Configure pipeline
        app.UseMiddleware<CleanArchCqrs.API.Middleware.GlobalExceptionHandlerMiddleware>();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Cloud DMS API v1");
                c.RoutePrefix = string.Empty; // Swagger at root
            });
        }

        // Security Headers Middleware (P3-07 / BUG-38)
        app.Use(async (context, next) =>
        {
            context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
            context.Response.Headers.Append("X-Frame-Options", "DENY");
            context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
            context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
            await next();
        });

        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        app.UseCors("AllowFrontend");
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.MapControllers();
        app.MapHealthChecks("/health");

        await app.RunAsync();
    }
}
