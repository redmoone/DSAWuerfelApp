using DsaWuerfelApp.Client;
using DsaWuerfelApp.Client.Services;

using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.Services.AddSingleton<GameClient>();
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddMudServices();
builder.Services.AddScoped<IHeroApiClient, HeroApiClient>();
builder.Services.AddScoped<IWuerfelApiClient, WuerfelApiClient>();
builder.Services.AddScoped<ActiveHeroState>();
builder.Services.AddScoped<WuerfelState>();
builder.Services.AddScoped<WuerfelUiOperationRunner>();
builder.Services.AddScoped<WuerfelSelectionService>();
builder.Services.AddScoped<WuerfelContextService>();
builder.Services.AddScoped<WuerfelContextSubscription>();
builder.Services.AddScoped<WuerfelSignalREventBridge>();
builder.Services.AddScoped<WuerfelRollCommandDispatcher>();
builder.Services.AddScoped<IWuerfelRollDispatchStrategy, SessionWuerfelRollDispatchStrategy>();
builder.Services.AddScoped<IWuerfelRollDispatchStrategy, ApiWuerfelRollDispatchStrategy>();
builder.Services.AddScoped<WuerfelFacade>();


await builder.Build().RunAsync();