var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/healthz", () => TypedResults.Ok("ok"));

app.Run();

/// <summary>Entry point, exposed for <c>WebApplicationFactory</c> in tests.</summary>
public partial class Program;
