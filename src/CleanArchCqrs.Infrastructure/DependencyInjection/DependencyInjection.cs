using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Interfaces.Repositories;
using CleanArchCqrs.Infrastructure.Authentication;
using CleanArchCqrs.Infrastructure.Persistence;
using CleanArchCqrs.Infrastructure.Repositories;
using CleanArchCqrs.Infrastructure.Services;
using CleanArchCqrs.Infrastructure.Services.Auth;
using CleanArchCqrs.Infrastructure.Security;
using CleanArchCqrs.Infrastructure.Workers;
using CleanArchCqrs.Application.Common.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchCqrs.Infrastructure.DependencyInjection
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructureServices(
         this IServiceCollection services,
         IConfiguration configuration)
        {
            // Services
            services.AddSingleton<IDateTimeProvider, DateTimeProvider>();

            services.Configure<GoogleAuthOptions>(configuration.GetSection("GoogleAuth"));
            services.AddScoped<IPasswordHasher, PasswordHasher>();

            services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.SectionName));
            services.AddSingleton<RedisSecurityClient>();
            services.AddSingleton<ISecurityRedis>(provider =>
                provider.GetRequiredService<RedisSecurityClient>());
            services.AddSingleton<ISecurityReadiness, RedisReadiness>();
            services.AddScoped<IAccessSnapshotStore, RedisAccessSnapshotStore>();
            services.AddScoped<IAuthorizationService, AuthorizationService>();
            services.AddScoped<IAuthorizationMutationCoordinator, AuthorizationMutationCoordinator>();
            services.AddScoped<IAuthorizationProjectionWriter, AuthorizationProjectionWriter>();
            services.AddHostedService<AuthorizationOutboxWorker>();
            services.AddSingleton<AccessPolicyEvaluator>();

            // JWT
            services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

            services.AddScoped<IJwtTokenService, JwtTokenService>();
            services.AddScoped<IAuthSessionService, AuthSessionService>();
            services.AddScoped<IFileAccessContextResolver, FileAccessContextResolver>();
            services.AddScoped<IFileAclQuery, FileAclQuery>();
            services.AddScoped<IFileAccessService, FileAccessService>();
            var readTtlSeconds = configuration.GetValue<int?>("ReadAccess:UrlTtlSeconds") ?? 120;
            if (readTtlSeconds is < 1 or > 600)
                throw new InvalidOperationException("ReadAccess:UrlTtlSeconds must be between 1 and 600.");
            var publicOriginText = configuration["ReadAccess:PublicApiOrigin"];
            Uri? publicOrigin = null;
            if (!string.IsNullOrWhiteSpace(publicOriginText))
            {
                if (!Uri.TryCreate(publicOriginText, UriKind.Absolute, out publicOrigin)
                    || publicOrigin.Scheme is not ("https" or "http")
                    || publicOrigin.AbsolutePath != "/" || !string.IsNullOrEmpty(publicOrigin.Query)
                    || !string.IsNullOrEmpty(publicOrigin.Fragment) || !string.IsNullOrEmpty(publicOrigin.UserInfo))
                    throw new InvalidOperationException("ReadAccess:PublicApiOrigin must be an HTTP(S) origin.");
            }
            services.AddSingleton(new CleanArchCqrs.Application.Features.Files.Queries.GetFileReadAccess.ReadAccessOptions(
                TimeSpan.FromSeconds(readTtlSeconds), publicOrigin));
            services.AddScoped<IQuotaReleaseWriter, MySqlQuotaReleaseWriter>();
            services.AddScoped<CleanArchCqrs.Application.Features.Files.Commands.UploadFile.UploadAuthorization>();
            services.AddScoped<CleanArchCqrs.Application.Features.Files.Commands.UploadFile.UploadQuota>();
            services.AddScoped<CleanArchCqrs.Application.Features.Files.Commands.BeginMultipartUpload.MultipartUploadService>();
            services.AddHostedService<MultipartUploadRecoveryWorker>();
            services.AddScoped<CleanArchCqrs.Application.Features.Files.Commands.ManageAccess.FileMutationService>();
            services.AddScoped<IGoogleAuthService>(provider =>
                new GoogleAuthService(
                    new HttpClient(),
                    provider.GetRequiredService<IMemoryCache>(),
                    provider.GetRequiredService<IOptions<GoogleAuthOptions>>(),
                    provider.GetRequiredService<IUserRepository>(),
                    provider.GetRequiredService<IUserAuthorizationRepository>(),
                    provider.GetRequiredService<IApplicationDbContext>(),
                    provider.GetRequiredService<IAuthSessionService>()));
            services.AddScoped<IEmailService, EmailService>();
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<IUserAuthorizationRepository, UserAuthorizationRepository>();

            // Storage & Security
            services.AddSingleton<IAesEncryptionService, AesEncryptionService>();
            services.AddScoped<IStorageServiceFactory, CleanArchCqrs.Infrastructure.Services.Storage.StorageServiceFactory>();
            services.AddScoped<IObjectReadUrlService, CleanArchCqrs.Infrastructure.Services.Storage.ObjectReadUrlService>();
            services.AddScoped<IVirusScannerService, ClamAvVirusScannerService>();

            // Database
            var connectionString =
                configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException(
                    "Connection string 'DefaultConnection' not found.");

            var serverVersion =
                new MySqlServerVersion(
                    new Version(8, 0, 36));

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseMySql(
                    connectionString,
                    serverVersion,
                    mySqlOptions =>
                    {
                        mySqlOptions.MigrationsAssembly(
                            "CleanArchCqrs.Infrastructure");

                        mySqlOptions.EnableRetryOnFailure(
                            maxRetryCount: 5,
                            maxRetryDelay: TimeSpan.FromSeconds(10),
                            errorNumbersToAdd: null);
                    });
            });

            // Application DbContext
            services.AddScoped<IApplicationDbContext>(
                provider =>
                    provider.GetRequiredService<AppDbContext>());

            return services;
        }
    }
}
