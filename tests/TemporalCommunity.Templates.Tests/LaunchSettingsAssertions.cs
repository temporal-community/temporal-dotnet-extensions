using System.Text.Json;
using Xunit;

namespace TemporalCommunity.Templates.Tests;

/// <summary>
/// Structural assertions for generated <c>Properties/launchSettings.json</c> files. Ports are
/// produced by the template engine's <c>port</c> generator at creation time, so URLs are checked
/// for scheme, host, and a valid integer port rather than literal values.
/// </summary>
internal static class LaunchSettingsAssertions
{
    private static readonly string[] WebProfileOrder = ["http", "https"];
    private static readonly string[] AspireProfileOrder = ["https", "http"];

    public static void AssertDevelopmentProfile(string content, string profileName)
    {
        using var settings = JsonDocument.Parse(content);
        var profiles = settings.RootElement.GetProperty("profiles");
        Assert.Equal(profileName, Assert.Single(profiles.EnumerateObject()).Name);

        var profile = profiles.GetProperty(profileName);
        Assert.Equal("Project", profile.GetProperty("commandName").GetString());
        var environmentVariables = profile.GetProperty("environmentVariables");
        Assert.Single(environmentVariables.EnumerateObject());
        AssertVariable(environmentVariables, "DOTNET_ENVIRONMENT", "Development");
        AssertNoTemporalOrOtelVariables(content);
    }

    public static void AssertWebProfiles(string content)
    {
        using var settings = JsonDocument.Parse(content);
        var profiles = settings.RootElement.GetProperty("profiles");
        // `dotnet run` and IDEs use the first profile as the default, so order is part of the contract.
        Assert.Equal(WebProfileOrder, profiles.EnumerateObject().Select(p => p.Name));

        var http = profiles.GetProperty("http");
        var https = profiles.GetProperty("https");
        AssertWebProfile(http);
        AssertWebProfile(https);

        var httpPort = AssertSingleUrl(http.GetProperty("applicationUrl").GetString(), "http");
        var (httpsPort, httpsHttpPort) = AssertHttpsThenHttpUrls(https.GetProperty("applicationUrl").GetString());
        Assert.Equal(httpPort, httpsHttpPort);
        Assert.NotEqual(httpPort, httpsPort);
        AssertNoTemporalOrOtelVariables(content);
    }

    public static void AssertAspireProfiles(string content)
    {
        using var settings = JsonDocument.Parse(content);
        var profiles = settings.RootElement.GetProperty("profiles");
        // `dotnet run` and IDEs use the first profile as the default, so order is part of the contract.
        Assert.Equal(AspireProfileOrder, profiles.EnumerateObject().Select(p => p.Name));

        var https = profiles.GetProperty("https");
        var http = profiles.GetProperty("http");
        AssertAspireProfile(https, "https");
        AssertAspireProfile(http, "http");

        var (httpsPort, httpsHttpPort) = AssertHttpsThenHttpUrls(https.GetProperty("applicationUrl").GetString());
        var httpPort = AssertSingleUrl(http.GetProperty("applicationUrl").GetString(), "http");
        Assert.Equal(httpPort, httpsHttpPort);
        Assert.NotEqual(httpPort, httpsPort);

        var httpVariables = http.GetProperty("environmentVariables");
        Assert.Equal(5, httpVariables.EnumerateObject().Count());
        AssertVariable(httpVariables, "ASPIRE_ALLOW_UNSECURED_TRANSPORT", "true");

        var httpsVariables = https.GetProperty("environmentVariables");
        Assert.Equal(4, httpsVariables.EnumerateObject().Count());
        Assert.False(httpsVariables.TryGetProperty("ASPIRE_ALLOW_UNSECURED_TRANSPORT", out _));
        AssertNoTemporalOrOtelVariables(content);
    }

    private static void AssertWebProfile(JsonElement profile)
    {
        Assert.Equal("Project", profile.GetProperty("commandName").GetString());
        Assert.True(profile.GetProperty("dotnetRunMessages").GetBoolean());
        Assert.False(profile.GetProperty("launchBrowser").GetBoolean());
        var environmentVariables = profile.GetProperty("environmentVariables");
        Assert.Single(environmentVariables.EnumerateObject());
        AssertVariable(environmentVariables, "ASPNETCORE_ENVIRONMENT", "Development");
    }

    private static void AssertAspireProfile(JsonElement profile, string scheme)
    {
        Assert.Equal("Project", profile.GetProperty("commandName").GetString());
        Assert.True(profile.GetProperty("dotnetRunMessages").GetBoolean());
        Assert.True(profile.GetProperty("launchBrowser").GetBoolean());

        var environmentVariables = profile.GetProperty("environmentVariables");
        AssertVariable(environmentVariables, "ASPNETCORE_ENVIRONMENT", "Development");
        AssertVariable(environmentVariables, "DOTNET_ENVIRONMENT", "Development");
        var otlpPort = AssertSingleUrl(
            environmentVariables.GetProperty("ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL").GetString(), scheme);
        var resourcePort = AssertSingleUrl(
            environmentVariables.GetProperty("ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL").GetString(), scheme);
        Assert.NotEqual(otlpPort, resourcePort);
    }

    private static (int HttpsPort, int HttpPort) AssertHttpsThenHttpUrls(string? applicationUrl)
    {
        Assert.NotNull(applicationUrl);
        var urls = applicationUrl.Split(';');
        Assert.Equal(2, urls.Length);
        return (AssertSingleUrl(urls[0], "https"), AssertSingleUrl(urls[1], "http"));
    }

    private static int AssertSingleUrl(string? url, string expectedScheme)
    {
        Assert.NotNull(url);
        Assert.DoesNotContain(';', url);
        Assert.True(Uri.TryCreate(url, UriKind.Absolute, out var uri), $"Expected an absolute URL but found '{url}'.");
        Assert.Equal(expectedScheme, uri.Scheme);
        Assert.Equal("localhost", uri.Host);
        // Uri reports the scheme default when no port is present; require an explicit generated port.
        Assert.Matches(@":\d+/?$", url);
        Assert.InRange(uri.Port, 1024, 65535);
        return uri.Port;
    }

    private static void AssertVariable(JsonElement environmentVariables, string name, string expectedValue)
    {
        Assert.True(
            environmentVariables.TryGetProperty(name, out var value),
            $"Expected environment variable '{name}' in launch profile.");
        Assert.Equal(expectedValue, value.GetString());
    }

    private static void AssertNoTemporalOrOtelVariables(string content)
    {
        Assert.DoesNotContain("TEMPORAL_", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OTEL_", content, StringComparison.OrdinalIgnoreCase);
    }
}
