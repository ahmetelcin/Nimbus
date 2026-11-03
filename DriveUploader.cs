using System.Globalization;
using System.Net;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace Nimbus;

internal sealed record UploadedFile(string Id, string WebViewLink, long ModifiedUtcTicks);

internal sealed class DriveUploader(UserCredential credential) : IDisposable
{
    private const string FolderName = "Nimbus";
    private const string LegacyFolderName = "DriveAc";
    private const string FolderMimeType = "application/vnd.google-apps.folder";
    private const string ExportSizeLimitReason = "exportSizeLimitExceeded";
    private const int ProgressChunkSize = ResumableUpload.MinimumChunkSize * 4;

    private readonly DriveService _service = new(new BaseClientService.Initializer
    {
        HttpClientInitializer = credential,
        ApplicationName = "Nimbus",
    });

    public async Task<string> GetAccountEmailAsync()
    {
        var request = _service.About.Get();
        request.Fields = "user(emailAddress)";
        var about = await request.ExecuteAsync();
        return about.User.EmailAddress;
    }

    // onProgress: 0..1 yüklenen oran; null ise dosya Google tarafında dönüştürülüyor.
    public async Task<UploadedFile> UploadAndConvertAsync(string path, FileType fileType, Action<double?> onProgress)
    {
        using var content = OpenForUpload(path);
        var folderId = await FindOwnFolderAsync() ?? await CreateOwnFolderAsync();
        var length = content.Length;

        var metadata = new DriveFile
        {
            Name = $"{Path.GetFileNameWithoutExtension(path)} - {DateTime.Now:yyyyMMdd-HHmmss}",
            MimeType = fileType.GoogleMimeType,
            Parents = [folderId],
        };

        var request = _service.Files.Create(metadata, content, fileType.SourceMimeType);
        request.Fields = "id,webViewLink,modifiedTime";
        request.ChunkSize = ProgressChunkSize;

        // Son parçanın yanıtı dönüştürme bitince gelir; bu yüzden o noktadan sonrası belirsiz gösterilir.
        void Report(long sent) => onProgress(sent + ProgressChunkSize >= length ? null : (double)sent / length);
        request.ProgressChanged += p =>
        {
            if (p.Status == UploadStatus.Uploading)
                Report(p.BytesSent);
        };
        Report(0);

        var progress = await request.UploadAsync();
        if (progress.Exception is not null)
            throw progress.Exception;

        if (request.ResponseBody is not { Id: { } id, WebViewLink: { } link })
            throw new UserMessageException("Dosya yüklendi ancak Drive bağlantı döndürmedi.");

        return new UploadedFile(id, link, request.ResponseBody.ModifiedTimeDateTimeOffset?.UtcTicks ?? 0);
    }

    // Kopya silinmiş ya da çöp kutusundaysa null döner.
    public async Task<GoogleState?> GetStateAsync(string fileId)
    {
        var file = await GetFileOrNullAsync(fileId, "trashed,webViewLink,createdTime,modifiedTime");
        return file is null || file.Trashed == true
            ? null
            : new GoogleState(
                file.WebViewLink,
                file.CreatedTimeDateTimeOffset?.UtcTicks ?? 0,
                file.ModifiedTimeDateTimeOffset?.UtcTicks ?? 0);
    }

    public async Task ExportToFileAsync(string fileId, string mimeType, string destination)
    {
        await using var output = File.Create(destination);
        var progress = await _service.Files.Export(fileId, mimeType).DownloadAsync(output);
        if (progress.Status == DownloadStatus.Completed)
            return;

        if (progress.Exception is GoogleApiException api
            && api.Error?.Errors?.Any(e => e.Reason == ExportSizeLimitReason) == true)
        {
            output.SetLength(0);
            await DownloadExportLinkAsync(fileId, mimeType, output);
            return;
        }

        throw progress.Exception ?? new UserMessageException("Dosya Google'dan indirilemedi.");
    }

