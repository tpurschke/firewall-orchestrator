using System;
using System.Net;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using FWO.ApiClient;
using FWO.Ui.Auth;
using FWO.Middleware.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using FWO.Config;
using FWO.ApiConfig;
using FWO.Logging;
using FWO.Ui.Services;
using BlazorTable;

namespace FWO.Ui
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddRazorPages();
            services.AddServerSideBlazor();

            services.AddScoped<AuthenticationStateProvider, AuthStateProvider>();

            ConfigFile configConnection = new ConfigFile();

            string ApiUri = configConnection.ApiServerUri;
            string MiddlewareUri = configConnection.MiddlewareServerUri;
            string ProductVersion = configConnection.ProductVersion;

            services.AddScoped<APIConnection>(_ => new APIConnection(ApiUri));
            services.AddScoped<MiddlewareClient>(_ => new MiddlewareClient(MiddlewareUri));
            // create "anonymous" (empty) jwt

            MiddlewareClient middlewareClient = new MiddlewareClient(MiddlewareUri);
            APIConnection apiConn = new APIConnection(ApiUri);
            MiddlewareServerResponse createJWTResponse = middlewareClient.CreateInitialJWT().Result;
            if (createJWTResponse.Status == HttpStatusCode.BadRequest) 
            {
                Log.WriteError("Middleware Server Connection", $"Error while authenticating as anonymous user from UI.");
                Environment.Exit(1);
            }
            string jwt = createJWTResponse.GetResult<string>("jwt");
            apiConn.SetAuthHeader(jwt);
            //((AuthStateProvider)AuthService).AuthenticateUser(jwt);
            
            // get all non-confidential configuration settings and add to a global service (for all users)
            GlobalConfig globalConfig = new GlobalConfig(jwt);
            services.AddSingleton<GlobalConfig>(_ => globalConfig);
            
            services.AddScoped<UserConfig>(_ => new UserConfig(globalConfig));

            services.AddBlazorTable();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                // app.UseHsts();
            }

            // app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapBlazorHub();
                endpoints.MapFallbackToPage("/_Host");
            });
        }
    }
}
