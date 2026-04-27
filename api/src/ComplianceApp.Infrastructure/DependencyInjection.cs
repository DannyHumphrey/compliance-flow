using System.Text;
using ComplianceApp.Application.Common.Authentication;
using ComplianceApp.Application.Common.Persistence;
using ComplianceApp.Infrastructure.Authentication;
using ComplianceApp.Infrastructure.Persistence;
using ComplianceApp.Infrastructure.Persistence.Interceptors;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace ComplianceApp.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Wires Infrastructure services: persistence (EF Core + Postgres),
    /// the current-user resolver, JWT bearer auth, and the dev token
    /// issuer (Development only). Refuses to start if dev auth is enabled
    /// outside Development.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        AddPersistence(services, configuration);
        AddDevAuth(services, configuration, environment);
        AddJwtBearer(services, configuration, environment);

        services.AddAuthorization();

        return services;
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<AuditableEntityInterceptor>();

        var connectionString = configuration.GetConnectionString("DefaultConnection");

        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString);
            options.AddInterceptors(sp.GetRequiredService<AuditableEntityInterceptor>());
        });

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<IUnitOfWork, UnitOfWork>();
    }

    private static void AddDevAuth(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services
            .AddOptions<DevAuthOptions>()
            .Bind(configuration.GetSection(DevAuthOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                opts => !opts.Enabled || environment.IsDevelopment(),
                "DevAuth.Enabled must be false outside the Development environment.")
            .ValidateOnStart();

        var devAuthOptions =
            configuration.GetSection(DevAuthOptions.SectionName).Get<DevAuthOptions>()
            ?? new DevAuthOptions();

        if (devAuthOptions.Enabled)
        {
            services.AddSingleton<IDevTokenIssuer, DevTokenIssuer>();
        }
    }

    private static void AddJwtBearer(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var devAuthOptions =
            configuration.GetSection(DevAuthOptions.SectionName).Get<DevAuthOptions>()
            ?? new DevAuthOptions();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = !environment.IsDevelopment();
                options.SaveToken = true;
                // Keep "sub" as "sub" rather than the legacy ClaimTypes.NameIdentifier URI.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = devAuthOptions.Issuer,
                    ValidAudience = devAuthOptions.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(
                            string.IsNullOrEmpty(devAuthOptions.SigningKey)
                                ? new string('x', 32)
                                : devAuthOptions.SigningKey)),
                    NameClaimType = ComplianceAppClaimTypes.Subject,
                };
            });
    }
}
