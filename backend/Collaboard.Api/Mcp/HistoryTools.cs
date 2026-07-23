using System.ComponentModel;
using System.Text.Json;
using Collaboard.Api.Endpoints;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Collaboard.Api.Mcp;

[McpServerToolType]
public sealed class HistoryTools(BoardDbContext db, McpAuthService auth)
{
    [McpServerTool(Name = "get_card_history", ReadOnly = true, Destructive = false)]
    [Description("Get the edit history of a card's description — every recorded version with who replaced it and when, newest first. Defaults to format 'diff', which returns a unified (git-style) diff of what each edit changed instead of full snapshots; pass 'full' for the whole text at each revision or 'both' for each. Supply from AND to to get the diff between two arbitrary revisions instead of the whole trail. History starts at a card's first description edit — a never-edited card returns an empty trail and its current text is available from get_card.")]
    public async Task<string> GetCardHistoryAsync
    (
        [Description("Your auth key")] string authKey,
        [Description("The ID (guid) of the card (provide this or cardNumber)")] Guid? cardId = null,
        [Description("The card number (provide this or cardId). Requires boardId or boardSlug.")] long? cardNumber = null,
        [Description("Board ID (required when using cardNumber)")] Guid? boardId = null,
        [Description("Board slug (alternative to boardId when using cardNumber)")] string? boardSlug = null,
        [Description("Which field's history to return. Defaults to 'description', the only field recorded today.")] string? field = null,
        [Description("One of 'diff' (default — unified diff of what each edit changed), 'full' (the whole value at each revision), or 'both'.")] string? format = null,
        [Description("Start revision of an arbitrary-pair comparison. Requires 'to'.")] int? from = null,
        [Description("End revision of an arbitrary-pair comparison. Requires 'from'.")] int? to = null,
        CancellationToken ct = default
    )
    {
        var (_, error) = await auth.RequireUserAsync(authKey, ct);
        if (error is not null)
        {
            return error;
        }

        var (resolvedCardId, resolveError) = await McpCardResolver.ResolveCardIdAsync(db, cardId, cardNumber, boardId, boardSlug, ct);
        if (resolveError is not null)
        {
            return resolveError;
        }

        if (!await db.Cards.AnyAsync(c => c.Id == resolvedCardId!.Value, ct))
        {
            return "Error: Card not found.";
        }

        var (resolvedField, fieldError) = CardHistoryHelper.ResolveField(field);
        if (fieldError is not null)
        {
            return $"Error: {fieldError}";
        }

        // Diff is the default here and 'both' on REST: a bot asking "what changed?" wants the
        // change, not N full snapshots it has to diff itself, and snapshots of a long description
        // are the expensive part of this response.
        if (!CardHistoryBuilder.TryParseFormat(format, CardHistoryFormat.Diff, out var resolvedFormat))
        {
            return $"Error: {CardHistoryBuilder.FormatError}";
        }

        if (from.HasValue != to.HasValue)
        {
            return "Error: from and to must be supplied together.";
        }

        if (from.HasValue && to.HasValue)
        {
            var (pair, pairError) = await CardHistoryBuilder.BuildPairAsync(db, resolvedCardId!.Value, resolvedField!, resolvedFormat, from.Value, to.Value, ct);

            return pairError is not null
                ? $"Error: {pairError}"
                : JsonSerializer.Serialize(pair, JsonSerializerOptions.Web);
        }

        var trail = await CardHistoryBuilder.BuildTrailAsync(db, resolvedCardId!.Value, resolvedField!, resolvedFormat, ct);
        return JsonSerializer.Serialize(trail, JsonSerializerOptions.Web);
    }
}
