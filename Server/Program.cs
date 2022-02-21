using System.Data;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.RegularExpressions;
using FusionBlog.Abstractions;
using FusionBlog.Server;
using FusionBlog.Server.Endpoints;
using FusionBlog.Services;
using FusionBlog.UI;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.OpenApi.Models;
using Stl.DependencyInjection;
using Stl.Fusion;
using Stl.Fusion.Blazor;
using Stl.Fusion.Bridge;
using Stl.Fusion.Client;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.Extensions;
using Stl.Fusion.Operations.Reprocessing;
using Stl.Fusion.Server;
using Stl.IO;
using tusdotnet;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Models.Concatenation;
using tusdotnet.Models.Configuration;
using tusdotnet.Models.Expiration;
using tusdotnet.Stores;

var builder = WebApplication.CreateBuilder(args);
builder.Host.ConfigureHostConfiguration(cfg =>
{
    cfg.Sources.Insert(0, new MemoryConfigurationSource()
    {
        InitialData = new Dictionary<string, string>()
        {
            { WebHostDefaults.ServerUrlsKey, "http://localhost:5005" },
        }
    })


    ;
});

//builder.WebHost.UseDefaultServiceProvider((ctx, options) =>
//{
//    if (ctx.HostingEnvironment.IsDevelopment())
//    {
//        options.ValidateScopes = true;
//        options.ValidateOnBuild = true;
//    }

//});
var Env = builder.Environment;
ILogger<Program> Log = NullLogger<Program>.Instance;
HostSettings? HostSettings = null;

builder.Services.AddSettings<HostSettings>();

ConfigureServices(builder.Services);

var app = builder.Build();


var Cfg = app.Configuration;

Configure(app, Log);
var dbContextFactory = app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
await using var dbContext = dbContextFactory.CreateDbContext();
// await dbContext.Database.EnsureDeletedAsync();
await dbContext.Database.EnsureCreatedAsync();


await app.RunAsync();


void ConfigureServices(IServiceCollection services)
{
    builder.Services.AddSingleton<TusDiskStore>(b =>
    {
        var baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
        var binCfgPart = Regex.Match(baseDir, @"[\\/]bin[\\/]\w+[\\/]").Value;
        var wwwRootPath = Path.Combine(Env.ContentRootPath, "..\\UI\\wwwroot");

        return new TusDiskStore(Path.Combine(wwwRootPath, "files"));
    });

    
    builder.Services.AddSingleton(CreateTusConfiguration);
    builder.Services.AddScoped<ITusUpload, TusUploadServer>();
    builder.Services.AddHostedService<ExpiredFilesCleanupService>();

    // Logging
    services.AddLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Information);
        if (Env.IsDevelopment())
        {
            logging.AddFilter(typeof(App).Namespace, LogLevel.Information);
            logging.AddFilter("Microsoft", LogLevel.Warning);
            logging.AddFilter("Microsoft.AspNetCore.Hosting", LogLevel.Information);
            logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Information);
            logging.AddFilter("Stl.Fusion.Operations", LogLevel.Information);
        }
    });

    services.AddSettings<HostSettings>();
#pragma warning disable ASP0000
    HostSettings = services.BuildServiceProvider().GetRequiredService<HostSettings>();
