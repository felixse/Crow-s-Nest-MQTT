namespace CrowsNestMqtt.AppHostTests;

using Xunit;

public sealed class SquidProxyConfigurationTests
{
    [Fact]
    public void AppHost_MountsSquidPolicyThatAllowsEmqxWebSocketConnect()
    {
        var repositoryRoot = FindRepositoryRoot();
        var squidConfigPath = Path.Combine(
            repositoryRoot,
            "src",
            "AppHost",
            "squid",
            "crowsnest.conf");
        var appHostProgramPath = Path.Combine(
            repositoryRoot,
            "src",
            "AppHost",
            "Program.cs");

        Assert.True(File.Exists(squidConfigPath), $"Missing Squid configuration: {squidConfigPath}");

        var squidConfig = File.ReadAllText(squidConfigPath);
        Assert.Contains("acl SSL_ports port 8083", squidConfig, StringComparison.Ordinal);
        Assert.Contains("http_access allow localnet CONNECT", squidConfig, StringComparison.Ordinal);

        var appHostProgram = File.ReadAllText(appHostProgramPath);
        Assert.Contains(
            ".WithBindMount(\"./squid/crowsnest.conf\", \"/etc/squid/conf.d/crowsnest.conf\", isReadOnly: true)",
            appHostProgram,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "CrowsNestMQTT.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Could not locate repository root from {AppContext.BaseDirectory}.");
    }
}
