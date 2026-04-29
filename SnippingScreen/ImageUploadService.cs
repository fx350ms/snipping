using System.Drawing.Imaging;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SnippingScreen;

internal sealed class ImageUploadService
{
    private readonly UploadSettings _settings;

    public ImageUploadService(UploadSettings settings)
    {
        _settings = settings;
    }

    public async Task<string> UploadAsync(Image image, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.Url))
        {
            throw new InvalidOperationException("Upload.Url is empty in appsettings.json.");
        }

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

        using var content = new MultipartFormDataContent();
        using var stream = new MemoryStream();
        SaveAsJpeg(image, stream);
        stream.Position = 0;

        var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, string.IsNullOrWhiteSpace(_settings.FormFieldName) ? "file" : _settings.FormFieldName, $"snip-{DateTime.Now:yyyyMMdd-HHmmss}.jpg");
        content.Add(new StringContent(string.IsNullOrWhiteSpace(_settings.ServiceName) ? "pbt" : _settings.ServiceName), "serviceName");

        using var response = await client.PostAsync(_settings.Url, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        return ExtractUrl(body);
    }

    private string ExtractUrl(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "";
        }

        if (Uri.TryCreate(body.Trim().Trim('"'), UriKind.Absolute, out _))
        {
            return body.Trim().Trim('"');
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            return ExtractUrlFromJson(doc, body);
        }
        catch (JsonException)
        {
            return body.Trim().Trim('"');
        }
    }

    private string ExtractUrlFromJson(JsonDocument doc, string fallback)
    {
        if (doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("success", out var success)
            && success.ValueKind == JsonValueKind.False)
        {
            var message = doc.RootElement.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString()
                : "Upload failed.";
            throw new InvalidOperationException(message ?? "Upload failed.");
        }

        var current = doc.RootElement;
        var path = string.IsNullOrWhiteSpace(_settings.ResponseUrlJsonPath) ? "url" : _settings.ResponseUrlJsonPath;

        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
            {
                return fallback;
            }
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() ?? "" : current.ToString();
    }

    private static void SaveAsJpeg(Image image, Stream stream)
    {
        using var jpeg = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(jpeg))
        {
            g.Clear(Color.White);
            g.DrawImageUnscaled(image, 0, 0);
        }

        jpeg.Save(stream, ImageFormat.Jpeg);
    }
}
