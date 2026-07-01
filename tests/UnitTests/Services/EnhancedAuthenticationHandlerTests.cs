using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using CrowsNestMqtt.BusinessLogic.Services;
using CrowsNestMqtt.Utils;
using Microsoft.IdentityModel.Tokens;
using MQTTnet;
using MQTTnet.Packets;
using Xunit;

namespace CrowsNestMqtt.UnitTests.Services;

public class EnhancedAuthenticationHandlerTests
{
    private static string GenerateTestToken(DateTime expiry)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes("a-super-secret-key-that-is-long-enough");
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[] { new Claim("name", "testuser") }),
            NotBefore = expiry.AddMinutes(-30),
            Expires = expiry,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    [Fact]
    public void ConnectAsync_WithValidToken_LogsInformation()
    {
        // Arrange
        var validToken = GenerateTestToken(DateTime.UtcNow.AddHours(1));
        var options = new MqttClientOptions { AuthenticationMethod = "K8S-SAT", AuthenticationData = Encoding.UTF8.GetBytes(validToken) };
        var handler = new EnhancedAuthenticationHandler(options);
        
        var logMessages = new System.Collections.Generic.List<string>();
        Action<string, string> logHandler = (level, message) => { if (level == "Information") logMessages.Add(message); };
        AppLogger.OnLogMessage += logHandler;

        try
        {
            // Act
            handler.Configure();

            // Assert
            Assert.Contains(logMessages, log => log.Contains("Enhanced Authentication (K8S-SAT): Token is valid until"));
        }
        finally
        {
            // Cleanup
            AppLogger.OnLogMessage -= logHandler;
        }
    }

    [Fact]
    public void  ConnectAsync_WithExpiredToken_LogsWarning()
    {
        // Arrange
        var expiredToken = GenerateTestToken(DateTime.UtcNow.AddHours(-1));
        var options = new MqttClientOptions { AuthenticationMethod = "K8S-SAT", AuthenticationData = Encoding.UTF8.GetBytes(expiredToken) };
        var handler = new EnhancedAuthenticationHandler(options);

        var logMessages = new System.Collections.Generic.List<string>();
        Action<string, string> logHandler = (level, message) => { if (level == "Warning") logMessages.Add(message); };
        AppLogger.OnLogMessage += logHandler;

        try
        {
            // Act
            handler.Configure();

            // Assert
            Assert.Contains(logMessages, log => log.Contains("Enhanced Authentication (K8S-SAT): Token has expired on"));
        }
        finally
        {
            // Cleanup
            AppLogger.OnLogMessage -= logHandler;
        }
    }

    [Fact]
    public void  ConnectAsync_WithMissingToken_LogsWarning()
    {
        // Arrange
        var options = new MqttClientOptions(); // No user properties
        var handler = new EnhancedAuthenticationHandler(options);
        
        var logMessages = new System.Collections.Generic.List<string>();
        Action<string, string> logHandler = (level, message) => { if (level == "Warning") logMessages.Add(message); };
        AppLogger.OnLogMessage += logHandler;

        try
        {
            // Act
            handler.Configure();

            // Assert
            Assert.Contains(logMessages, log => log.Contains("Enhanced Authentication: Token not found in UserProperties."));
        }
        finally
        {
            // Cleanup
            AppLogger.OnLogMessage -= logHandler;
        }
    }
    
    [Fact]
    public void ConnectAsync_WithEmptyToken_LogsWarning()
    {
        // Arrange
        var options = new MqttClientOptions { AuthenticationData = Encoding.UTF8.GetBytes(string.Empty) };
        var handler = new EnhancedAuthenticationHandler(options);

        var logMessages = new System.Collections.Generic.List<string>();
        Action<string, string> logHandler = (level, message) => { if (level == "Warning") logMessages.Add(message); };
        AppLogger.OnLogMessage += logHandler;

        try
        {
            // Act
            handler.Configure();

            // Assert
            Assert.Contains(logMessages, log => log.Contains("Enhanced Authentication: Token not found in UserProperties."));
        }
        finally
        {
            // Cleanup
            AppLogger.OnLogMessage -= logHandler;
        }
    }

    // --- Azure OAUTH2-JWT tests ---

    /// <summary>
    /// Minimal <see cref="IAccessTokenProvider"/> stub for tests; returns a canned token
    /// and increments a call counter so tests can assert it was invoked.
    /// </summary>
    private sealed class StubAccessTokenProvider : IAccessTokenProvider
    {
        public int CallCount { get; private set; }
        public string Token { get; set; } = "stub-token";
        public DateTimeOffset ExpiresOn { get; set; } = DateTimeOffset.UtcNow.AddHours(1);

        public Task<AccessTokenResult> GetTokenAsync(System.Threading.CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new AccessTokenResult(Token, ExpiresOn));
        }
    }

    [Fact]
    public void Configure_WithAzureMode_AssignsHandlerToOptions()
    {
        var validToken = GenerateTestToken(DateTime.UtcNow.AddHours(1));
        var options = new MqttClientOptions
        {
            AuthenticationMethod = "OAUTH2-JWT",
            AuthenticationData = Encoding.UTF8.GetBytes(validToken),
        };
        var provider = new StubAccessTokenProvider();
        var handler = new EnhancedAuthenticationHandler(options, provider);

        handler.Configure();

        Assert.Same(handler, options.EnhancedAuthenticationHandler);
    }

    [Fact]
    public void Configure_WithOAuth2Jwt_LogsTokenValidity()
    {
        var validToken = GenerateTestToken(DateTime.UtcNow.AddHours(1));
        var options = new MqttClientOptions
        {
            AuthenticationMethod = "OAUTH2-JWT",
            AuthenticationData = Encoding.UTF8.GetBytes(validToken),
        };
        var provider = new StubAccessTokenProvider();
        var handler = new EnhancedAuthenticationHandler(options, provider);

        var logMessages = new System.Collections.Generic.List<string>();
        Action<string, string> logHandler = (level, message) =>
        {
            if (level == "Information") logMessages.Add(message);
        };
        AppLogger.OnLogMessage += logHandler;

        try
        {
            handler.Configure();
            Assert.Contains(logMessages, log => log.Contains("Enhanced Authentication (OAUTH2-JWT): Token is valid until"));
        }
        finally
        {
            AppLogger.OnLogMessage -= logHandler;
        }
    }
}
