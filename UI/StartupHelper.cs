using FusionBlog.Abstractions;
using FusionBlog.Abstractions.Clients;
using FusionBlog.UI.Tus;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Stl.Fusion.Blazor;
using Stl.Fusion.Client;
using Stl.Fusion.Extensions;
using Stl.Fusion.UI;
using MudBlazor.Services;


namespace FusionBlog.UI;

public static class StartupHelper
{
    public static void ConfigureServices(IServiceCollection services, WebAssemblyHostBuilder builder)
    {
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Logging.AddFilter(typeof(App).Namespace, LogLevel.Information);
        services.AddOptions();
        services.AddAuthorizationCore();
        var baseUri = new Uri(builder.HostEnvironment.BaseAddress);
        var apiBaseUri = new Uri($"{baseUri}api/");

        // Fusion services
        var fusion = services.AddFusion();
        var fusionClient = fusion.AddRestEaseClient((_, o) => {
            o.BaseUri = baseUri;
            o.IsLoggingEnabled = true;
            o.IsMessageLoggingEnabled = false;
        });
        fusionClient.ConfigureHttpClientFactory((c, name, o) => {
            var isFusionClient = (name ?? "").StartsWith("Stl.Fusion");
            var clientBaseUri = isFusionClient ? baseUri : apiBaseUri;
            o.HttpClientActions.Add(client => client.BaseAddress = clientBaseUri);
        });
        fusion.AddAuthentication().AddRestEaseClient().AddBlazor();

        // Option 1: Client-side SimpleTodoService (no RPC)
        // fusion.AddComputeService<ITodoService, SimpleTodoService>();

        // Option 2: Client-side TodoService and SandboxedKeyValueStore using InMemoryKeyValueStore (no RPC)
        // fusion.AddInMemoryKeyValueStore();
        // fusion.AddSandboxedKeyValueStore();
        // fusion.AddComputeService<ITodoService, TodoService>();

        // Option 3: Client-side TodoService + remote SandboxedKeyValueStore -> DbKeyValueStore
        // fusionClient.AddReplicaService<ISandboxedKeyValueStore, ISandboxedKeyValueStoreClientDef>();
        // fusion.AddComputeService<ITodoService, TodoService>();

        // Option 4: Remote TodoService, SandboxedKeyValueStore, and DbKeyValueStore
        fusionClient.AddReplicaService<IPostService, IPostClientDef>();

        ConfigureSharedServices(services);
    }

    public static void ConfigureSharedServices(IServiceCollection services)
    {

        services.AddHttpClient();
        services.AddScoped<TusUploadHelper>();
        // Other UI-related services
        var fusion = services.AddFusion();
        fusion.AddBlazorUIServices();
        fusion.AddFusionTime();
        fusion.AddBackendStatus();

        services.AddMudServices();



        // Default update delay is 0.5s
        services.AddTransient<IUpdateDelayer>(c => new UpdateDelayer(c.UICommandTracker(), 0.5));
    }
}
