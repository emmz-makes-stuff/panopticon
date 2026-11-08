using Panopticon.Contracts;

namespace Panopticon.Server.Tests;

public class SmokeTests
{
    [Fact]
    public void Contracts_name_the_report_progress_tool() =>
        Assert.Equal("report_progress", ContractsAssembly.ReportProgressToolName);
}
