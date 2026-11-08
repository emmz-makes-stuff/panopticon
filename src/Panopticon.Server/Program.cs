using Panopticon.Server;

// `--migrate` applies pending migrations and exits (design D6: e.g. a pre-deploy step).
// It is removed before the host sees the arguments, because the command-line configuration
// provider rejects a switch without a value.
const string MigrateSwitch = "--migrate";
var migrateOnly = args.Contains(MigrateSwitch, StringComparer.Ordinal);
args = [.. args.Where(a => a != MigrateSwitch)];

var builder = WebApplication.CreateBuilder(args);
builder.AddPanopticon();

var app = builder.Build();
app.MapPanopticon();

if (migrateOnly)
{
    await app.MigrateAsync();
    return;
}

if (app.Environment.IsDevelopment())
{
    await app.MigrateAsync();
}

await app.RunAsync();

/// <summary>Entry point, exposed for <c>WebApplicationFactory</c> in tests.</summary>
public partial class Program;
