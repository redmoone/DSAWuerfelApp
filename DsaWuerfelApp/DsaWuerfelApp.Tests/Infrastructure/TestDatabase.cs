namespace DsaWuerfelApp.Tests.Infrastructure;

public sealed class TestDatabase : IDisposable
{
    private readonly string _directory;

    public TestDatabase()
    {
        _directory = Path.Combine(Path.GetTempPath(), "dsa-wuerfelapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        DatabasePath = Path.Combine(_directory, "heroes.db");
        DataProtectionPath = Path.Combine(_directory, "data-protection-keys");
        Directory.CreateDirectory(DataProtectionPath);
    }

    public string DatabasePath { get; }

    public string DataProtectionPath { get; }

    public string ConnectionString => $"Data Source={DatabasePath};Pooling=False";

    public void Dispose()
    {
        for (var attempt = 0; attempt < 20 && Directory.Exists(_directory); attempt++)
        {
            try
            {
                Directory.Delete(_directory, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 19)
            {
                Thread.Sleep(100);
            }
        }
    }
}
