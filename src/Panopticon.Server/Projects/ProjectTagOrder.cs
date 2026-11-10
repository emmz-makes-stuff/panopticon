namespace Panopticon.Server.Projects;

/// <summary>
/// The one order for a Project's tags (tags D6): by key, then value (a bare key first), compared ordinally on the
/// lower-cased keys. It's applied in memory, never in SQL, where the database's collation would order <c>a_b</c> and
/// <c>ab</c> differently from the cards, and every list (the Project page, <c>project-tags-changed</c>,
/// <c>list_projects</c>, <c>tag_change</c>'s messages and the cards) agrees.
/// </summary>
public static class ProjectTagOrder
{
    public static IEnumerable<T> InTagOrder<T>(this IEnumerable<T> tags, Func<T, string> keyKey, Func<T, string> valueKey) =>
        tags.OrderBy(keyKey, StringComparer.Ordinal).ThenBy(valueKey, StringComparer.Ordinal);
}
