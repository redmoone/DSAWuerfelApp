using DsaWuerfelApp.Hubs;
using DsaWuerfelApp.Services;
using Microsoft.AspNetCore.StaticFiles;

var builder = WebApplication.CreateBuilder(args);
var provider = new FileExtensionContentTypeProvider
{
    Mappings =
    {
        [".glb"] = "model/gltf-binary"
    }
};

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddSingleton<SessionService>();
builder.Services.AddSingleton<DiceService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        policy =>
        {
            policy.AllowAnyOrigin()
                .AllowAnyHeader()
                .AllowAnyMethod();
        });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseHsts();
}

app.UseCors("AllowAll");

app.UseHttpsRedirection();

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = provider
});

app.UseRouting();

app.MapControllers();
app.MapHub<GameHub>("/gamehub"); 
app.MapFallbackToFile("index.html"); 

app.Run();