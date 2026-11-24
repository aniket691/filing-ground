using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinQuery.Application.Documents;
using Microsoft.Extensions.DependencyInjection;

namespace FinQuery.Api.Tests;

public class DocumentUploadEndpointTests : IClassFixture<FinQueryApiFactory>
{
    private const string Password = "StrongPassword123!";
    private readonly FinQueryApiFactory _factory;
    private readonly HttpClient _client;

    public DocumentUploadEndpointTests(FinQueryApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PdfUpload_PersistsTheRowAndTheFile()
    {
        await Authenticate("upload-owner@example.com");
        var companyId = await CreateCompany("Upload Co", "UPLC");
        var response = await Upload(companyId, "report.pdf", "%PDF-1.4\n"u8.ToArray(), "10-K");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("report.pdf", body.GetProperty("title").GetString());
        Assert.Equal("uploaded", body.GetProperty("processing_status").GetString());
        Assert.Equal(companyId, body.GetProperty("company_id").GetInt32());
        Assert.Equal("10-K", body.GetProperty("document_type").GetString());

        var root = _factory.Services.GetRequiredService<StorageSettings>().Path;
        var stored = Path.Combine(root, body.GetProperty("file_path").GetString()!.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(stored));
        Assert.DoesNotContain("backend" + Path.DirectorySeparatorChar + "storage_data", root, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DuplicateContent_ReturnsTheSameId_AndDoesNotAddASecondFile()
    {
        await Authenticate("upload-dup@example.com");
        var companyId = await CreateCompany("Dup Upload", "DUPU");
        var bytes = "same-bytes"u8.ToArray();
        var first = await (await Upload(companyId, "a.txt", bytes, null)).Content.ReadFromJsonAsync<JsonElement>();
        var root = _factory.Services.GetRequiredService<StorageSettings>().Path;
        var before = Directory.Exists(root) ? Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length : 0;

        var second = await Upload(companyId, "b.txt", bytes, null);
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        var after = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length;

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(first.GetProperty("id").GetInt32(), secondBody.GetProperty("id").GetInt32());
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task InvalidAndEmptyFiles_Are422()
    {
        await Authenticate("upload-bad@example.com");
        var exe = await Upload(null, "payload.exe", "MZ"u8.ToArray(), null);
        var exeBody = await exe.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, exe.StatusCode);
        Assert.Contains("Unsupported file format 'exe'", exeBody.GetProperty("detail").GetString());
        Assert.Equal("ValidationError", exeBody.GetProperty("error").GetString());

        var empty = await Upload(null, "empty.pdf", [], null);
        var emptyBody = await empty.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, empty.StatusCode);
        Assert.Equal("Uploaded file is empty", emptyBody.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task OtherUsersCompany_Is404()
    {
        await Authenticate("upload-a@example.com");
        var companyId = await CreateCompany("Owned Co", "OWNC");
        await Authenticate("upload-b@example.com");

        var response = await Upload(companyId, "report.pdf", "%PDF"u8.ToArray(), "10-K");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal($"Company with id {companyId} not found", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task MissingToken_Is401()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var response = await Upload(null, "note.txt", "hello"u8.ToArray(), null);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Missing authentication token", body.GetProperty("detail").GetString());
    }

    private async Task Authenticate(string email)
    {
        var register = await _client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = Password });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private async Task<int> CreateCompany(string legalName, string ticker)
    {
        var created = await _client.PostAsJsonAsync("/api/v1/companies", new { legal_name = legalName, ticker });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    private Task<HttpResponseMessage> Upload(int? companyId, string filename, byte[] bytes, string? documentType)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", filename);
        if (companyId is not null)
        {
            form.Add(new StringContent(companyId.Value.ToString()), "company_id");
        }

        if (documentType is not null)
        {
            form.Add(new StringContent(documentType), "document_type");
        }

        return _client.PostAsync("/api/v1/documents/upload", form);
    }
}
