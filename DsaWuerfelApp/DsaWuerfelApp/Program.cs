using DsaWuerfelApp.Hubs;
using DsaWuerfelApp.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSignalR();    

builder.Services.AddSingleton<SessionService>(); 
builder.Services.AddSingleton<DiceService>();   

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



app.MapHub<GameHub>("/gamehub"); 
app.MapControllers();           
app.MapFallbackToFile("index.html");

app.Run();