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

    // M2 (#329) widens All per family as emit-sites are wired. The card family is live; the
    // comment / lane / board / label / attachment families add their members in follow-on slices,
    // so they are deliberately absent here (selectable ≡ deliverable at every slice boundary).
    [Fact]
    public void M2_All_ContainsExactlyTheCardFamily() =>
        WebhookEventTypes.All.ShouldBe(
            [
                WebhookEventTypes.CardCreated,
                WebhookEventTypes.CardMoved,
                WebhookEventTypes.CardUpdated,
                WebhookEventTypes.CardArchived,
                WebhookEventTypes.CardRestored,
                WebhookEventTypes.CardLabeled,
                WebhookEventTypes.CardUnlabeled,
            ],
            ignoreOrder: true);

    [Fact]
    public void IsValidSelection_AcceptsKnownTypesAndWildcard_RejectsUnknown()
    {
        WebhookEventTypes.IsValidSelection(WebhookEventTypes.CardCreated).ShouldBeTrue();
        WebhookEventTypes.IsValidSelection(WebhookEventTypes.Wildcard).ShouldBeTrue();
        WebhookEventTypes.IsValidSelection("comment.created").ShouldBeFalse();   // M2, not yet live
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