#pragma warning restore ASP0000

    // DbContext & related services
    var appTempDir = FilePath.GetApplicationTempDirectory("", true);
    var dbPath = appTempDir & "App2.db";
    services.AddDbContextFactory<AppDbContext>(dbContext =>
    {
        if (!string.IsNullOrEmpty(HostSettings.UseSqlServer))
            dbContext.UseSqlServer(HostSettings.UseSqlServer);
        else if (!string.IsNullOrEmpty(HostSettings.UsePostgreSql))
        {
            dbContext.UseNpgsql(HostSettings.UsePostgreSql);
            // dbContext.UseNpgsqlHintFormatter();
        }
        else
            dbContext.UseSqlite($"Data Source={dbPath}");

        if (Env.IsDevelopment())
            dbContext.EnableSensitiveDataLogging();
    });
    services.AddTransient(c => new DbOperationScope<AppDbContext>(c)
    {
        IsolationLevel = IsolationLevel.Serializable,
    });
    services.AddDbContextServices<AppDbContext>(dbContext =>
    {
        // This is the best way to add DbContext-related services from Stl.Fusion.EntityFramework
        dbContext.AddOperations((_, o) =>
        {
            // We use FileBasedDbOperationLogChangeMonitor, so unconditional wake up period
            // can be arbitrary long - all depends on the reliability of Notifier-Monitor chain.
            o.UnconditionalWakeUpPeriod = TimeSpan.FromSeconds(Env.IsDevelopment() ? 60 : 5);
        });
        var operationLogChangeAlertPath = dbPath + "_changed";
        dbContext.AddFileBasedOperationLogChangeTracking(operationLogChangeAlertPath);
        // dbContext.AddRedisDb("localhost", "Fusion.Samples.PostApp");
        // dbContext.AddRedisOperationLogChangeTracking();
        if (!HostSettings.UseInMemoryAuthService)
            dbContext.AddAuthentication<string>();
        dbContext.AddKeyValueStore();
    });

    // Fusion services
    services.AddSingleton(new Publisher.Options() { Id = HostSettings.PublisherId });
    var fusion = services.AddFusion();
    var fusionServer = fusion.AddWebServer();
    var fusionClient = fusion.AddRestEaseClient();
    var fusionAuth = fusion.AddAuthentication().AddServer(
        signInControllerOptionsBuilder: (_, options) =>
        {
            options.DefaultScheme = MicrosoftAccountDefaults.AuthenticationScheme;
        },
        authHelperOptionsBuilder: (_, options) => { options.NameClaimKeys = Array.Empty<string>(); });
    fusion.AddSandboxedKeyValueStore();
    fusion.AddOperationReprocessor();
    // You don't need to manually add TransientFailureDetector -
    // it's here only to show that operation reprocessor works
    // when PostService.AddOrUpdate throws this exception.
    // Database-related transient errors are auto-detected by
    // DbOperationScopeProvider<TDbContext> (it uses DbContext's
    // IExecutionStrategy to do this).
    services.TryAddEnumerable(ServiceDescriptor.Singleton(
        TransientFailureDetector.New(e => e is DbUpdateConcurrencyException)));

    // Compute service(s)
    fusion.AddComputeService<IPostService, PostService>();

    // Shared services
    StartupHelper.ConfigureSharedServices(services);

    // ASP.NET Core authentication providers
    services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = "oidc";
        }).AddCookie("Cookies", options =>
        {
            options.LoginPath = "/signIn";
            options.LogoutPath = "/signOut";
            options.Cookie.SameSite = SameSiteMode.Lax;
            if (Env.IsDevelopment())
                options.Cookie.SecurePolicy = CookieSecurePolicy.None;
        })
        .AddOpenIdConnect("oidc", options =>
        {
            options.Authority = "https://auth.utc.uz:44310/";
            options.ClientId = "TodoService";
            options.ClientSecret = "a4e4e19c-7a3d-8645-9287-f274fd35e34e";
            options.ResponseType = "code";
            options.NonceCookie.SecurePolicy = CookieSecurePolicy.Always;
            options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
            options.GetClaimsFromUserInfoEndpoint = true;
            options.MapInboundClaims = true;
            options.ResponseMode = "query";
            //options.TokenValidationParameters = new TokenValidationParameters {
            //    NameClaimType = JwtClaimTypes.Name,
            //    RoleClaimType = JwtClaimTypes.Role
            //};
            // options.CorrelationCookie.SameSite = SameSiteMode.Lax;

            // options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            // options.SignOutScheme = OpenIdConnectDefaults.AuthenticationScheme;

            // options.GetClaimsFromUserInfoEndpoint = true;

            options.Scope.Add("openid");
            options.Scope.Add("profile");
            // options.Scope.Add("email");
            options.Scope.Add("roles");
            options.Scope.Add("Auth_api");

            options.ClaimActions.MapJsonKey(ClaimTypes.Name, "Name");
            // options.ClaimActions.MapJsonKey("role", "role", "role"); //And this
            // options.TokenValidationParameters.RoleClaimType = "role"; //And als

            // options.SignInScheme = "Cookies";

            options.SaveTokens = true;


        })
        .AddMicrosoftAccount(options =>
        {
            options.ClientId = HostSettings.MicrosoftAccountClientId;
            options.ClientSecret = HostSettings.MicrosoftAccountClientSecret;
            // That's for personal account authentication flow
            options.AuthorizationEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize";
            options.TokenEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
            options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        }).AddGitHub(options =>
        {
            options.ClientId = HostSettings.GitHubClientId;
            options.ClientSecret = HostSettings.GitHubClientSecret;
            options.Scope.Add("read:user");
            options.Scope.Add("user:email");
            options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        });

    // Web
    services.AddRouting();
    services.AddMvc().AddApplicationPart(Assembly.GetExecutingAssembly());
    services.AddServerSideBlazor(o => o.DetailedErrors = true);
    fusionAuth.AddBlazor(o => { }); // Must follow services.AddServerSideBlazor()!

    // Swagger & debug tools
    services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "Templates.PostApp API",
            Version = "v1"
        });
    });
}


