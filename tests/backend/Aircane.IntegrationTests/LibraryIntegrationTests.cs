using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aircane.Application.DTOs.Library;
using Aircane.Domain.Enums;
using Xunit;

namespace Aircane.IntegrationTests;

/// <summary>
/// Integration tests for the Library document upload workflow.
/// Verifies: POST document upload → document record created with correct metadata.
/// </summary>
public class LibraryIntegrationTests : IClassFixture<AircaneWebApplicationFactory>
{
    private readonly HttpClient _client;

    public LibraryIntegrationTests(AircaneWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task UploadDocument_CreatesDocumentRecord()
    {
        // Arrange — create a minimal PDF-like file (the import pipeline won't fully process
        // in-memory, but the upload endpoint should accept it and create the record)
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent("%PDF-1.4 fake content"u8.ToArray());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "File", "test-rules.pdf");
        content.Add(new StringContent("Test Rules Document"), "Title");
        content.Add(new StringContent("0"), "SourceType"); // Rules = 0
        content.Add(new StringContent("D&D 5e 2014"), "GameSystem");
        content.Add(new StringContent("PHB"), "Ruleset");
        content.Add(new StringContent("0"), "Visibility"); // Public = 0

        // Act
        var response = await _client.PostAsync("/api/library/documents", content);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var doc = await response.Content.ReadFromJsonAsync<SourceDocumentDto>();
        Assert.NotNull(doc);
        Assert.Equal("Test Rules Document", doc.Title);
        Assert.Equal("test-rules.pdf", doc.OriginalFileName);
        Assert.Equal(SourceType.Rules, doc.SourceType);
        Assert.Equal("D&D 5e 2014", doc.GameSystem);
        Assert.Equal("PHB", doc.Ruleset);
        Assert.NotEqual(Guid.Empty, doc.Id);
    }

    [Fact]
    public async Task UploadDocument_ListDocuments_ReturnsUploadedDocument()
    {
        // Arrange — upload a document first
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent("%PDF-1.4 adventure content"u8.ToArray());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "File", "lost-mine.pdf");
        content.Add(new StringContent("Lost Mine of Phandelver"), "Title");
        content.Add(new StringContent("1"), "SourceType"); // Adventure = 1
        content.Add(new StringContent("D&D 5e 2014"), "GameSystem");
        content.Add(new StringContent("Starter Set"), "Ruleset");
        content.Add(new StringContent("0"), "Visibility");

        var uploadResponse = await _client.PostAsync("/api/library/documents", content);
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);

        // Act — list documents
        var listResponse = await _client.GetAsync("/api/library/documents");

        // Assert
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var documents = await listResponse.Content.ReadFromJsonAsync<List<SourceDocumentDto>>();
        Assert.NotNull(documents);
        Assert.Contains(documents, d => d.Title == "Lost Mine of Phandelver");
    }
}
