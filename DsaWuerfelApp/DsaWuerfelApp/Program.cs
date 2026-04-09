using DsaWuerfelApp.Core.Mappers;
using DsaWuerfelApp.Hubs;
using DsaWuerfelApp.Persistence;
using DsaWuerfelApp.Services;
using DsaWuerfelApp.Services.Application.Import;
using DsaWuerfelApp.Services.Auth;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var provider = new FileExtensionContentTypeProvider { Mappings = { [".glb"] = "model/gltf-binary" } };
var authSessionLifetimeDays = Math.Max(1, builder.Configuration.GetValue<int?>("AuthSession:LifetimeDays") ?? 180);
var javaMicroserviceBaseUrl = builder.Configuration["JavaMicroservice:BaseUrl"] ?? "http://localhost:8080";
var javaMicroserviceTimeoutSeconds = builder.Configuration.GetValue<int?>("JavaMicroservice:TimeoutSeconds") ?? 30;
var dataProtectionApplicationName = builder.Configuration["DataProtection:ApplicationName"] ?? "DsaWuerfelApp";
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    dataProtectionKeysPath = builder.Environment.IsDevelopment()
        ? Path.Combine(builder.Environment.ContentRootPath, "artifacts", "data-protection-keys")
        : "/var/lib/dsawuerfelapp/data-protection-keys";
}

Directory.CreateDirectory(dataProtectionKeysPath);

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
    .SetApplicationName(dataProtectionApplicationName);
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "dsa.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(authSessionLifetimeDays);
    });
