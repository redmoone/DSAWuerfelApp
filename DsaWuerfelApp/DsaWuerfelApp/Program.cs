using DsaWuerfelApp.Services;
using DsaWuerfelApp.Components;
using _Imports = DsaWuerfelApp.Client._Imports;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<DiceService>();

builder.Services.AddCors(o => o.AddPolicy("dev", p =>
    p.WithOrigins("https://127.0.0.1:55059", "http://127.0.0.1:55058",
            "https://localhost:55059", "http://localhost:55058")
        .AllowAnyHeader()
        .AllowAnyMethod()
));

builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseCors("dev");        
app.UseAntiforgery();

app.MapControllers();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(_Imports).Assembly);

app.Run();