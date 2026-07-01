using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CrowsNestMqtt.BusinessLogic;
using CrowsNestMqtt.BusinessLogic.Commands;
using CrowsNestMqtt.BusinessLogic.Services;
using CrowsNestMqtt.UI.ViewModels;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace CrowsNestMqtt.UnitTests.ViewModels;

public sealed class AzureWhoAmIDispatchTests : IDisposable
{
    private sealed class StubAccessTokenProvider : IAccessTokenProvider
    {
        public string Token { get; set; } = string.Empty;
        public DateTimeOffset ExpiresOn { get; set; } = DateTimeOffset.UtcNow.AddHours(1);
        public int CallCount { get; private set; }
        public Exception? ToThrow { get; set; }

        public Task<AccessTokenResult> GetTokenAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (ToThrow is not null)
            {
                throw ToThrow;
            }
            return Task.FromResult(new AccessTokenResult(Token, ExpiresOn));
        }
    }

    private readonly StubAccessTokenProvider _tokenProvider = new();
    private readonly MainViewModel _viewModel;
    private readonly MethodInfo _dispatchMethod;

    public AzureWhoAmIDispatchTests()
    {
        var parser = Substitute.For<ICommandParserService>();
        var mqtt = Substitute.For<IMqttService>();
        _viewModel = new MainViewModel(
            commandParserService: parser,
            mqttService: mqtt,
            uiScheduler: Scheduler.Immediate,
            azureTokenProviderFactory: _ => _tokenProvider);

        _dispatchMethod = typeof(MainViewModel).GetMethod(
            "DispatchCommand", BindingFlags.NonPublic | BindingFlags.Instance)!;
    }

    public void Dispose() => _viewModel.Dispose();

    private void Dispatch(CommandType type, params string[] args)
    {
        var command = new ParsedCommand(type, args.ToList().AsReadOnly());
        _dispatchMethod.Invoke(_viewModel, new object[] { command });
    }

    private static string BuildJwt(params (string type, string value)[] claims)
    {
        var handler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes("test-key-that-is-long-enough-for-hmac-signing");
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims.Select(c => new Claim(c.type, c.value))),
            Expires = DateTime.UtcNow.AddHours(1),
            Issuer = "https://sts.local",
            Audience = "https://eventgrid.azure.net",
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature),
        };
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    [Fact]
    public async Task Dispatch_AzureWhoAmI_LogsUpnAndOidAndAppId()
    {
        _tokenProvider.Token = BuildJwt(
            ("upn", "alkopke@example.com"),
            ("oid", "11111111-2222-3333-4444-555555555555"),
            ("name", "Alexander Kopke"),
            ("tid", "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            ("idtyp", "user"));

        Dispatch(CommandType.AzureWhoAmI);
        // ExecuteAzureWhoAmIAsync is dispatched fire-and-forget; poll briefly.
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline
            && !(_viewModel.StatusBarText ?? string.Empty).Contains("Signed in as"))
        {
            await Task.Delay(50);
        }

        Assert.Contains("upn=alkopke@example.com", _viewModel.StatusBarText ?? string.Empty);
        Assert.Contains("oid=11111111-2222-3333-4444-555555555555", _viewModel.StatusBarText ?? string.Empty);
        Assert.Contains("name=Alexander Kopke", _viewModel.StatusBarText ?? string.Empty);
        Assert.Contains(":setclientid", _viewModel.StatusBarText ?? string.Empty);
        Assert.Equal(1, _tokenProvider.CallCount);
    }

    [Fact]
    public async Task Dispatch_AzureWhoAmI_FallsBackToPreferredUsername_WhenUpnMissing()
    {
        _tokenProvider.Token = BuildJwt(
            ("preferred_username", "alkopke@example.com"),
            ("oid", "abc123"));

        Dispatch(CommandType.AzureWhoAmI);
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline
            && !(_viewModel.StatusBarText ?? string.Empty).Contains("Signed in as"))
        {
            await Task.Delay(50);
        }

        Assert.Contains("upn=alkopke@example.com", _viewModel.StatusBarText ?? string.Empty);
        Assert.Contains("oid=abc123", _viewModel.StatusBarText ?? string.Empty);
    }

    [Fact]
    public async Task Dispatch_AzureWhoAmI_HandlesCredentialFailureGracefully()
    {
        _tokenProvider.ToThrow = new InvalidOperationException("No credentials configured");

        Dispatch(CommandType.AzureWhoAmI);
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline
            && !(_viewModel.StatusBarText ?? string.Empty).Contains("whoami failed"))
        {
            await Task.Delay(50);
        }

        Assert.Contains("Azure whoami failed", _viewModel.StatusBarText ?? string.Empty);
        Assert.Contains("az login", _viewModel.StatusBarText ?? string.Empty);
    }
}
