using System.Net;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace DriveAc;

internal sealed class DriveUploader(UserCredential credential) : IDisposable
{
    private const string FolderName = "DriveAc";
    private const string FolderMimeType = "application/vnd.google-apps.folder";

    private readonly DriveService _service = new(new BaseClientService.Initializer
    {
        HttpClientInitializer = credential,
        ApplicationName = "DriveAc",
    });

    public async Task<string> UploadAndConvertAsync(string path, FileType fileType)
    {
        using var content = OpenForUpload(path);
        var folderId = await EnsureFolderAsync();

        var metadata = new DriveFile
        {
            Name = $"{Path.GetFileNameWithoutExtension(path)} - {DateTime.Now:yyyyMMdd-HHmmss}",
            MimeType = fileType.GoogleMimeType,
            Parents = [folderId],
        };

        var request = _service.Files.Create(metadata, content, fileType.SourceMimeType);
        request.Fields = "id,webViewLink";

        var progress = await request.UploadAsync();
        if (progress.Exception is not null)
            throw progress.Exception;

        return request.ResponseBody?.WebViewLink
            ?? throw new UserMessageException("Dosya yüklendi ancak Drive bağlantı döndürmedi.");
    }

    public void Dispose() => _service.Dispose();

    private static FileStream OpenForUpload(string path)
    {
        var name = Path.GetFileName(path);
        try
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new UserMessageException($"Dosya bulunamadı: {name}");
        }
        catch (UnauthorizedAccessException)
        {
            throw new UserMessageException($"Dosyayı okuma izni yok: {name}");
        }
        catch (IOException)
        {
            throw new UserMessageException($"Dosya başka bir program tarafından kilitli: {name}");
        }
    }

    private async Task<string> EnsureFolderAsync()
    {
        var settings = AppSettings.Load();
        if (settings.DriveFolderId is { } savedId && await FolderExistsAsync(savedId))
            return savedId;

        settings.DriveFolderId = await FindFolderAsync() ?? await CreateFolderAsync();
        settings.Save();
        return settings.DriveFolderId;
    }

    private async Task<bool> FolderExistsAsync(string id)
    {
        var request = _service.Files.Get(id);
        request.Fields = "trashed";
        try
        {
            var folder = await request.ExecuteAsync();
            return folder.Trashed != true;
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    // settings.json silinmiş ya da başka hesaba geçilmişse aynı klasörü yeniden oluşturmamak için.
    private async Task<string?> FindFolderAsync()
    {
        var request = _service.Files.List();
        request.Q = $"name = '{FolderName}' and mimeType = '{FolderMimeType}' and trashed = false";
        request.Fields = "files(id)";
        request.PageSize = 1;
        var result = await request.ExecuteAsync();
        return result.Files.FirstOrDefault()?.Id;
    }

    private async Task<string> CreateFolderAsync()
    {
        var request = _service.Files.Create(new DriveFile { Name = FolderName, MimeType = FolderMimeType });
        request.Fields = "id";
        var folder = await request.ExecuteAsync();
        return folder.Id;
    }
}
