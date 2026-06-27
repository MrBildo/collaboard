using System.Reflection;
using Collaboard.Api.Events;
using Shouldly;

namespace Collaboard.Api.Tests;

// WebhookEventTypes — the catalog SoT and selection semantics (#326). The reflection test (S4) is
// the drift guard: it keeps `selectable ≡ deliverable` honest as M2 grows the catalog.
public sealed class WebhookEventTypesTests
{
    // S4 — every event-type const must appear in All, so a new const can't be silently unselectable.
    // The wildcard sentinel and a deliverable-only Ping (M2) are the only non-selectable consts and
    // are excluded by design.
    [Fact]
    public void EveryEventTypeConst_IsInTheAllSet()
    {
        var eventTypeConsts = typeof(WebhookEventTypes)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => new { f.Name, Value = (string)f.GetValue(null)! })
            .Where(f => f.Value != WebhookEventTypes.Wildcard && f.Name != "Ping")
            .ToList();

        eventTypeConsts.ShouldNotBeEmpty();

        foreach (var c in eventTypeConsts)
        {
            WebhookEventTypes.All.ShouldContain
            (
                c.Value,
                $"{c.Name} is a public event-type const but is missing from All (selectable != deliverable drift)"
            );
        }
    }

    // M2 (#329) is now COMPLETE: the lane and board families close the catalog. All board-scoped
    // events the project raises are both emitted and selectable. (User-account events are out of
    // scope; Ping and the Wildcard sentinel are deliverable-only / not event types.)
    [Fact]
    public void M2_All_ContainsTheFullCatalog() =>
        WebhookEventTypes.All.ShouldBe(
            [
                WebhookEventTypes.CardCreated,
                WebhookEventTypes.CardMoved,
                WebhookEventTypes.CardUpdated,
                WebhookEventTypes.CardArchived,
                WebhookEventTypes.CardRestored,
                WebhookEventTypes.CardLabeled,
                WebhookEventTypes.CardUnlabeled,
                WebhookEventTypes.CommentCreated,
                WebhookEventTypes.CommentUpdated,
                WebhookEventTypes.CommentDeleted,
                WebhookEventTypes.LabelCreated,
                WebhookEventTypes.LabelUpdated,
                WebhookEventTypes.LabelDeleted,
                WebhookEventTypes.AttachmentCreated,
                WebhookEventTypes.AttachmentDeleted,
                WebhookEventTypes.LaneCreated,
                WebhookEventTypes.LaneRenamed,
                WebhookEventTypes.LaneReordered,
                WebhookEventTypes.LaneDeleted,
                WebhookEventTypes.BoardCreated,
                WebhookEventTypes.BoardRenamed,
                WebhookEventTypes.BoardDeleted,
            ],
            ignoreOrder: true);

    [Fact]
    public void IsValidSelection_AcceptsKnownTypesAndWildcard_RejectsUnknown()
    {
        WebhookEventTypes.IsValidSelection(WebhookEventTypes.CardCreated).ShouldBeTrue();
        WebhookEventTypes.IsValidSelection(WebhookEventTypes.CommentCreated).ShouldBeTrue();
        WebhookEventTypes.IsValidSelection(WebhookEventTypes.LaneReordered).ShouldBeTrue();   // M2 — catalog now closed
        WebhookEventTypes.IsValidSelection(WebhookEventTypes.BoardCreated).ShouldBeTrue();    // M2 — catalog now closed
        WebhookEventTypes.IsValidSelection(WebhookEventTypes.Wildcard).ShouldBeTrue();
        WebhookEventTypes.IsValidSelection("user.created").ShouldBeFalse();   // user-account events are out of scope
        WebhookEventTypes.IsValidSelection("nonsense").ShouldBeFalse();
    }

    [Fact]
    public void Matches_ExactType_Matches()
    {
        WebhookEventTypes.Matches([WebhookEventTypes.CardMoved], WebhookEventTypes.CardMoved).ShouldBeTrue();
        WebhookEventTypes.Matches([WebhookEventTypes.CardCreated], WebhookEventTypes.CardMoved).ShouldBeFalse();
    }

    [Fact]
    public void Matches_Wildcard_MatchesEverything()
    {
        WebhookEventTypes.Matches([WebhookEventTypes.Wildcard], WebhookEventTypes.CardCreated).ShouldBeTrue();
        WebhookEventTypes.Matches([WebhookEventTypes.Wildcard], "any.future.event").ShouldBeTrue();
    }

    [Fact]
    public void Matches_EmptySelection_MatchesNothing() =>
        WebhookEventTypes.Matches([], WebhookEventTypes.CardCreated).ShouldBeFalse();
}
