using System.Buffers.Binary;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Application.Services;
using EcommerceBackend.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EcommerceBackend.Tests;

public sealed class MonobankIconServiceTests
{
    [Theory]
    [InlineData(120, 180)] // Вертикальное фото
    [InlineData(200, 100)] // Горизонтальное фото
    public async Task CropToSquareJpegAsync_ProducesExactSquareJpeg_FromNonSquareBmp(int width, int height)
    {
        await using var inputStream = CreateSolidBmpStream(width, height);

        var jpegBytes = await MonobankIconService.CropToSquareJpegAsync(inputStream, 256);

        Assert.True(jpegBytes.Length > 100);
        // JPEG SOI marker (0xFF 0xD8)
        Assert.Equal(0xFF, jpegBytes[0]);
        Assert.Equal(0xD8, jpegBytes[1]);

        var (outWidth, outHeight) = ReadJpegDimensions(jpegBytes);
        Assert.Equal(256, outWidth);
        Assert.Equal(256, outHeight);
    }

    [Fact]
    public async Task GetMonobankItemIcon_ReturnsJpegFile_RedirectOrNotFound()
    {
        var controller = new PaymentsController(
            null!,
            null!,
            new ConfigurationBuilder().Build(),
            NullLogger<PaymentsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var jpegResult = await controller.GetMonobankItemIcon(
            Guid.NewGuid(),
            new StubIconService(new MonobankSquareIconResult([1, 2, 3], "https://cdn.example.com/a.jpg")),
            CancellationToken.None);
        var fileResult = Assert.IsType<FileContentResult>(jpegResult);
        Assert.Equal("image/jpeg", fileResult.ContentType);
        Assert.Equal([1, 2, 3], fileResult.FileContents);

        var redirectResult = await controller.GetMonobankItemIcon(
            Guid.NewGuid(),
            new StubIconService(new MonobankSquareIconResult(null, "https://cdn.example.com/a.jpg")),
            CancellationToken.None);
        var redirect = Assert.IsType<RedirectResult>(redirectResult);
        Assert.Equal("https://cdn.example.com/a.jpg", redirect.Url);

        var notFoundResult = await controller.GetMonobankItemIcon(
            Guid.NewGuid(),
            new StubIconService(new MonobankSquareIconResult(null, null)),
            CancellationToken.None);
        Assert.IsType<NotFoundResult>(notFoundResult);
    }

    private static MemoryStream CreateSolidBmpStream(int width, int height)
    {
        var rowSize = ((width * 3 + 3) / 4) * 4;
        var pixelDataSize = rowSize * height;
        var fileSize = 54 + pixelDataSize;
        var bytes = new byte[fileSize];

        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2, 4), fileSize);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(10, 4), 54);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14, 4), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18, 4), width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22, 4), height);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(26, 2), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(28, 2), 24);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(34, 4), pixelDataSize);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = 54 + y * rowSize + x * 3;
                bytes[offset] = 40;
                bytes[offset + 1] = 120;
                bytes[offset + 2] = 60;
            }
        }

        return new MemoryStream(bytes);
    }

    private static (int Width, int Height) ReadJpegDimensions(byte[] jpeg)
    {
        var i = 2;
        while (i + 4 < jpeg.Length)
        {
            if (jpeg[i] != 0xFF)
            {
                i++;
                continue;
            }

            var marker = jpeg[i + 1];
            if (marker is 0xC0 or 0xC1 or 0xC2)
            {
                var h = BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(i + 5, 2));
                var w = BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(i + 7, 2));
                return (w, h);
            }

            if (marker is 0xD8 or 0xD9 or 0x01 || (marker >= 0xD0 && marker <= 0xD7))
            {
                i += 2;
                continue;
            }

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(i + 2, 2));
            i += 2 + segmentLength;
        }

        throw new InvalidOperationException("JPEG SOF marker not found.");
    }

    private sealed class StubIconService(MonobankSquareIconResult result) : IMonobankIconService
    {
        public Task<MonobankSquareIconResult> GetSquareProductIconAsync(Guid productId, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }
}
