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
        builder.Services.AddMemoryCache();

        // CORS: restrict to configured frontend origins instead of trusting every origin.
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? new[] { "http://localhost:3000", "http://localhost:5173" };

        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowFrontend", policy =>
            {
                policy.WithOrigins(allowedOrigins)
                      .AllowAnyMethod()
                      .AllowAnyHeader();
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
                    logger.LogWarning(mex, "Database migration skipped or tables already exist.");
                }

                // Ensure newly added columns & tables exist in MySQL
                try
                {
                    await context.Database.ExecuteSqlRawAsync(
                        "ALTER TABLE `Users` ADD COLUMN `EmailConfirmed` tinyint(1) NOT NULL DEFAULT 0;");
                    logger.LogInformation("Added missing column EmailConfirmed to Users table.");
                }
                catch
                {
                    // Column already exists, ignore
                }

                try
                {
                    await context.Database.ExecuteSqlRawAsync(@"
                        CREATE TABLE IF NOT EXISTS `EmailVerificationTokens` (
                            `Id` char(36) COLLATE ascii_general_ci NOT NULL,
                            `UserId` char(36) COLLATE ascii_general_ci NOT NULL,
                            `Token` varchar(256) CHARACTER SET utf8mb4 NOT NULL,
                            `TokenType` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
                            `ExpiresAt` datetime(6) NOT NULL,
                            `IsUsed` tinyint(1) NOT NULL,
                            `UsedAt` datetime(6) NULL,
                            `CreatedAt` datetime(6) NOT NULL,
                            `UpdatedAt` datetime(6) NULL,
                            PRIMARY KEY (`Id`),
                            KEY `IX_EmailVerificationTokens_UserId` (`UserId`),
                            KEY `IX_EmailVerificationTokens_Token` (`Token`),
                            KEY `IX_EmailVerificationTokens_Token_IsUsed` (`Token`, `IsUsed`),
                            CONSTRAINT `FK_EmailVerificationTokens_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE
                        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;");
                }
                catch (Exception exTokens)
                {
                    logger.LogWarning(exTokens, "Creation of EmailVerificationTokens table skipped.");
                }

                if (await EnsureGoogleUserColumnExistsAsync(context, "GoogleSubject"))
                {
                    logger.LogInformation("Added missing column GoogleSubject to Users table.");
                }

                if (await EnsureGoogleUserColumnExistsAsync(context, "GoogleIssuer"))
                {
                    logger.LogInformation("Added missing column GoogleIssuer to Users table.");
                }

                try
                {
                    await context.Database.ExecuteSqlRawAsync(@"
                        CREATE TABLE IF NOT EXISTS `UserStorageQuotas` (
                            `Id` char(36) COLLATE ascii_general_ci NOT NULL,
                            `UserId` char(36) COLLATE ascii_general_ci NOT NULL,
                            `QuotaBytes` bigint NOT NULL,
                            `UsedBytes` bigint NOT NULL,
                            `CreatedAt` datetime(6) NOT NULL,
                            `UpdatedAt` datetime(6) NULL,
                            PRIMARY KEY (`Id`),
                            UNIQUE KEY `IX_UserStorageQuotas_UserId` (`UserId`),
                            CONSTRAINT `FK_UserStorageQuotas_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE
                        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;");
                }
                catch (Exception exQuotas)
                {
                    logger.LogWarning(exQuotas, "Creation of UserStorageQuotas table skipped.");
                }

                try
                {
                    await context.Database.ExecuteSqlRawAsync(@"
                        CREATE TABLE IF NOT EXISTS `StorageProviders` (
                            `Id` char(36) COLLATE ascii_general_ci NOT NULL,
                            `Name` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
                            `Type` int NOT NULL,
                            `ConfigEncrypted` longtext CHARACTER SET utf8mb4 NOT NULL,
                            `TotalCapacityBytes` bigint NOT NULL,
                            `UsedCapacityBytes` bigint NOT NULL,
                            `RootFolderId` varchar(255) CHARACTER SET utf8mb4 NULL,
                            `IsActive` tinyint(1) NOT NULL DEFAULT 1,
                            `Status` varchar(50) CHARACTER SET utf8mb4 NOT NULL DEFAULT 'Connected',
                            `LastSyncAt` datetime(6) NULL,
                            `CreatedAt` datetime(6) NOT NULL,
                            `UpdatedAt` datetime(6) NULL,
                            PRIMARY KEY (`Id`)
                        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;");

                    try
                    {
                        await context.Database.ExecuteSqlRawAsync(@"
                            ALTER TABLE `Tenants` ADD COLUMN `StorageProviderId` char(36) COLLATE ascii_general_ci NULL;");
                    }
                    catch { /* column already exists */ }

                    try
                    {
                        await context.Database.ExecuteSqlRawAsync(@"
                            ALTER TABLE `FileEntries` ADD COLUMN `StorageProviderId` char(36) COLLATE ascii_general_ci NULL;");
                    }
                    catch { /* column already exists */ }

                    try
                    {
                        await context.Database.ExecuteSqlRawAsync(@"
                            ALTER TABLE `FileEntries` ADD COLUMN `ScanStatus` varchar(50) NOT NULL DEFAULT 'Clean';");
                    }
                    catch { /* column already exists */ }
                }
                catch (Exception exProviders)
                {
                    logger.LogWarning(exProviders, "Creation of StorageProviders table skipped.");
                }

                var passwordHasher = services.GetRequiredService<CleanArchCqrs.Application.Common.Interfaces.IPasswordHasher>();
                await DbInitializer.SeedDefaultDataAsync(context, passwordHasher, logger);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred while seeding the database.");
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
        app.UseCors("AllowFrontend");
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();

        await app.RunAsync();
    }

    private static async Task<bool> EnsureGoogleUserColumnExistsAsync(
        AppDbContext context,
        string columnName,
        CancellationToken cancellationToken = default)
    {
        var columnCount = await context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS `Value` FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = {0} AND COLUMN_NAME = {1}",
            "Users",
            columnName)
            .SingleAsync(cancellationToken);

        if (columnCount > 0)
        {
            return false;
        }

        var alterStatement = columnName switch
        {
            "GoogleSubject" => "ALTER TABLE `Users` ADD COLUMN `GoogleSubject` varchar(255) NULL;",
            "GoogleIssuer" => "ALTER TABLE `Users` ADD COLUMN `GoogleIssuer` varchar(255) NULL;",
            _ => throw new ArgumentOutOfRangeException(nameof(columnName), "Unsupported Google identity column.")
        };

        await context.Database.ExecuteSqlRawAsync(alterStatement, cancellationToken);
        return true;
    }
}
