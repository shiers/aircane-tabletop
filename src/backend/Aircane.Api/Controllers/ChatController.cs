using Aircane.Api.Authorization;
using Aircane.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aircane.Api.Controllers;

/// <summary>
/// A play session's table chat history, so late joiners and reloaded clients can catch up. Live messages
/// still arrive over the hub (<c>ChatMessageReceived</c>); their <c>messageId</c> is the id used here, so a
/// client can tell which it already has.
/// <para>
/// Open to the host and to approved, non-denied participants of this session (the
/// <see cref="AuthorizationPolicies.SessionMember"/> policy): a waiting participant gets 403 "Waiting for
/// approval", a denied one 403 with <c>code: "participant-denied"</c>, a token from another session 403.
/// Chat is table-wide, so everyone allowed in reads the same messages: none is private or host-only.
/// </para>
/// </summary>
[ApiController]
[Route("api/sessions/{sessionId:guid}/chat")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.SessionMember)]
public sealed class ChatController : ControllerBase
{
    private readonly IChatService _chat;
    private readonly ISessionHostingService _sessions;

    public ChatController(IChatService chat, ISessionHostingService sessions)
    {
        _chat = chat;
        _sessions = sessions;
    }

    /// <summary>
    /// The latest messages, oldest first (default <c>limit</c> 100, at most 200). To page back, pass the id of
    /// the first message you have as <c>before</c> while <c>hasMore</c> is true.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ChatHistoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHistory(
        Guid sessionId,
        [FromQuery] int limit = ChatHistory.DefaultLimit,
        [FromQuery] Guid? before = null,
        CancellationToken cancellationToken = default)
    {
        if (await _sessions.GetSessionAsync(sessionId, cancellationToken) is null)
            return NotFound();

        // Always the current history: a cached copy would hide what was said since.
        Response.Headers.CacheControl = "no-store";
        return Ok(await _chat.GetHistoryAsync(sessionId, limit, before, cancellationToken));
    }
}
