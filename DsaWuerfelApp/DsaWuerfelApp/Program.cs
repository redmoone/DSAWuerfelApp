using DsaWuerfelApp.Hubs;
using DsaWuerfelApp.Persistence;
using DsaWuerfelApp.Services;

using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var provider = new FileExtensionContentTypeProvider { Mappings = { [".glb"] = "model/gltf-binary" } };

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddSingleton<SessionService>();
builder.Services.AddSingleton<DiceService>();
builder.Services.AddTransient<XmlHeroDeserializer>();
builder.Services.AddDbContext<HeroDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("HeroesDb")));

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

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<HeroDbContext>();
    dbContext.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors("AllowAll");

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = provider });

app.UseRouting();

app.MapControllers();
app.MapHub<GameHub>("/gamehub");
app.MapFallbackToFile("index.html");

app.Run();