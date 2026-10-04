using Aircane.Application.Abstractions;
using Aircane.Application.DocumentProcessing;
using Microsoft.Extensions.Logging;
using TesseractOCR;
using TesseractOCR.Enums;

namespace Aircane.Infrastructure.DocumentProcessing;

/// <summary>
/// OCR engine backed by Tesseract 5 (via the TesseractOCR wrapper).
/// </summary>
/// <remarks>
/// <para>
/// This engine depends on two runtime pieces that are NOT guaranteed to exist in every
/// environment: the native <c>libtesseract</c>/<c>libleptonica</c> binaries (bundled for
/// Windows by the NuGet package, but system-provided on Linux/macOS) and the language
/// <c>tessdata</c> files, which the user must supply.
/// </para>
/// <para>
/// Per the <see cref="IOcrEngine"/> contract this type never throws during construction
/// and never throws from <see cref="RecognizeAsync"/>. When the native library or tessdata
/// is missing it reports <see cref="IsAvailable"/> as <c>false</c> and returns
/// <see cref="OcrResult.Empty"/>, allowing the import pipeline to fall back to marking the
/// document as OCR-required.
/// </para>
/// <para>
/// tessdata location is resolved from <c>Ocr:TessdataPath</c> (see <see cref="OcrOptions"/>),
/// falling back to a <c>tessdata</c> folder next to the application. The language defaults to
/// English but can be overridden via <c>Ocr:Language</c>.
/// </para>
/// </remarks>
public sealed class TesseractOcrEngine : IOcrEngine, IDisposable
{
    private readonly ILogger<TesseractOcrEngine> _logger;
    private readonly string _tessdataPath;
    private readonly Language _language;
    private readonly float _minConfidence;

    private readonly object _initLock = new();
    private bool _initialized;
    private Engine? _engine;
    private string _statusDescription = "OCR not yet initialized.";

    public TesseractOcrEngine(ILogger<TesseractOcrEngine> logger, OcrOptions options)
    {
        _logger = logger;
        _tessdataPath = string.IsNullOrWhiteSpace(options.TessdataPath)
            ? Path.Combine(AppContext.BaseDirectory, "tessdata")
            : options.TessdataPath;
        _language = ParseLanguage(options.Language);
        _minConfidence = options.MinConfidence;
    }

    /// <inheritdoc />
    public bool IsAvailable
    {
        get
        {
            EnsureInitialized();
            return _engine is not null;
        }
    }

    /// <inheritdoc />
    public string StatusDescription
    {
        get
        {
            EnsureInitialized();
            return _statusDescription;
        }
    }

    /// <inheritdoc />
    public Task<OcrResult> RecognizeAsync(
        byte[] imageBytes, float? minConfidenceOverride = null, CancellationToken ct = default)
    {
        if (imageBytes is null || imageBytes.Length == 0)
            return Task.FromResult(OcrResult.Empty);

        EnsureInitialized();
        if (_engine is null)
            return Task.FromResult(OcrResult.Empty);

        ct.ThrowIfCancellationRequested();

        var threshold = minConfidenceOverride ?? _minConfidence;

        try
        {
            // The wrapper is synchronous and CPU-bound; run inline.
            using var img = TesseractOCR.Pix.Image.LoadFromMemory(imageBytes);
            using var page = _engine.Process(img);

            var text = page.Text ?? string.Empty;
            var confidence = page.MeanConfidence; // 0..1

            if (confidence < threshold)
            {
                _logger.LogDebug(
                    "OCR result discarded: mean confidence {Confidence:P0} below threshold {Threshold:P0}.",
                    confidence, threshold);
                return Task.FromResult(OcrResult.Empty);
            }

            return Task.FromResult(new OcrResult(text, confidence));
        }
        catch (Exception ex)
        {
            // Recognition failures (unsupported image, decode errors) are non-fatal.
            _logger.LogWarning(ex, "OCR recognition failed for an image; skipping.");
            return Task.FromResult(OcrResult.Empty);
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        lock (_initLock)
        {
            if (_initialized)
                return;

            try
            {
                if (!Directory.Exists(_tessdataPath))
                {
                    _statusDescription =
                        $"OCR unavailable: tessdata directory not found at '{_tessdataPath}'. " +
                        "Place Tesseract language files there (see docs/setup) to enable OCR.";
                    _logger.LogInformation("{Status}", _statusDescription);
                    return;
                }

                // Constructing the Engine loads the native library and language data.
                // A missing native binary or corrupt/missing traineddata throws here.
                _engine = new Engine(_tessdataPath, _language, EngineMode.Default);
                _statusDescription =
                    $"OCR available: Tesseract engine loaded (language '{_language}', tessdata '{_tessdataPath}').";
                _logger.LogInformation("{Status}", _statusDescription);
            }
            catch (Exception ex)
            {
                _engine = null;
                _statusDescription =
                    "OCR unavailable: could not load the Tesseract native engine or language data " +
                    $"({ex.GetType().Name}: {ex.Message}). Scanned PDFs will be marked OCR-required.";
                _logger.LogWarning(ex, "Tesseract OCR engine could not be initialized; OCR disabled.");
            }
            finally
            {
                _initialized = true;
            }
        }
    }

    private Language ParseLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return Language.English;

        if (Enum.TryParse<Language>(language, ignoreCase: true, out var parsed))
            return parsed;

        _logger.LogWarning(
            "Unknown OCR language '{Language}'; defaulting to English. Use a TesseractOCR Language enum name.",
            language);
        return Language.English;
    }

    public void Dispose() => _engine?.Dispose();
}
