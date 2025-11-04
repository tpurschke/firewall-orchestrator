using BlazorTable;
using FWO.Api.Client;
using FWO.Config.Api;
using FWO.Config.Api.Data;
using FWO.Config.File;
using FWO.Logging;
using FWO.Middleware.Client;
using FWO.Services;
using FWO.Services.EventMediator;
using FWO.Services.EventMediator.Interfaces;
using FWO.Services.RuleTreeBuilder;
using FWO.Ui.Auth;
using FWO.Ui.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using RestSharp;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

Log.WriteInfo("Startup", "Starting FWO UI Server...");

var builder = WebApplication.CreateBuilder(args);
ConfigureWebHost(builder);

string apiUri = ConfigFile.ApiServerUri;
string middlewareUri = ConfigFile.MiddlewareServerUri;

GlobalConfig globalConfig = InitializeGlobalConfig(apiUri, middlewareUri);
bool ssoConfigured = ConfigureServices(builder, globalConfig, apiUri, middlewareUri);

var app = builder.Build();
FWO.Services.ServiceProvider.UiServices = app.Services;

ConfigureRequestPipeline(app, globalConfig, ssoConfigured);

app.Run();

static void ConfigureWebHost(WebApplicationBuilder builder)
{
    builder.WebHost
        .UseWebRoot("wwwroot")
        .UseStaticWebAssets();

    builder.WebHost.ConfigureKestrel(serverOptions =>
    {
        serverOptions.ListenLocalhost(5000);
    });
}

static GlobalConfig InitializeGlobalConfig(string apiUri, string middlewareUri)
{
    var middlewareClient = new MiddlewareClient(middlewareUri);
    var apiConnection = new GraphQlApiConnection(apiUri);

    string jwt = RetrieveInitialJwt(middlewareClient);
    apiConnection.SetAuthHeader(jwt);

    return Task.Run(async () => await GlobalConfig.ConstructAsync(jwt, true, true)).Result;
}

static string RetrieveInitialJwt(MiddlewareClient middlewareClient)
{
    RestResponse<string> response = middlewareClient.CreateInitialJWT().Result;
    int attempt = 1;

    while (!response.IsSuccessful)
    {
        LogInitialJwtError(response, attempt);
        Thread.Sleep(500 * attempt++);
        response = middlewareClient.CreateInitialJWT().Result;
    }

    return response.Data ?? throw new NullReferenceException("Received empty jwt.");
}

static void LogInitialJwtError(RestResponse<string> response, int attempt)
{
    Log.WriteError("Middleware Server Connection",
        $"Error while authenticating as anonymous user from UI (Attempt {attempt}), " +
        $"Uri: {response.ResponseUri?.AbsoluteUri}, " +
        $"HttpStatus: {response.StatusDescription}, " +
        $"Error: {response.ErrorMessage}");
}

static bool ConfigureServices(WebApplicationBuilder builder, GlobalConfig globalConfig, string apiUri, string middlewareUri)
{
    ConfigureCors(builder.Services);
    ConfigureFrameworkServices(builder.Services);
    ConfigureScopedServices(builder.Services, globalConfig, apiUri, middlewareUri);
    bool ssoConfigured = ConfigureAuthentication(builder.Services, globalConfig);
    builder.Services.AddSingleton(new AdfsRuntimeOptions(ssoConfigured));
    return ssoConfigured;
}

static void ConfigureCors(IServiceCollection services)
{
    services.AddCors(options =>
    {
        options.AddPolicy("AllowRemoteOrigins", policy =>
        {
            policy.WithOrigins(ConfigFile.RemoteAddresses);
        });
    });
}

static void ConfigureFrameworkServices(IServiceCollection services)
{
    services.AddRazorPages();
    services.AddServerSideBlazor();
    services.AddAuthorization();
    services.AddHttpContextAccessor();
    services.AddBlazorTable();
}

