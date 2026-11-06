using Microsoft.AspNetCore.Http;

namespace EcommerceBackend.Application.Interfaces;

public interface IImageStorageService
{
    Task<string> UploadImageAsync(
        IFormFile file,
        string folderName,
        CancellationToken cancellationToken);
}
