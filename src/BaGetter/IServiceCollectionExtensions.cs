using BaGetter.Authentication;
using BaGetter.Core;
using BaGetter.Core.Authentication;
using BaGetter.Core.Configuration;
using BaGetter.Web.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Web;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;

namespace BaGetter;

internal static class IServiceCollectionExtensions
{
    internal static BaGetterApplication AddNugetBasicHttpAuthentication(this BaGetterApplication app)
    {
        app.Services.AddAuthentication(options =>
        {
            // Breaks existing tests if the contains check is not here.
            if (!options.SchemeMap.ContainsKey(AuthenticationConstants.NugetBasicAuthenticationScheme))
            {
                options.AddScheme<NugetBasicAuthenticationHandler>(AuthenticationConstants.NugetBasicAuthenticationScheme, AuthenticationConstants.NugetBasicAuthenticationScheme);
                options.DefaultAuthenticateScheme = AuthenticationConstants.NugetBasicAuthenticationScheme;
                options.DefaultChallengeScheme = AuthenticationConstants.NugetBasicAuthenticationScheme;
            }
        });

        return app;
    }

    internal static BaGetterApplication AddNugetBasicHttpAuthorization(this BaGetterApplication app, Action<AuthorizationPolicyBuilder>? configurePolicy = null)
    {
        app.Services.AddScoped<IAuthorizationHandler, FeedPermissionHandler>();

        app.Services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthenticationConstants.NugetUserPolicy, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.Requirements.Add(new FeedPermissionRequirement(FeedPermissionRequirement.Pull));
                configurePolicy?.Invoke(policy);
            });
        });

        return app;
    }

    /// <summary>
    /// Registers Entra ID (OIDC) authentication with cookie-based session when the
    /// authentication mode includes Entra (Entra or Hybrid).
    /// </summary>
    internal static BaGetterApplication AddEntraAuthentication(
        this BaGetterApplication app,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        // Read the authentication mode from configuration to decide whether to register Entra
        var authSection = configuration.GetSection("Authentication");
        var modeString = authSection?.GetValue<string>("Mode");

        if (!Enum.TryParse<AuthenticationMode>(modeString, ignoreCase: true, out var mode))
            mode = AuthenticationMode.None;

        if (mode is not (AuthenticationMode.Entra or AuthenticationMode.Hybrid))
            return app;

        var entraSection = authSection.GetSection("Entra");

        app.Services.AddAuthentication(options =>
            {
                // Keep NugetBasicAuth as the default for NuGet feed API requests.
                // OIDC + Cookie are used for interactive browser sessions only.
                options.DefaultScheme = AuthenticationConstants.NugetBasicAuthenticationScheme;
            })
            .AddMicrosoftIdentityWebApp(entraSection, AuthenticationConstants.EntraOidcScheme, AuthenticationConstants.CookieScheme);

        // When a request has the session cookie but no Authorization header (i.e. a browser
        // session after OIDC sign-in), forward authentication to the cookie scheme so the
        // identity is actually read. Without this the default NugetBasicAuth handler sees no
        // Authorization header and returns NoResult, leaving the user unauthenticated.
        app.Services.Configure<AuthenticationSchemeOptions>(
            AuthenticationConstants.NugetBasicAuthenticationScheme, options =>
        {
            options.ForwardDefaultSelector = context =>
            {
                if (!context.Request.Headers.ContainsKey("Authorization")
                    && context.Request.Cookies.ContainsKey("BaGetter.Auth"))
                {
                    return AuthenticationConstants.CookieScheme;
                }
                return null;
            };
        });

        // Configure the cookie scheme registered by AddMicrosoftIdentityWebApp
        app.Services.Configure<CookieAuthenticationOptions>(AuthenticationConstants.CookieScheme, options =>
        {
            options.LoginPath = "/Login";
            options.LogoutPath = "/Logout";
            options.AccessDeniedPath = "/Login";
            options.Cookie.Name = "BaGetter.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;
            options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
            options.SlidingExpiration = true;

            options.Events ??= new CookieAuthenticationEvents();
            options.Events.OnValidatePrincipal = async context =>
            {
                var userIdClaim = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(AuthenticationConstants.CookieScheme);
                    return;
                }

                var userService = context.HttpContext.RequestServices.GetRequiredService<IUserService>();
                var user = await userService.FindByIdAsync(userId, context.HttpContext.RequestAborted);
                if (user == null || !user.IsEnabled || !user.CanLoginToUI)
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(AuthenticationConstants.CookieScheme);
                }
            };
        });

        // Configure the OIDC options for App Role-based authentication
        app.Services.Configure<OpenIdConnectOptions>(AuthenticationConstants.EntraOidcScheme, options =>
        {
            // Use authorization code flow instead of implicit flow so that
            // "ID tokens" does not need to be enabled under Implicit grant
            // in the Azure app registration.
            options.ResponseType = Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectResponseType.Code;

            options.TokenValidationParameters.NameClaimType = "name";
            options.TokenValidationParameters.RoleClaimType = "roles";

            options.Events ??= new OpenIdConnectEvents();

            options.Events.OnRemoteFailure = context =>
            {
                var errorMessage = context.Failure?.Message ?? "Authentication failed.";
                context.Response.Redirect($"/Login?error={Uri.EscapeDataString(errorMessage)}");
                context.HandleResponse();
                return Task.CompletedTask;
            };

            var existingOnTokenValidated = options.Events.OnTokenValidated;

            options.Events.OnTokenValidated = async context =>
            {
                if (existingOnTokenValidated != null)
                    await existingOnTokenValidated(context);

                var syncService = context.HttpContext.RequestServices.GetRequiredService<EntraRoleSyncService>();
                try
                {
                    await syncService.OnTokenValidatedAsync(context.Principal, context.HttpContext.RequestAborted);
                }
                catch (UnauthorizedAccessException ex)
                {
                    context.Fail(ex.Message);
                }
            };
        });

        app.Services.AddScoped<EntraRoleSyncService>();

        return app;
    }
}
