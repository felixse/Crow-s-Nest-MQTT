using CrowsNestMqtt.UI.ViewModels;
using Xunit;

namespace CrowsNestMqtt.UnitTests.ViewModels;

public class AzureEventGridHostDetectionTests
{
    [Theory]
    [InlineData("myns.eastus-1.ts.eventgrid.azure.net", true)]
    [InlineData("MYNS.EASTUS-1.TS.EVENTGRID.AZURE.NET", true)]
    [InlineData("namespace.westeurope-2.ts.eventgrid.azure.net", true)]
    [InlineData("broker.hivemq.com", false)]
    [InlineData("eventgrid.azure.net", false)]                       // missing .ts. infix
    [InlineData("myns.ts.eventgrid.azure.net.malicious.com", false)] // suffix only, not contains
    [InlineData("localhost", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsAzureEventGridHost_RecognizesEventGridSuffix(string? host, bool expected)
    {
        Assert.Equal(expected, MainViewModel.IsAzureEventGridHost(host));
    }
}