static void ConfigureScopedServices(IServiceCollection services, GlobalConfig globalConfig, string apiUri, string middlewareUri)
{
    services.AddScoped<AuthenticationStateProvider, AuthStateProvider>();
    services.AddScoped<CircuitHandler, CircuitHandlerService>();
    services.AddScoped<KeyboardInputService, KeyboardInputService>();
    services.AddScoped<IEventMediator, EventMediator>();
    services.AddTransient<IRuleTreeBuilder, RuleTreeBuilder>();
    services.AddScoped<ApiConnection>(_ => new GraphQlApiConnection(apiUri));
    services.AddScoped<MiddlewareClient>(_ => new MiddlewareClient(middlewareUri));
    services.AddSingleton<GlobalConfig>(_ => globalConfig);
    services.AddSingleton<IUrlSanitizer, UrlSanitizer>();
    services.AddScoped<UserConfig>(_ => new UserConfig(globalConfig));
    services.AddScoped(_ => new NetworkZoneService());
    services.AddScoped(_ => new DomEventService());
}

static bool ConfigureAuthentication(IServiceCollection services, GlobalConfig globalConfig)
{
    AdfsSettings adfsSettings = globalConfig.AdfsSettings;

    bool openIdConfigured = false;
    Uri? authorityUri = null;
    Uri? metadataUri = null;

    if (adfsSettings.Enabled)
    {
        if (!Uri.TryCreate(adfsSettings.Authority, UriKind.Absolute, out authorityUri))
        {
            Log.WriteError("ADFS Configuration", $"Invalid authority URI configured: '{adfsSettings.Authority}'. Single sign-on is disabled.");
        }
        else
        {
            metadataUri = TryGetAbsoluteUri(adfsSettings.MetadataAddress, "metadata address");

            if (!TryEnsureHostResolvable(authorityUri, "authority"))
            {
                authorityUri = null;
            }
            else if (metadataUri is not null && !TryEnsureHostResolvable(metadataUri, "metadata address"))
            {
                metadataUri = null;
            }

            openIdConfigured = authorityUri is not null;
        }
    }

    var authenticationBuilder = services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = openIdConfigured
            ? OpenIdConnectDefaults.AuthenticationScheme
            : CookieAuthenticationDefaults.AuthenticationScheme;
    });

    authenticationBuilder.AddCookie();

    if (!openIdConfigured || authorityUri is null)
    {
        return false;
    }

    bool requireHttpsMetadata = ShouldRequireHttpsMetadata(authorityUri!, metadataUri);

    authenticationBuilder.AddOpenIdConnect(options => ConfigureOpenIdConnectOptions(options, adfsSettings, authorityUri!, metadataUri, requireHttpsMetadata));
    return true;
}

static void ConfigureOpenIdConnectOptions(OpenIdConnectOptions options, AdfsSettings adfsSettings, Uri authorityUri, Uri? metadataUri, bool requireHttpsMetadata)
{
    options.RequireHttpsMetadata = requireHttpsMetadata;
    options.Authority = authorityUri.AbsoluteUri;

    if (metadataUri is not null)
    {
        options.MetadataAddress = metadataUri.AbsoluteUri;
    }

    options.ClientId = adfsSettings.ClientId;

    if (!string.IsNullOrWhiteSpace(adfsSettings.ClientSecret))
    {
        options.ClientSecret = adfsSettings.ClientSecret;
        options.ResponseType = OpenIdConnectResponseType.Code;
    }
    else
    {
        options.ResponseType = OpenIdConnectResponseType.IdToken;
    }

    options.SaveTokens = true;

    if (!string.IsNullOrWhiteSpace(adfsSettings.CallbackPath))
    {
        options.CallbackPath = adfsSettings.CallbackPath;
    }

    if (!string.IsNullOrWhiteSpace(adfsSettings.SignedOutCallbackPath))
    {
        options.SignedOutCallbackPath = adfsSettings.SignedOutCallbackPath;
    }

    options.UsePkce = false;
    options.Scope.Clear();

    if (adfsSettings.Scopes.Length == 0)
    {
        options.Scope.Add("openid");
        return;
    }

    foreach (string scope in adfsSettings.Scopes)
    {
        options.Scope.Add(scope);
    }
}