builder.Services.AddAuthorization();
builder.Services.Configure<MagicLinkAuthOptions>(builder.Configuration.GetSection(MagicLinkAuthOptions.SectionName));
builder.Services.AddHttpClient<IMagicLinkEmailSender, ResendMagicLinkEmailSender>(client =>
{
    client.BaseAddress = new Uri("https://api.resend.com/");
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SessionRuntimeState>();
builder.Services.AddSingleton<SessionRecordStore>();
builder.Services.AddSingleton<SessionService>();
builder.Services.AddSingleton<DiceService>();
builder.Services.AddSingleton<TalentProbeService>();
builder.Services.AddSingleton<AttributeProbeService>();
builder.Services.AddSingleton<SchlechteEigenschaftProbeService>();
builder.Services.AddSingleton<TalentCatalogStore>();
builder.Services.AddSingleton<SpellCatalogStore>();
builder.Services.AddSingleton<TalentCatalogService>();
builder.Services.AddScoped<IHeroReadRepository, HeroReadRepository>();
builder.Services.AddScoped<HeroContextReader>();
builder.Services.AddScoped<BadTraitResolver>();
builder.Services.AddScoped<GetDicePageContextHandler>();
builder.Services.AddScoped<GetProbeInfoHandler>();
builder.Services.AddScoped<RollFreeHandler>();
builder.Services.AddScoped<RollTalentHandler>();
builder.Services.AddScoped<RollAttributeHandler>();
builder.Services.AddScoped<RollBadTraitHandler>();
builder.Services.AddScoped<DiceWorkflowService>();
builder.Services.AddScoped<MagicLinkService>();
builder.Services.AddSingleton<GameSessionRollPipeline>();
builder.Services.AddTransient<HeroAttributesMapper>();
builder.Services.AddTransient<HeroBadTraitsMapper>();
builder.Services.AddTransient<HeroTalentsMapper>();
builder.Services.AddTransient<HeroSpellsMapper>();
builder.Services.AddTransient<HeroMapper>();
builder.Services.AddTransient<XmlHeroDeserializer>();
builder.Services.AddTransient<HeroImportService>();
builder.Services.AddTransient<HeroReimportService>();
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
builder.Services.AddHttpClient("JavaMicroservice", client =>
{
    client.BaseAddress = new Uri(javaMicroserviceBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(javaMicroserviceTimeoutSeconds);
});
var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<HeroDbContext>();
    dbContext.Database.EnsureCreated();
    EnsureHeroSchema(dbContext);
    EnsureAuthSchema(dbContext);
    EnsureSessionSchema(dbContext);

    var heroReimportService = scope.ServiceProvider.GetRequiredService<HeroReimportService>();
    await heroReimportService.UpgradeStoredHeroesAsync();
}

if (app.Environment.IsDevelopment())
{
    if (builder.Configuration.GetValue("WebAssemblyDebugging:Enabled", false))
    {
        app.UseWebAssemblyDebugging();
    }
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
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<GameHub>("/gamehub");
app.MapFallbackToFile("index.html");

app.Run();

static void EnsureHeroSchema(HeroDbContext dbContext)
{
    using var connection = dbContext.Database.GetDbConnection();
    connection.Open();

    using var command = connection.CreateCommand();
    command.CommandText = "PRAGMA table_info('Heroes');";

    using var reader = command.ExecuteReader();
    var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    while (reader.Read())
    {
        existingColumns.Add(reader.GetString(1));
    }

    reader.Close();

    var requiredColumns = new (string Name, string Sql)[]
    {
        ("IsActive", "ALTER TABLE Heroes ADD COLUMN IsActive INTEGER NOT NULL DEFAULT 0;"), ("SchlechteEigenschaften",
            "ALTER TABLE Heroes ADD COLUMN SchlechteEigenschaften TEXT NOT NULL DEFAULT '{{}}';"),
        ("Zauber", "ALTER TABLE Heroes ADD COLUMN Zauber TEXT NOT NULL DEFAULT '{{}}';"),
        ("SourceXml", "ALTER TABLE Heroes ADD COLUMN SourceXml BLOB NULL;"),
        ("SourceFileName", "ALTER TABLE Heroes ADD COLUMN SourceFileName TEXT NULL;"),
        ("ImportVersion", "ALTER TABLE Heroes ADD COLUMN ImportVersion INTEGER NOT NULL DEFAULT 0;"),
        ("ImportedAtUtc", "ALTER TABLE Heroes ADD COLUMN ImportedAtUtc TEXT NULL;")
    };

    foreach (var (_, sql) in requiredColumns.Where(column => !existingColumns.Contains(column.Name)))
    {
        dbContext.Database.ExecuteSqlRaw(sql);
    }
}

static void EnsureAuthSchema(HeroDbContext dbContext)
{
    dbContext.Database.ExecuteSqlRaw(
        """
        CREATE TABLE IF NOT EXISTS AuthUsers (
            Id TEXT NOT NULL PRIMARY KEY,
            Email TEXT NOT NULL,
            DisplayName TEXT NULL,
            CreatedAtUtc TEXT NOT NULL,
            LastLoginAtUtc TEXT NOT NULL
        );
        """);

    dbContext.Database.ExecuteSqlRaw(
        """
        CREATE UNIQUE INDEX IF NOT EXISTS IX_AuthUsers_Email
        ON AuthUsers (Email);
        """);

    dbContext.Database.ExecuteSqlRaw(
        """
        CREATE TABLE IF NOT EXISTS MagicLinkTokens (
            Id TEXT NOT NULL PRIMARY KEY,
            Email TEXT NOT NULL,
            TokenHash TEXT NOT NULL,
            RedirectPath TEXT NULL,
            RequestIp TEXT NULL,
            RequestedAtUtc TEXT NOT NULL,
            ExpiresAtUtc TEXT NOT NULL,
            ConsumedAtUtc TEXT NULL
        );
        """);

    dbContext.Database.ExecuteSqlRaw(
        """
        CREATE UNIQUE INDEX IF NOT EXISTS IX_MagicLinkTokens_TokenHash
        ON MagicLinkTokens (TokenHash);
        """);

    dbContext.Database.ExecuteSqlRaw(
        """
        CREATE INDEX IF NOT EXISTS IX_MagicLinkTokens_Email_RequestedAtUtc
        ON MagicLinkTokens (Email, RequestedAtUtc);
        """);
}

static void EnsureSessionSchema(HeroDbContext dbContext)
{
    dbContext.Database.ExecuteSqlRaw(
        """
        CREATE TABLE IF NOT EXISTS GameSessions (
            Id TEXT NOT NULL PRIMARY KEY,
            Name TEXT NOT NULL,
            JoinCode TEXT NOT NULL,
            MasterUserId TEXT NOT NULL,
            CreatedAtUtc TEXT NOT NULL
        );
        """);

    dbContext.Database.ExecuteSqlRaw(
        """
        CREATE UNIQUE INDEX IF NOT EXISTS IX_GameSessions_JoinCode
        ON GameSessions (JoinCode);
        """);

    dbContext.Database.ExecuteSqlRaw(
        """
        CREATE INDEX IF NOT EXISTS IX_GameSessions_MasterUserId
        ON GameSessions (MasterUserId);
        """);

    dbContext.Database.ExecuteSqlRaw(
        """
        CREATE TABLE IF NOT EXISTS SessionParticipants (
            SessionId TEXT NOT NULL,
            UserId TEXT NOT NULL,
            Name TEXT NOT NULL,
            AvatarUrl TEXT NOT NULL DEFAULT '',
            IsMaster INTEGER NOT NULL DEFAULT 0,
            PRIMARY KEY (SessionId, UserId)
        );
        """);

    dbContext.Database.ExecuteSqlRaw(
        """
        CREATE INDEX IF NOT EXISTS IX_SessionParticipants_UserId
        ON SessionParticipants (UserId);
        """);

    dbContext.Database.ExecuteSqlRaw(
        """
        CREATE TABLE IF NOT EXISTS SessionRollHistory (
            Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            SessionId TEXT NOT NULL,
            PlayerName TEXT NOT NULL,
            TimestampUtc TEXT NOT NULL,
            RollsJson TEXT NOT NULL,
            Modifier INTEGER NOT NULL,
            TotalSum INTEGER NOT NULL
        );
        """);

    dbContext.Database.ExecuteSqlRaw(
        """
        CREATE INDEX IF NOT EXISTS IX_SessionRollHistory_SessionId_TimestampUtc
        ON SessionRollHistory (SessionId, TimestampUtc);
        """);
}