DefaultTusConfiguration CreateTusConfiguration(IServiceProvider serviceProvider)
{
    var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<Program>();
    


    // Change the value of EnableOnAuthorize in appsettings.json to enable or disable
    // the new authorization event.
    var enableAuthorize = serviceProvider.GetRequiredService<IOptions<OnAuthorizeOption>>().Value.EnableOnAuthorize;
    var store = serviceProvider.GetRequiredService<TusDiskStore>();
    
    return new DefaultTusConfiguration
    {
        UrlPath = "/files",
        Store =  store,
        MetadataParsingStrategy = MetadataParsingStrategy.AllowEmptyValues,
        UsePipelinesIfAvailable = true,
        Events = new Events
        {
            OnAuthorizeAsync = ctx =>
            {
                if (!enableAuthorize)
                    return Task.CompletedTask;

                if (ctx.HttpContext.User.Identity?.IsAuthenticated != true)
                {
                    ctx.HttpContext.Response.Headers.Add("WWW-Authenticate", new StringValues("Basic realm=tusdotnet-test-net6.0"));
                    ctx.FailRequest(HttpStatusCode.Unauthorized);
                    return Task.CompletedTask;
                }

                if (ctx.HttpContext.User.Identity.Name != "test")
                {
                    ctx.FailRequest(HttpStatusCode.Forbidden, "'test' is the only allowed user");
                    return Task.CompletedTask;
                }

                // Do other verification on the user; claims, roles, etc.

                // Verify different things depending on the intent of the request.
                // E.g.:
                //   Does the file about to be written belong to this user?
                //   Is the current user allowed to create new files or have they reached their quota?
                //   etc etc
                switch (ctx.Intent)
                {
                    case IntentType.CreateFile:
                        break;
                    case IntentType.ConcatenateFiles:
                        break;
                    case IntentType.WriteFile:
                        break;
                    case IntentType.DeleteFile:
                        break;
                    case IntentType.GetFileInfo:
                        break;
                    case IntentType.GetOptions:
                        break;
                    default:
                        break;
                }

                return Task.CompletedTask;
            },

            OnBeforeCreateAsync = ctx =>
            {
                // Partial files are not complete so we do not need to validate
                // the metadata in our example.
                if (ctx.FileConcatenation is FileConcatPartial)
                {
                    return Task.CompletedTask;
                }

                if (!ctx.Metadata.ContainsKey("name") || ctx.Metadata["name"].HasEmptyValue)
                {
                    ctx.FailRequest("name metadata must be specified. ");
                }

                if (!ctx.Metadata.ContainsKey("contentType") || ctx.Metadata["contentType"].HasEmptyValue)
                {
                    ctx.FailRequest("contentType metadata must be specified. ");
                }

                return Task.CompletedTask;
            },
            OnCreateCompleteAsync = ctx =>
            {
                logger.LogInformation($"Created file {ctx.FileId} using {ctx.Store.GetType().FullName}");
                return Task.CompletedTask;
            },
            OnBeforeDeleteAsync = ctx =>
            {
                // Can the file be deleted? If not call ctx.FailRequest(<message>);
                return Task.CompletedTask;
            },
            OnDeleteCompleteAsync = ctx =>
            {
                logger.LogInformation($"Deleted file {ctx.FileId} using {ctx.Store.GetType().FullName}");
                return Task.CompletedTask;
            },
            OnFileCompleteAsync = ctx =>
           {
               logger.LogInformation($"Upload of {ctx.FileId} completed using {ctx.Store.GetType().FullName}");
                // If the store implements ITusReadableStore one could access the completed file here.
                // The default TusDiskStore implements this interface:
                //var file = await ctx.GetFileAsync();
                // ITusFile file = await ctx.GetFileAsync();
                // Dictionary<string, Metadata> metadata = await file.GetMetadataAsync(ctx.CancellationToken);
                // Stream content = await file.GetContentAsync(ctx.CancellationToken);
                return Task.CompletedTask;
           }
        },
        // Set an expiration time where incomplete files can no longer be updated.
        // This value can either be absolute or sliding.
        // Absolute expiration will be saved per file on create
        // Sliding expiration will be saved per file on create and updated on each patch/update.
        Expiration = new AbsoluteExpiration(TimeSpan.FromMinutes(5))
    };
}

