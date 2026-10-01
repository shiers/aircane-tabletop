namespace Aircane.Application.Feedback;

/// <summary>
/// Thrown when a bug report cannot be submitted because the feedback feature is not configured
/// on this instance (for example, no GitHub token has been supplied). The controller maps this
/// to an HTTP 503 with a clear message. The exception message never contains the token.
/// </summary>
public sealed class FeedbackNotConfiguredException : Exception
{
    public FeedbackNotConfiguredException()
        : base("Feedback is not configured on this instance.")
    {
    }

    public FeedbackNotConfiguredException(string message)
        : base(message)
    {
    }
}
