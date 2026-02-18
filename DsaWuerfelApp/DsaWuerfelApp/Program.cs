using DsaWuerfelApp.Hubs;
using DsaWuerfelApp.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddSingleton<SessionService>();
builder.Services.AddSingleton<DiceService>();

// CORS hinzufügen
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

app.UseHttpsRedirection();

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseRouting();

// CORS aktivieren
app.UseCors("AllowAll");

app.MapControllers();
app.MapHub<GameHub>("/gamehub"); 
app.MapFallbackToFile("index.html"); 

app.Run();