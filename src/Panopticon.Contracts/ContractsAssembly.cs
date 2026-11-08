namespace Panopticon.Contracts;

/// <summary>Marker for the shared wire contracts between the server and the CLI (design D15).</summary>
public static class ContractsAssembly
{
    /// <summary>The MCP tool both the server endpoint and the CLI bridge expose.</summary>
    public const string ReportProgressToolName = "report_progress";
}
