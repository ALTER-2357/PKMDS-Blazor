using Pkmds.Rcl.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
var services = builder.Services;

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

services
    .AddMudServices(config =>
    {
        config.SnackbarConfiguration.PreventDuplicates = false;
        config.SnackbarConfiguration.ClearAfterNavigation = true;
    });

services
    .AddSingleton(_ => new HttpClient { BaseAddress = new(builder.HostEnvironment.BaseAddress) })
    .AddFileSystemAccessService()
    .AddSingleton<IAppState, AppState>()
    .AddSingleton<IRefreshService, RefreshService>()
    .AddSingleton<IAppService, AppService>()
    .AddSingleton<JsService>()
    .AddSingleton<BlazorAesProvider>()
    .AddSingleton<BlazorMd5Provider>()
    // Register the backup service used by MainLayout:
    .AddScoped<IBackupService, BackupService>();

var app = builder.Build();

// Although Blazor WASM can target the whole .NET Framework API surface,
// during the Runtime, Microsoft has disabled the native support to some APIs under the System.Security.Cryptography namespace
// During startup we replace PKHeX unsupported cryptography APIs with a javascript-based alternative 
RuntimeCryptographyProvider.Aes = app.Services.GetRequiredService<BlazorAesProvider>();
RuntimeCryptographyProvider.Md5 = app.Services.GetRequiredService<BlazorMd5Provider>();

await app.RunAsync();
