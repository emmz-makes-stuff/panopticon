using Panopticon.Server;

var builder = WebApplication.CreateBuilder(args);
builder.AddPanopticon();

var app = builder.Build();
app.MapPanopticon();

app.Run();

/// <summary>Entry point, exposed for <c>WebApplicationFactory</c> in tests.</summary>
public partial class Program;
