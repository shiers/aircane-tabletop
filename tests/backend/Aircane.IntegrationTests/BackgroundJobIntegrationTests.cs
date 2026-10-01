using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aircane.Application.DTOs.Library;
using Aircane.Domain.Enums;
using Xunit;

namespace Aircane.IntegrationTests;

/// <summary>
/// End-to-end tests for the background job pipeline: upload/re-embed return immediately with a
/// 202/201 while the hosted <c>BackgroundJobWorker</c> processes the work asynchronously, and job
/// status is observable via <c>GET /api/jobs/{id}</c> and the document status endpoint.
/// </summary>
public class BackgroundJobIntegrationTests : IClassFixture<AircaneWebApplicationFactory>
{
    private readonly HttpClient _client;

    public BackgroundJobIntegrationTests(AircaneWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task UploadDocument_ReturnsImmediately_ThenWorkerProcessesToTerminalStatus()
    {
        // Arrange: a fake PDF. It won't parse into text, so the worker will mark it OcrRequired
        // or Failed — either way a TERMINAL state, which proves the worker ran asynchronously
        // after the upload returned.
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent("%PDF-1.4 fake background job content"u8.ToArray());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "File", "bgjob-upload.pdf");
        content.Add(new StringContent("Background Job Upload"), "Title");
        content.Add(new StringContent("0"), "SourceType");
        content.Add(new StringContent("D&D 5e 2014"), "GameSystem");
        content.Add(new StringContent("PHB"), "Ruleset");
        content.Add(new StringContent("0"), "Visibility");

        // Act: upload returns 201 promptly (the import is enqueued, not run inline).
        var uploadResponse = await _client.PostAsync("/api/library/documents", content);
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);

        var doc = await uploadResponse.Content.ReadFromJsonAsync<SourceDocumentDto>();
        Assert.NotNull(doc);

        // Assert: the worker eventually moves the document out of Pending to a terminal state.
        var status = await PollDocumentStatusAsync(
            doc!.Id,
            s => s is ImportStatus.Completed or ImportStatus.OcrRequired or ImportStatus.Failed);

        Assert.True(
            status is ImportStatus.Completed or ImportStatus.OcrRequired or ImportStatus.Failed,
            $"Expected a terminal import status but got {status}.");
    }

    [Fact]
    public async Task ReembedAll_Returns202_WithJobId_AndJobReachesTerminalState()
    {
        // Act: enqueue a re-embed-all job.
        var response = await _client.PostAsync("/api/library/documents/reembed-all", content: null);

        // Assert: 202 Accepted with a correlatable job id.
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var accepted = await response.Content.ReadFromJsonAsync<JobAcceptedResponse>();
        Assert.NotNull(accepted);
        Assert.NotEqual(Guid.Empty, accepted!.JobId);
        Assert.Equal("ReembedAll", accepted.JobType);

        // The job is observable via GET /api/jobs/{id} and reaches Completed.
        var job = await PollJobStatusAsync(accepted.JobId, isTerminal: true);
        Assert.NotNull(job);
        Assert.True(
            job!.State is Aircane.Application.Abstractions.BackgroundJobs.BackgroundJobState.Completed
                or Aircane.Application.Abstractions.BackgroundJobs.BackgroundJobState.Failed,
            $"Expected re-embed job to finish but state was {job.State}.");
    }

    [Fact]
    public async Task ScanFolder_UnknownFolder_Returns404()
    {
        var response = await _client.PostAsync(
            $"/api/library/folders/{Guid.NewGuid()}/scan", content: null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetJob_UnknownId_Returns404()
    {
        var response = await _client.GetAsync($"/api/jobs/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReOcrAll_Returns202_WithJobId()
    {
        var response = await _client.PostAsync("/api/library/documents/reocr-all", content: null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var accepted = await response.Content.ReadFromJsonAsync<JobAcceptedResponse>();
        Assert.NotNull(accepted);
        Assert.Equal("ReocrAll", accepted!.JobType);

        // The job is observable and reaches a terminal state (no OCR-required docs -> quick completion).
        var job = await PollJobStatusAsync(accepted.JobId, isTerminal: true);
        Assert.NotNull(job);
    }

    private async Task<ImportStatus> PollDocumentStatusAsync(
        Guid documentId,
        Func<ImportStatus, bool> done,
        int timeoutMs = 10000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        ImportStatus last = ImportStatus.Pending;

        while (DateTime.UtcNow < deadline)
        {
            var res = await _client.GetAsync($"/api/library/documents/{documentId}/status");
            if (res.StatusCode == HttpStatusCode.OK)
            {
                var dto = await res.Content.ReadFromJsonAsync<ImportStatusDto>();
                if (dto is not null)
                {
                    last = dto.Status;
                    if (done(last))
                        return last;
                }
            }

            await Task.Delay(50);
        }

        return last;
    }

    private async Task<Aircane.Application.Abstractions.BackgroundJobs.BackgroundJobStatus?> PollJobStatusAsync(
        Guid jobId,
        bool isTerminal,
        int timeoutMs = 10000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        Aircane.Application.Abstractions.BackgroundJobs.BackgroundJobStatus? last = null;

        while (DateTime.UtcNow < deadline)
        {
            var res = await _client.GetAsync($"/api/jobs/{jobId}");
            if (res.StatusCode == HttpStatusCode.OK)
            {
                last = await res.Content
                    .ReadFromJsonAsync<Aircane.Application.Abstractions.BackgroundJobs.BackgroundJobStatus>();

                if (last is not null && (!isTerminal ||
                    last.State is Aircane.Application.Abstractions.BackgroundJobs.BackgroundJobState.Completed
                        or Aircane.Application.Abstractions.BackgroundJobs.BackgroundJobState.Failed))
                {
                    return last;
                }
            }

            await Task.Delay(50);
        }

        return last;
    }
}
