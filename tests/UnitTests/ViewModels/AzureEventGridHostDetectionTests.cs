using CrowsNestMqtt.UI.ViewModels;
using Xunit;

namespace CrowsNestMqtt.UnitTests.ViewModels;

public class AzureEventGridHostDetectionTests
{
    [Theory]
    [InlineData("myns.eastus-1.ts.eventgrid.azure.net", true)]
    [InlineData("MYNS.EASTUS-1.TS.EVENTGRID.AZURE.NET", true)]
    [InlineData("namespace.westeurope-2.ts.eventgrid.azure.net", true)]
    // HTTP data-plane suffix is also recognized so pasted URLs trigger auto-config;
    // the hostname normalizer then rewrites them to the MQTT topic-space form.
    [InlineData("myns.northeurope-1.eventgrid.azure.net", true)]
    [InlineData("broker.hivemq.com", false)]
    [InlineData("myns.ts.eventgrid.azure.net.malicious.com", false)] // suffix only, not contains
    [InlineData("localhost", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsAzureEventGridHost_RecognizesEventGridSuffix(string? host, bool expected)
    {
        Assert.Equal(expected, MainViewModel.IsAzureEventGridHost(host));
    }
}