    public async Task<IReadOnlyList<string>> ListFilesOlderThanAsync(int days)
    {
        var folderId = await FindOwnFolderAsync();
        if (folderId is null)
            return [];

        var cutoff = DateTime.UtcNow.AddDays(-days).ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var ids = new List<string>();
        string? pageToken = null;
        do
        {
            var request = _service.Files.List();
            request.Q = $"'{folderId}' in parents and trashed = false and modifiedTime < '{cutoff}'";
            request.Fields = "nextPageToken,files(id)";
            request.PageSize = 100;
            request.PageToken = pageToken;
            var page = await request.ExecuteAsync();
            ids.AddRange(page.Files.Select(f => f.Id));
            pageToken = page.NextPageToken;
        }
        while (pageToken is not null);

        return ids;
    }

    public async Task MoveToTrashAsync(IEnumerable<string> fileIds)
    {
        foreach (var id in fileIds)
            await _service.Files.Update(new DriveFile { Trashed = true }, id).ExecuteAsync();
    }

    public void Dispose() => _service.Dispose();

    // files.export 10 MB ile sınırlıdır; görselli sunumlar bunu kolayca aşar, dışa aktarma bağlantısı aşabilir.
    private async Task DownloadExportLinkAsync(string fileId, string mimeType, Stream output)
    {
        var file = await GetFileOrNullAsync(fileId, "exportLinks");
        string? link = null;
        if (file?.ExportLinks?.TryGetValue(mimeType, out link) != true || link is null)
            throw new UserMessageException("Dosya, Google'ın indirme sınırını aştığı için indirilemedi.");

        using var response = await _service.HttpClient.GetAsync(link, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
            throw new UserMessageException("Dosya, Google'ın indirme sınırını aştığı için indirilemedi.");

        await response.Content.CopyToAsync(output);
    }

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

    // Eski ada sahip klasör bulunursa adını değiştirir; hiç klasör yoksa null döner.
    private async Task<string?> FindOwnFolderAsync()
    {
        var settings = AppSettings.Load();
        if (settings.DriveFolderId is { } savedId
            && await GetFileOrNullAsync(savedId, "name,trashed") is { Trashed: not true } saved)
        {
            if (saved.Name == LegacyFolderName)
                await RenameToCurrentAsync(savedId);
            return savedId;
        }

        var id = await SearchFolderAsync(FolderName);
        if (id is null && (id = await SearchFolderAsync(LegacyFolderName)) is not null)
            await RenameToCurrentAsync(id);

        if (id is not null)
        {
            settings.DriveFolderId = id;
            settings.Save();
        }

        return id;
    }

    private async Task<string> CreateOwnFolderAsync()
    {
        var request = _service.Files.Create(new DriveFile { Name = FolderName, MimeType = FolderMimeType });
        request.Fields = "id";
        var folder = await request.ExecuteAsync();

        var settings = AppSettings.Load();
        settings.DriveFolderId = folder.Id;
        settings.Save();
        return folder.Id;
    }

    private async Task<DriveFile?> GetFileOrNullAsync(string id, string fields)
    {
        var request = _service.Files.Get(id);
        request.Fields = fields;
        try
        {
            return await request.ExecuteAsync();
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task RenameToCurrentAsync(string id) =>
        await _service.Files.Update(new DriveFile { Name = FolderName }, id).ExecuteAsync();

    // settings.json silinmiş ya da başka hesaba geçilmişse aynı klasörü yeniden oluşturmamak için.
    private async Task<string?> SearchFolderAsync(string name)
    {
        var request = _service.Files.List();
        request.Q = $"name = '{name}' and mimeType = '{FolderMimeType}' and trashed = false";
        request.Fields = "files(id)";
        request.PageSize = 1;
        var result = await request.ExecuteAsync();
        return result.Files.FirstOrDefault()?.Id;
    }
}
