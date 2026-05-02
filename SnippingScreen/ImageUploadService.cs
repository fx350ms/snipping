using System.Buffers.Text;
using System.Drawing.Imaging;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SnippingScreen;

internal sealed class ImageUploadService
{
    private readonly UploadSettings _settings;
    private readonly AppSettings _appsetting;

    public ImageUploadService(AppSettings appSettings)
    {
        _settings = appSettings.Upload;
        _appsetting = appSettings;
    }

    public async Task<string> UploadAsync(Image image, CancellationToken cancellationToken = default)
    {
        //if (string.IsNullOrWhiteSpace(_settings.Url))
        //{
        //    throw new InvalidOperationException("Upload.Url is empty in appsettings.json.");
        //}

        //using var client = new HttpClient();
        //client.DefaultRequestHeaders.Accept.Clear();
        //client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

        //using var content = new MultipartFormDataContent();
        //using var stream = new MemoryStream();
        //SaveAsJpeg(image, stream);
        //stream.Position = 0;

        //var fileContent = new StreamContent(stream);
        //fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        //content.Add(fileContent, string.IsNullOrWhiteSpace(_settings.FormFieldName) ? "file" : _settings.FormFieldName, $"snip-{DateTime.Now:yyyyMMdd-HHmmss}.jpg");
        //content.Add(new StringContent(string.IsNullOrWhiteSpace(_settings.ServiceName) ? "pbt" : _settings.ServiceName), "serviceName");

        //using var response = await client.PostAsync(_settings.Url, content, cancellationToken);
        //var body = await response.Content.ReadAsStringAsync(cancellationToken);
        //response.EnsureSuccessStatusCode();

        //return ExtractUrl(body);
         
        if(_appsetting.HostType == (int) HostType.PBTHost)
        {
            return await UploadToPbtHostAsync(image, cancellationToken);
        }
        else if(_appsetting.HostType == (int) HostType.FreeImageHosting)
        {
            return await UploadToFreeHostAsync(image, cancellationToken);
        }
        else
        {
            throw new InvalidOperationException("Invalid HostType in appsettings.json.");
        }

    }

    public async Task<string> UploadToPbtHostAsync(Image image, CancellationToken cancellationToken = default)
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


    public async Task<string> UploadToFreeHostAsync(Image image, CancellationToken cancellationToken = default)
    {
        // 1. Thay API Key của bạn vào đây
        string apiKey = "6d207e02198a847aa98d0a2a901485a5";
        string endpoint = "https://freeimage.host/api/1/upload";

        if (image == null) return "Error: Image is null";

        using (var client = new HttpClient())
        {
            try
            {
                // 2. Chuyển đổi Image sang chuỗi Base64
                string base64Source;
                using (MemoryStream ms = new MemoryStream())
                {
                    // Lưu ảnh vào stream dưới định dạng Png để giữ chất lượng
                    image.Save(ms, ImageFormat.Png);
                    byte[] imageBytes = ms.ToArray();
                    base64Source = Convert.ToBase64String(imageBytes);
                }

                // 3. Chuẩn bị các tham số cho Body Request (POST)
                var postData = new Dictionary<string, string>
            {
                { "key", apiKey },
                { "action", "upload" },
                { "source", base64Source },
                { "format", "json" }
            };

                // 4. Gửi yêu cầu POST theo định dạng FormUrlEncoded (tương đương form submit)
                using (var content = new FormUrlEncodedContent(postData))
                {
                    var response = await client.PostAsync(endpoint, content, cancellationToken);

                    if (response.IsSuccessStatusCode)
                    {
                        string jsonResponse = await response.Content.ReadAsStringAsync();

                        // 5. Phân tích kết quả JSON để lấy display_url
                        using (JsonDocument doc = JsonDocument.Parse(jsonResponse))
                        {
                            JsonElement root = doc.RootElement;

                            // Kiểm tra tính hợp lệ của cấu trúc trả về
                            if (root.TryGetProperty("image", out JsonElement imageElement))
                            {
                                if (imageElement.TryGetProperty("display_url", out JsonElement displayUrl))
                                {
                                    return displayUrl.GetString();
                                }
                            }
                        }
                    }
                    else
                    {
                        string errorContent = await response.Content.ReadAsStringAsync();
                        return $"Server Error: {response.StatusCode} - {errorContent}";
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return "Task was cancelled.";
            }
            catch (Exception ex)
            {
                return $"Exception: {ex.Message}";
            }
        }

        return "Unknown error";
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
