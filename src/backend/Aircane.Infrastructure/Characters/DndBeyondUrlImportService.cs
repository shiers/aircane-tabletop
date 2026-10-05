using System.Globalization;
using System.Net;
using Aircane.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.Characters;

/// <summary>
/// Fetches raw D&amp;D Beyond character JSON via the unofficial character-service API.
/// <para>
/// Security/robustness guarantees: the character URL is NEVER stored or logged (only the parsed
/// numeric id at Debug and the HTTP status code are logged); there is no response caching; and a
/// process-wide runaway guard (single-flight + a minimum interval between outbound calls) prevents
/// a buggy client loop from hammering the upstream service on LAN, independent of tunnel state.
/// </para>
/// </summary>
public sealed class DndBeyondUrlImportService : IDndBeyondUrlImportService
{
    /// <summary>Maximum accepted length of the supplied URL/id input.</summary>
    private const int MaxUrlLength = 2048;

    /// <summary>Minimum interval between outbound calls (process-wide runaway guard).</summary>
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(2);

    // Process-wide single-flight gate + last-call timestamp so a client loop cannot flood the
    // unofficial upstream service. Shared across all instances/requests, independent of tunnel mode.
    private static readonly SemaphoreSlim CallGate = new(1, 1);
    private static DateTimeOffset _lastCallUtc = DateTimeOffset.MinValue;

    private readonly HttpClient _httpClient;
    private readonly ILogger<DndBeyondUrlImportService> _logger;

    public DndBeyondUrlImportService(HttpClient httpClient, ILogger<DndBeyondUrlImportService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> FetchAsync(string characterUrl, CancellationToken ct)
    {
        var id = ParseCharacterId(characterUrl);

        await CallGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var sinceLast = DateTimeOffset.UtcNow - _lastCallUtc;
            if (sinceLast < MinimumInterval)
                await Task.Delay(MinimumInterval - sinceLast, ct).ConfigureAwait(false);

            try
            {
                _logger.LogDebug("Fetching D&D Beyond character {CharacterId}.", id);

                using var response = await _httpClient
                    .GetAsync($"character/v5/character/{id}?includeCustomItems=true", ct)
                    .ConfigureAwait(false);

                _logger.LogDebug(
                    "D&D Beyond character {CharacterId} fetch returned status {StatusCode}.",
                    id, (int)response.StatusCode);

                if (!response.IsSuccessStatusCode)
                    throw MapStatus(response.StatusCode);

                return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            }
            catch (DndBeyondImportException)
            {
                throw;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // HttpClient surfaces a timeout as a cancellation that is NOT our caller's token.
                _logger.LogWarning("D&D Beyond character {CharacterId} fetch timed out.", id);
                throw new DndBeyondImportException(
                    DndBeyondImportStatusKind.Timeout, "The D&D Beyond request timed out.");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning("D&D Beyond character {CharacterId} fetch failed at the network layer.", id);
                throw new DndBeyondImportException(
                    DndBeyondImportStatusKind.Upstream, "The D&D Beyond request failed.", ex);
            }
        }
        finally
        {
            _lastCallUtc = DateTimeOffset.UtcNow;
            CallGate.Release();
        }
    }

    /// <summary>
    /// Extracts the numeric character id from a full dndbeyond.com/characters/{id} URL or a bare
    /// numeric id. Takes the first run of digits in the <c>/characters/</c> segment, or treats a
    /// bare all-digit input as the id. Throws <see cref="DndBeyondImportException"/> (InvalidUrl)
    /// when no id can be parsed or the input is too long.
    /// </summary>
    internal static string ParseCharacterId(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw InvalidUrl();

        var trimmed = input.Trim();
        if (trimmed.Length > MaxUrlLength)
            throw InvalidUrl();

        // Bare numeric id.
        if (trimmed.All(char.IsDigit))
            return trimmed;

        // Locate the "/characters/" segment and read the first digit run after it.
        const string segment = "/characters/";
        var segmentIndex = trimmed.IndexOf(segment, StringComparison.OrdinalIgnoreCase);
        if (segmentIndex < 0)
            throw InvalidUrl();

        var cursor = segmentIndex + segment.Length;
        // Skip any non-digit characters immediately following the segment.
        while (cursor < trimmed.Length && !char.IsDigit(trimmed[cursor]))
            cursor++;

        var start = cursor;
        while (cursor < trimmed.Length && char.IsDigit(trimmed[cursor]))
            cursor++;

        if (cursor == start)
            throw InvalidUrl();

        var id = trimmed[start..cursor];

        // Validate it is a sensible positive integer (no overflow surprises downstream).
        if (!long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var numeric) || numeric <= 0)
            throw InvalidUrl();

        return id;
    }

    private static DndBeyondImportException MapStatus(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Forbidden => new DndBeyondImportException(
            DndBeyondImportStatusKind.Private403, "The D&D Beyond character is private."),
        HttpStatusCode.NotFound => new DndBeyondImportException(
            DndBeyondImportStatusKind.NotFound404, "The D&D Beyond character was not found."),
        _ => new DndBeyondImportException(
            DndBeyondImportStatusKind.Upstream, "The D&D Beyond request failed."),
    };

    private static DndBeyondImportException InvalidUrl() =>
        new(DndBeyondImportStatusKind.InvalidUrl, "The supplied D&D Beyond URL or id could not be parsed.");
}
