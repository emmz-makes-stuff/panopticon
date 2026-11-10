using System.Text;

namespace Panopticon.Contracts;

/// <summary>One of the reporter's Projects (projects D6): its id is what <c>.panopticon.json</c> holds.</summary>
/// <param name="Role">The reporter's role in it: <c>owner</c>, <c>editor</c> or <c>viewer</c>.</param>
public sealed record ProjectSummary(string Id, string Name, string Code, string Role);

/// <summary>The <c>list_projects</c> tool's result, from the server and from the bridge alike (projects D6).</summary>
public sealed record ListProjectsResult(IReadOnlyList<ProjectSummary> Projects)
{
    /// <summary>
    /// The text an agent reads: one line per Project, e.g. "PAN · Panopticon · owner · id 6f1c…", and how to link a
    /// repo, or a line saying there are none.
    /// </summary>
    public string Describe()
    {
        if (Projects.Count == 0)
        {
            return "You aren't a member of any Project. Without one, your reports are your own.";
        }

        var text = new StringBuilder();
        foreach (var project in Projects)
        {
            text.Append(project.Code).Append(" · ").Append(project.Name).Append(" · ").Append(project.Role)
                .Append(" · id ").AppendLine(project.Id);
        }

        return text.Append("To link a repo, write {\"project\": \"<id>\"} to .panopticon.json at its root.").ToString();
    }
}
