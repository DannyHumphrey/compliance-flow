using System.Text;
using ComplianceApp.Application.Common.Authentication;
using ComplianceApp.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace ComplianceApp.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Wires Infrastructure services: current-user resolver, JWT bearer auth,
    /// and (in Development) the dev token issuer. Refuses to start if dev auth
    /// is enabled outside Development.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        // Bind + validate DevAuthOptions
        services
            .AddOptions<DevAuthOptions>()
            .Bind(configuration.GetSection(DevAuthOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                opts => !opts.Enabled || environment.IsDevelopment(),
                "DevAuth.Enabled must be false outside the Development environment.")
            .ValidateOnStart();

        var devAuthSection = configuration.GetSection(DevAuthOptions.SectionName);
        var devAuthOptions = devAuthSection.Get<DevAuthOptions>() ?? new DevAuthOptions();

        if (devAuthOptions.Enabled)
        {
            services.AddSingleton<IDevTokenIssuer, DevTokenIssuer>();
        }

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

        services.AddAuthorization();

        return services;
    }
}
