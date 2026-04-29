using System.Text.Json;

namespace SnippingScreen;

internal sealed class AppSettings
{
    public UploadSettings Upload { get; init; } = new();

    public static AppSettings Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }
}

internal sealed class UploadSettings
{
    public string Url { get; init; } = "";
    public string FormFieldName { get; init; } = "file";
    public string ServiceName { get; init; } = "pbt";
    public string ResponseUrlJsonPath { get; init; } = "url";
}
