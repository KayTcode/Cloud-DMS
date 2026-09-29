using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Interfaces.Repositorires;
using CleanArchCqrs.Infrastructure.Persistence;
using CleanArchCqrs.Infrastructure.Persistence.Configuations;
using CleanArchCqrs.Infrastructure.Repositories;
using CleanArchCqrs.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

            services.AddScoped<IPasswordHasher, PasswordHasher>();

            // JWT
            services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

            services.AddScoped<IJwtTokenService, JwtTokenService>();
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<IUserAuthorizationRepository, UserAuthorizationRepository>();

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
