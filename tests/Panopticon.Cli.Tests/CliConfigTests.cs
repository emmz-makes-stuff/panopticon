using System.Runtime.Versioning;
using Panopticon.Cli.Bridge;
using Panopticon.Cli.Config;

namespace Panopticon.Cli.Tests;

// specs/cli-bridge "Configuration" ("Saved login", "Environment overrides the saved login"); design D14.
public sealed class CliConfigTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("panopticon-config-").FullName;

    private string ConfigPath => Path.Combine(_root, "panopticon", "config.json");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static Func<string, string?> Env(Dictionary<string, string?> values) =>
        name => values.TryGetValue(name, out var value) ? value : null;

    private BridgeSettings Resolve(Dictionary<string, string?> env, StringWriter? stderr = null) =>
        BridgeSettings.Resolve(Env(env), new CliConfigFile(ConfigPath), stderr ?? new StringWriter(), _root, "vm-1");

    [Fact]
    public void Saved_login_is_used_when_neither_variable_is_set()
    {
        new CliConfigFile(ConfigPath).Write(new CliConfig("https://p.example.com", "pnp_saved"));

        var settings = Resolve([]);

        Assert.Equal(("https://p.example.com", "pnp_saved"), (settings.ServerUrl, settings.Token));
    }

    [Fact]
    public void Environment_overrides_the_saved_login_value_by_value()
    {
        new CliConfigFile(ConfigPath).Write(new CliConfig("https://p.example.com", "pnp_saved"));

        var tokenOnly = Resolve(new() { [BridgeSettings.TokenVariable] = "pnp_env" });
        var both = Resolve(new() { [BridgeSettings.TokenVariable] = "pnp_env", [BridgeSettings.ServerUrlVariable] = "https://other.example.com" });

        Assert.Equal(("https://p.example.com", "pnp_env"), (tokenOnly.ServerUrl, tokenOnly.Token));
        Assert.Equal(("https://other.example.com", "pnp_env"), (both.ServerUrl, both.Token));
    }

    [Fact]
    public void A_saved_token_is_not_used_for_another_server()
    {
        new CliConfigFile(ConfigPath).Write(new CliConfig("https://p.example.com/", "pnp_saved"));

        var settings = Resolve(new() { [BridgeSettings.ServerUrlVariable] = "https://other.example.com" });

        Assert.Null(settings.Token);
        Assert.Equal("https://p.example.com/", settings.SavedLoginServer);
        var error = Assert.Throws<BridgeException>(() => settings.RequireServer());
        Assert.Contains("saved login is for https://p.example.com/", error.Message, StringComparison.Ordinal);
        Assert.Contains("panopticon login --server https://other.example.com", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("pnp_saved", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://p.example.com")]
    [InlineData("https://P.Example.com/")]
    [InlineData("https://p.example.com/mcp")]
    [InlineData("https://p.example.com:443/mcp/")]
    public void A_saved_token_is_used_when_the_environment_names_the_saved_server(string serverUrl)
    {
        new CliConfigFile(ConfigPath).Write(new CliConfig("https://p.example.com/", "pnp_saved"));

        var settings = Resolve(new() { [BridgeSettings.ServerUrlVariable] = serverUrl });

        Assert.Equal("pnp_saved", settings.Token);
        Assert.Null(settings.SavedLoginServer);
    }

    [Theory]
    [InlineData("http://p.example.com/")]
    [InlineData("https://p.example.com:8443/")]
    [InlineData("https://p.example.com/team-b/")]
    public void A_different_scheme_port_or_path_is_another_server(string serverUrl) =>
        Assert.False(BridgeSettings.SameServer("https://p.example.com/", serverUrl));

    [Fact]
    public void A_blank_variable_counts_as_unset()
    {
        new CliConfigFile(ConfigPath).Write(new CliConfig("https://p.example.com", "pnp_saved"));

        var settings = Resolve(new() { [BridgeSettings.TokenVariable] = "  " });

        Assert.Equal("pnp_saved", settings.Token);
        Assert.False(settings.TokenFromEnvironment);
    }

    [Fact]
    public void The_token_records_whether_it_came_from_the_environment()
    {
        new CliConfigFile(ConfigPath).Write(new CliConfig("https://p.example.com", "pnp_saved"));

        Assert.True(Resolve(new() { [BridgeSettings.TokenVariable] = "pnp_env" }).TokenFromEnvironment);
        Assert.False(Resolve([]).TokenFromEnvironment);
    }

    [Fact]
    public void No_variables_and_no_file_leaves_both_unset_and_logs_nothing()
    {
        var stderr = new StringWriter();

        var settings = Resolve([], stderr);

        Assert.Equal((null, null), (settings.ServerUrl, settings.Token));
        Assert.Empty(stderr.ToString());
    }

    [Fact]
    public void An_unreadable_file_counts_as_unset_and_is_logged_once_without_its_content()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, """{"serverUrl":"https://p.example.com","token":"pnp_secret_value" BROKEN""");
        var stderr = new StringWriter();

        var settings = Resolve([], stderr);

        Assert.Equal((null, null), (settings.ServerUrl, settings.Token));
        var log = stderr.ToString();
        Assert.Single(log.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains(ConfigPath, log, StringComparison.Ordinal);
        Assert.DoesNotContain("pnp_secret_value", log, StringComparison.Ordinal);
        Assert.DoesNotContain("BROKEN", log, StringComparison.Ordinal);
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void A_file_without_read_permission_counts_as_unset()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file modes");
        new CliConfigFile(ConfigPath).Write(new CliConfig("https://p.example.com", "pnp_saved"));
        File.SetUnixFileMode(ConfigPath, UnixFileMode.None);
        var stderr = new StringWriter();

        var settings = Resolve([], stderr);

        Assert.Null(settings.Token);
        Assert.Contains(ConfigPath, stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void The_file_is_written_for_the_user_only_and_leaves_no_temp_file()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file modes");
        var config = new CliConfigFile(ConfigPath);

        config.Write(new CliConfig("https://p.example.com", "pnp_first"));
        config.Write(new CliConfig("https://q.example.com", "pnp_second"));

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(ConfigPath));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(Path.GetDirectoryName(ConfigPath)!));
        Assert.Equal(["config.json"], Directory.GetFiles(Path.GetDirectoryName(ConfigPath)!).Select(Path.GetFileName));
        Assert.Equal(new CliConfig("https://q.example.com", "pnp_second"), config.Read(new StringWriter()));
    }

    // tags D8: read-modify-write, so each writer keeps what it doesn't change.
    [Fact]
    public void A_tag_write_keeps_the_login_and_a_login_write_keeps_the_tags()
    {
        var config = new CliConfigFile(ConfigPath);
        config.Write(new CliConfig("https://p.example.com", "pnp_saved"));

        config.Update(c => c with { Tags = ["dev"] }, startOverIfUnreadable: false);
        config.Update(c => c with { ServerUrl = "https://q.example.com", Token = "pnp_new" }, startOverIfUnreadable: true);

        var saved = config.Read(new StringWriter())!;
        Assert.Equal(("https://q.example.com", "pnp_new"), (saved.ServerUrl, saved.Token));
        Assert.Equal(["dev"], saved.Tags!);
        Assert.Contains("\"tags\"", File.ReadAllText(ConfigPath), StringComparison.Ordinal);
    }

    [Fact]
    public void An_update_with_no_file_starts_from_an_empty_config()
    {
        var written = new CliConfigFile(ConfigPath).Update(c => c with { Tags = ["dev"] }, startOverIfUnreadable: false);

        Assert.Equal((null, null), (written.ServerUrl, written.Token));
        Assert.Equal(["dev"], new CliConfigFile(ConfigPath).Read(new StringWriter())!.Tags!);
    }

    [Fact]
    public void A_tag_write_leaves_an_unreadable_file_alone_and_says_so_without_its_content()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        const string broken = "{ \"token\": \"pnp_secret\", ";
        File.WriteAllText(ConfigPath, broken);
        var config = new CliConfigFile(ConfigPath);

        var error = Assert.Throws<InvalidDataException>(() => config.Update(c => c with { Tags = ["dev"] }, startOverIfUnreadable: false));

        Assert.Equal(broken, File.ReadAllText(ConfigPath));
        Assert.Contains(ConfigPath, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("pnp_secret", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_login_write_replaces_an_unreadable_file()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, "not json");
        var config = new CliConfigFile(ConfigPath);

        config.Update(c => c with { ServerUrl = "https://p.example.com", Token = "pnp_new" }, startOverIfUnreadable: true);

        Assert.Equal(new CliConfig("https://p.example.com", "pnp_new"), config.Read(new StringWriter()));
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void An_update_keeps_the_file_and_directory_for_the_user_only()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file modes");
        var config = new CliConfigFile(ConfigPath);
        config.Write(new CliConfig("https://p.example.com", "pnp_saved"));
        File.SetUnixFileMode(ConfigPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);

        config.Update(c => c with { Tags = ["dev"] }, startOverIfUnreadable: false);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(ConfigPath));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(Path.GetDirectoryName(ConfigPath)!));
        Assert.Equal(["config.json"], Directory.GetFiles(Path.GetDirectoryName(ConfigPath)!).Select(Path.GetFileName));
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Writing_tightens_an_existing_files_mode()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file modes");
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, "{}");
        File.SetUnixFileMode(ConfigPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        new CliConfigFile(ConfigPath).Write(new CliConfig("https://p.example.com", "pnp_saved"));

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(ConfigPath));
    }

    [Theory]
    [InlineData(false, null, "/home/emmz/.config/panopticon/config.json")]
    [InlineData(false, "/xdg", "/xdg/panopticon/config.json")]
    [InlineData(false, "relative/xdg", "/home/emmz/.config/panopticon/config.json")]
    public void Default_path_follows_xdg_on_unix(bool isWindows, string? xdg, string expected)
    {
        var path = CliConfigFile.DefaultPath(Env(new() { ["XDG_CONFIG_HOME"] = xdg }), isWindows, "/home/emmz");

        Assert.Equal(expected, path);
    }

    [Fact]
    public void Default_path_is_under_appdata_on_windows()
    {
        var appData = Path.Combine(_root, "AppData", "Roaming");

        var path = CliConfigFile.DefaultPath(Env(new() { ["APPDATA"] = appData }), isWindows: true, _root);

        Assert.Equal(Path.Combine(appData, "panopticon", "config.json"), path);
    }
}
