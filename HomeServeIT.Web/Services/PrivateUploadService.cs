using System.Text;
using System.Text.RegularExpressions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using UglyToad.PdfPig;

namespace HomeServeIT.Web.Services;

public sealed class UploadValidationException(string message) : Exception(message);

public sealed class StoredUpload(string url, string physicalPath) : IAsyncDisposable
{
    public string Url { get; } = url;
    private bool committed;
    public void Complete() => committed = true;
    public ValueTask DisposeAsync()
    {
        if (!committed && System.IO.File.Exists(physicalPath)) System.IO.File.Delete(physicalPath);
        return ValueTask.CompletedTask;
    }
}

public sealed class PrivateUploadService(IWebHostEnvironment environment)
{
    public const long MaxFileBytes = 5 * 1024 * 1024;
    public const long MaxRequestBytes = MaxFileBytes + 256 * 1024;
    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png",
        [".gif"] = "image/gif", [".webp"] = "image/webp", [".pdf"] = "application/pdf"
    };

    public static bool IsValidPath(string bucket, string filename) =>
        bucket is "service-requests" or "deliverables"
        && Regex.IsMatch(filename, @"\A[a-fA-F0-9-]{32,36}\.(png|jpg|jpeg|gif|webp|pdf)\z");

    public string? Resolve(string bucket, string filename)
    {
        if (!IsValidPath(bucket, filename)) return null;
        var path = Path.Combine(environment.ContentRootPath, "App_Data", "private-uploads", bucket, filename);
        if (System.IO.File.Exists(path)) return path;
        // Existing generated files retain their URLs, but all access goes through authorization.
        var legacy = Path.Combine(environment.WebRootPath, "uploads", bucket, filename);
        return System.IO.File.Exists(legacy) ? legacy : null;
    }

    public static string ContentType(string filename) => MimeTypes[Path.GetExtension(filename)];

    public async Task<StoredUpload?> StoreAsync(IFormFile? file, string bucket, CancellationToken cancellationToken = default)
    {
        if (file == null) return null;
        if (bucket is not ("service-requests" or "deliverables")) throw new ArgumentException("Invalid upload category.");
        if (file.Length <= 0 || file.Length > MaxFileBytes)
            throw new UploadValidationException("Choose a nonempty file of at most 5 MB.");
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!MimeTypes.TryGetValue(extension, out var mime) || file.ContentType != mime
            || (extension == ".pdf" && bucket != "deliverables"))
            throw new UploadValidationException("The file extension and content type must match an allowed image or deliverable PDF.");
        using var input = new MemoryStream();
        await using (var source = file.OpenReadStream())
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (input.Length + read > MaxFileBytes) throw new UploadValidationException("The file exceeds 5 MB.");
                await input.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        input.Position = 0;
        byte[] bytes;
        try
        {
            if (extension == ".pdf")
            {
                bytes = input.ToArray();
                if (!bytes.AsSpan().StartsWith("%PDF-"u8)
                    || !Encoding.ASCII.GetString(bytes.AsSpan(Math.Max(0, bytes.Length - 1024))).Contains("%%EOF"))
                    throw new UploadValidationException("Invalid PDF signature or ending.");
                using var pdf = PdfDocument.Open(bytes, new ParsingOptions { UseLenientParsing = false });
                if (pdf.NumberOfPages is < 1 or > 100) throw new UploadValidationException("A PDF must have 1–100 pages.");
                foreach (var page in pdf.GetPages()) _ = page.Number;
            }
            else
            {
                var format = await Image.DetectFormatAsync(input, cancellationToken);
                if (format.DefaultMimeType != mime) throw new UploadValidationException("The file signature does not match its type.");
                input.Position = 0;
                var info = await Image.IdentifyAsync(input, cancellationToken);
                if ((long)info.Width * info.Height > 16_000_000)
                    throw new UploadValidationException("Images must contain at most 16 million pixels.");
                input.Position = 0;
                using var image = await Image.LoadAsync(new DecoderOptions { MaxFrames = 1, SkipMetadata = true }, input, cancellationToken);
                using var output = new MemoryStream();
                await image.SaveAsPngAsync(output, cancellationToken);
                bytes = output.ToArray();
                if (bytes.Length > MaxFileBytes) throw new UploadValidationException("The decoded image exceeds 5 MB. Resize it and retry.");
                extension = ".png";
            }
        }
        catch (Exception ex) when (ex is not UploadValidationException and not OperationCanceledException and not OutOfMemoryException)
        {
            throw new UploadValidationException("The file is malformed or cannot be decoded.");
        }
        var filename = Guid.NewGuid().ToString("N") + extension;
        var directory = Path.Combine(environment.ContentRootPath, "App_Data", "private-uploads", bucket);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, filename);
        try { await System.IO.File.WriteAllBytesAsync(path, bytes, cancellationToken); }
        catch { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); throw; }
        return new StoredUpload($"/private-files/{bucket}/{filename}", path);
    }
}
