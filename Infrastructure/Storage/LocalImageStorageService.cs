using EcommerceBackend.Application.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace EcommerceBackend.Infrastructure.Storage;

public sealed class LocalImageStorageService(IWebHostEnvironment environment) : IImageStorageService
{
    private const long MaxFileSize = 5 * 1024 * 1024;
    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };

    public async Task<string> UploadImageAsync(
        IFormFile file,
        string folderName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (file.Length is <= 0 or > MaxFileSize)
            throw new ArgumentException("Image size must be between 1 byte and 5 MB.", nameof(file));

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(extension))
            throw new ArgumentException("Only .jpg, .jpeg, .png, and .webp images are allowed.", nameof(file));

        if (string.IsNullOrWhiteSpace(folderName) ||
            folderName is "." or ".." ||
            folderName.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException("Folder name must be a single safe path segment.", nameof(folderName));
        }

        var webRootPath = environment.WebRootPath;
        if (string.IsNullOrWhiteSpace(webRootPath))
            webRootPath = Path.Combine(environment.ContentRootPath, "wwwroot");

        var uploadDirectory = Path.Combine(webRootPath, "uploads", folderName);
        Directory.CreateDirectory(uploadDirectory);

        var fileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var filePath = Path.Combine(uploadDirectory, fileName);
        var header = new byte[12];

        await using var input = file.OpenReadStream();
        var headerLength = await ReadHeaderAsync(input, header, cancellationToken);
        if (!HasValidSignature(header.AsSpan(0, headerLength), extension))
            throw new ArgumentException("File contents do not match the image extension.", nameof(file));

        var uploadCompleted = false;
        try
        {
            await using var output = new FileStream(
                filePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await output.WriteAsync(header.AsMemory(0, headerLength), cancellationToken);
            await input.CopyToAsync(output, cancellationToken);
            uploadCompleted = true;
        }
        finally
        {
            if (!uploadCompleted && File.Exists(filePath))
                File.Delete(filePath);
        }

        return $"/uploads/{folderName}/{fileName}";
    }

    private static async Task<int> ReadHeaderAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(totalRead), cancellationToken);
            if (bytesRead == 0)
                break;

            totalRead += bytesRead;
        }

        return totalRead;
    }

    private static bool HasValidSignature(ReadOnlySpan<byte> header, string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" =>
                header.Length >= 3 &&
                header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            ".png" =>
                header.Length >= 8 &&
                header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            ".webp" =>
                header.Length >= 12 &&
                header[..4].SequenceEqual("RIFF"u8) &&
                header[8..12].SequenceEqual("WEBP"u8),
            _ => false
        };
}
