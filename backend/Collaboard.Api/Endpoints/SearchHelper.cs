using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Endpoints;

internal static class SearchHelper
{
    public static IQueryable<CardItem> ApplySearchFilter(IQueryable<CardItem> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return query;
        }

        var term = search.Trim();

        // #123 — exact card number lookup
        if (term.StartsWith('#') && long.TryParse(term[1..], out var cardNumber))
        {
            return query.Where(c => c.Number == cardNumber);
        }

        // Plain number — match card number OR name/description
        if (long.TryParse(term, out var num))
        {
            var pattern = $"%{EscapeLike(term)}%";
            return query.Where(c =>
                c.Number == num
                || EF.Functions.Like(c.Name, pattern, "\\")
                || EF.Functions.Like(c.DescriptionMarkdown, pattern, "\\"));
        }

        // Free-text — match name or description
        var likePattern = $"%{EscapeLike(term)}%";
        return query.Where(c =>
            EF.Functions.Like(c.Name, likePattern, "\\")
            || EF.Functions.Like(c.DescriptionMarkdown, likePattern, "\\"));
    }

    private static string EscapeLike(string term) =>
        term.Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_")
            .Replace("[", "\\[");
}
