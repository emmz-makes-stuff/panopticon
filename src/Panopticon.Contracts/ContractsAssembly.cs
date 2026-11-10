namespace Panopticon.Contracts;

/// <summary>Marker for the shared wire contracts between the server and the CLI (design D15).</summary>
public static class ContractsAssembly
{
    /// <summary>The MCP tool both the server endpoint and the CLI bridge expose.</summary>
    public const string ReportProgressToolName = "report_progress";

    /// <summary>The MCP tool listing the reporter's Projects, on the server endpoint and the CLI bridge (projects D6).</summary>
    public const string ListProjectsToolName = "list_projects";
}
