using Hangfire;
using Hangfire.MySql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Transactions;
using TAKAHATA.Application.Common.Interface.Repositories;
using TAKAHATA.Application.Common.Interface.Services;
using TAKAHATA.Application.Services;
using TAKAHATA.Domain.User;
using TAKAHATA.Infrastructure.Authentication;
using TAKAHATA.Infrastructure.Persistence;
using TAKAHATA.Infrastructure.Persistence.Repositories;
using TAKAHATA.Infrastructure.Services;
using TAKAHATA.Infrastructure.Settings;

namespace TAKAHATA.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            // Repositories
            services.AddScoped<IMachineRepository, MachineRepository>();
            services.AddScoped<IIndexRepository, IndexRepository>();
            services.AddScoped<IPartRepository, PartRepository>();
            services.AddScoped<IPartProcessRepository, PartProcessRepository>();
            services.AddScoped<IMachineMaintenanceProcessRepository, MachineMaintenanceProcessRepository>();
            services.AddScoped<IUserMachinePermissionRepository, UserMachinePermissionRepository>();
            services.AddScoped<IUnitOfWorkRepository, UnitOfWorkRepository>();

            // Service
            services.AddScoped<IPLCService, PLCService>();
            services.AddScoped<IPLCSettingsService, PLCSettingsService>();

            // MySQL
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseMySql(
                    connectionString,
                    ServerVersion.AutoDetect(connectionString));
            });

            services
                .AddIdentity<ApplicationUser, IdentityRole>()
                .AddEntityFrameworkStores<AppDbContext>()
                .AddDefaultTokenProviders();

            // JWT
            var jwtSettings = new JwtSettings();
            configuration.Bind(JwtSettings.SectionName, jwtSettings);
            services.AddSingleton(Options.Create(jwtSettings));

            services
                .AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme =
                        JwtBearerDefaults.AuthenticationScheme;

                    options.DefaultChallengeScheme =
                        JwtBearerDefaults.AuthenticationScheme;
                })
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters =
                        new TokenValidationParameters
                        {
                            ValidateIssuer = true,
                            ValidateAudience = true,
                            ValidateLifetime = true,
                            ValidateIssuerSigningKey = true,
                            ClockSkew = TimeSpan.Zero,
                            ValidIssuer = jwtSettings.Issuer,
                            ValidAudience = jwtSettings.Audience,

                            IssuerSigningKey =
                                new SymmetricSecurityKey(
                                    Encoding.UTF8.GetBytes(jwtSettings.Secret))
                        };
                });

            services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();

            // Hangfire
            var hangfireConnectionString =
                configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Không tìm thấy connection string HangfireConnection.");

            services.AddHangfire(configuration =>
            {
                configuration
                    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                    .UseSimpleAssemblyNameTypeSerializer()
                    .UseRecommendedSerializerSettings()
                    .UseStorage(
                        new MySqlStorage(
                            hangfireConnectionString,
                            new MySqlStorageOptions
                            {
                                TransactionIsolationLevel =
                                    IsolationLevel.ReadCommitted,

                                QueuePollInterval =
                                    TimeSpan.FromSeconds(15),

                                JobExpirationCheckInterval =
                                    TimeSpan.FromHours(1),

                                CountersAggregateInterval =
                                    TimeSpan.FromMinutes(5),

                                PrepareSchemaIfNecessary = true,

                                DashboardJobListLimit = 50000,

                                TransactionTimeout =
                                    TimeSpan.FromMinutes(1),

                                TablesPrefix = "Hangfire"
                            }));
            });

            services.AddHangfireServer(options =>
            {
                options.WorkerCount = 1;
            });

            services.AddHostedService<ProcessStopFinalizerBackgroundService>();
            services.AddTransient<DailyJob>();

            return services;
        }
    }
}