static Uri? TryGetAbsoluteUri(string? candidate, string description)
{
    if (string.IsNullOrWhiteSpace(candidate))
    {
        return null;
    }

    if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri))
    {
        return uri;
    }

    Log.WriteWarning("ADFS Configuration", $"The {description} '{candidate}' is not a valid absolute URI and will be ignored.");
    return null;
}

static bool ShouldRequireHttpsMetadata(Uri authorityUri, Uri? metadataUri)
{
    if (!string.Equals(authorityUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    if (metadataUri is not null &&
        !string.Equals(metadataUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    return true;
}

static bool TryEnsureHostResolvable(Uri uri, string description)
{
    try
    {
        _ = Dns.GetHostEntry(uri.Host);
        return true;
    }
    catch (SocketException exception)
    {
        Log.WriteWarning("ADFS Configuration", $"Host resolution failed for {description} '{uri}'. Single sign-on is disabled. ({exception.Message})");
        return false;
    }
}

static void ConfigureRequestPipeline(WebApplication app, GlobalConfig globalConfig, bool ssoConfigured)
{
    LogEnvironment(app);
    ConfigureExceptionHandling(app);

    app.UseStaticFiles();
    app.UseRouting();
    ConfigureUrlSanitizer(app);

    app.UseAuthentication();
    app.UseAuthorization();

    MapAuthenticationEndpoints(app, globalConfig, ssoConfigured);

    app.MapBlazorHub();
    app.MapFallbackToPage("/_Host");
}

static void LogEnvironment(WebApplication app)
{
    Log.WriteInfo("Environment", $"{app.Environment.ApplicationName} runs in {app.Environment.EnvironmentName} Mode.");
}

static void ConfigureExceptionHandling(WebApplication app)
{
    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
        return;
    }

    app.UseExceptionHandler("/Error");
}

static void ConfigureUrlSanitizer(WebApplication app)
{
    app.UseWhen(
        ctx => !ctx.Request.Path.StartsWithSegments("/_blazor") &&
               !ctx.Request.Path.StartsWithSegments("/_framework") &&
               !ctx.Request.Path.StartsWithSegments("/css") &&
               !ctx.Request.Path.StartsWithSegments("/js") &&
               !ctx.Request.Path.StartsWithSegments("/images"),
        branch =>
        {
            branch.UseMiddleware<UrlSanitizerMiddleware>();
        });
}

static void MapAuthenticationEndpoints(WebApplication app, GlobalConfig globalConfig, bool ssoConfigured)
{
    app.MapGet("/authentication/login", context => HandleLoginAsync(context, globalConfig, ssoConfigured));
    app.MapGet("/authentication/logout", context => HandleLogoutAsync(context, globalConfig, ssoConfigured));
}

static async Task HandleLoginAsync(HttpContext context, GlobalConfig globalConfig, bool ssoConfigured)
{
    if (!ssoConfigured)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    string returnUrl = ExtractReturnUrl(context);

    if (context.User?.Identity?.IsAuthenticated == true)
    {
        context.Response.Redirect(returnUrl);
        return;
    }

    await context.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme, new AuthenticationProperties { RedirectUri = returnUrl });
}

static async Task HandleLogoutAsync(HttpContext context, GlobalConfig globalConfig, bool ssoConfigured)
{
    if (!ssoConfigured)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    string returnUrl = ExtractReturnUrl(context);

    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    await context.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme, new AuthenticationProperties { RedirectUri = returnUrl });
}

static string ExtractReturnUrl(HttpContext context)
{
    return context.Request.Query.TryGetValue("returnUrl", out var values) && !string.IsNullOrWhiteSpace(values)
        ? values.ToString()
        : "/login";
}

internal sealed class AdfsRuntimeOptions
{
    public bool IsConfigured { get; }

    public AdfsRuntimeOptions(bool isConfigured)
    {
        IsConfigured = isConfigured;
    }
}
