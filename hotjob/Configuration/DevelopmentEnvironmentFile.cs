using Microsoft.Extensions.Hosting;

namespace hotjob.Configuration;

public static class DevelopmentEnvironmentFile
{
    public static void Load(ConfigurationManager configuration, IHostEnvironment environment, string currentDirectory)
    {
        if (!environment.IsDevelopment()) return;

        var path = new[]
        {
            Path.Combine(environment.ContentRootPath, ".env.local"),
            Path.Combine(currentDirectory, ".env.local"),
            Path.Combine(currentDirectory, "hotjob", ".env.local")
        }.FirstOrDefault(File.Exists);
        if (path is null) return;

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(path))
        {
            var text = line.Trim();
            if (text.Length == 0 || text.StartsWith('#')) continue;
            if (text.StartsWith("export ", StringComparison.Ordinal)) text = text[7..].TrimStart();
            var parts = text.Split('=', 2);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0])) continue;
            values[parts[0].Trim().Replace("__", ":")] = parts[1].Trim().Trim('"', '\'');
        }
        configuration.AddInMemoryCollection(values);
    }
}