void Configure(IApplicationBuilder app, ILogger<Program> log)
{
    Log = log;

    // This server serves static content from Blazor Client,
    // and since we don't copy it to local wwwroot,
    // we need to find Client's wwwroot in bin/(Debug/Release) folder
    // and set it as this server's content root.
    var baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
    var binCfgPart = Regex.Match(baseDir, @"[\\/]bin[\\/]\w+[\\/]").Value;
    var wwwRootPath = Path.Combine(baseDir, "wwwroot");
    if (!Directory.Exists(Path.Combine(wwwRootPath, "_framework")))
        // This is a regular build, not a build produced w/ "publish",
        // so we remap wwwroot to the client's wwwroot folder
        wwwRootPath = Path.GetFullPath(Path.Combine(baseDir, $"../../../../UI/{binCfgPart}/net6.0/wwwroot"));
    Env.WebRootPath = wwwRootPath;
    Env.WebRootFileProvider = new PhysicalFileProvider(Env.WebRootPath);
    StaticWebAssetsLoader.UseStaticWebAssets(Env, Cfg);

    if (Env.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
        app.UseWebAssemblyDebugging();
    }
    else
    {
        app.UseExceptionHandler("/Error");
        app.UseHsts();
    }
    //app.UseHttpsRedirection();

    app.UseWebSockets(new WebSocketOptions()
    {
        KeepAliveInterval = TimeSpan.FromSeconds(30),
    });
    app.UseFusionSession();

    // Static + Swagger
    app.UseBlazorFrameworkFiles();
    app.UseStaticFiles();
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "API v1");
    });

    // API controllers
    app.UseRouting();
    //app.UseCookiePolicy();
    app.UseAuthentication();
    app.UseAuthorization();

    app.UseTus(httpContext => httpContext.RequestServices.GetRequiredService<DefaultTusConfiguration>());

    app.UseEndpoints(endpoints =>
    {
        endpoints.MapBlazorHub();
        endpoints.MapGet("/files/{fileId}", DownloadFileEndpoint.HandleRoute);
        endpoints.MapFusionWebSocketServer();
        endpoints.MapControllers();
        endpoints.MapFallbackToPage("/_Host");
    });
}

public enum HybridType
{
    ServerSide,
    WebAssembly,
    HybridManual,
    HybridOnNavigation,
    HybridOnReady
}

public class HybridOptions
{
    public HybridType HybridType { get; set; }
}


