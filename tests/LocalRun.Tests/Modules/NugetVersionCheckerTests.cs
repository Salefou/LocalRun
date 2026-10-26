namespace LocalRun.Tests.Modules;

using LocalRun.Core.Modules;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;

public sealed class NugetVersionCheckerTests : IDisposable
{
    // The project folder's NuGet.Config clears every source, like samples/NuGet.config minus its feeds
    private readonly string root = Directory.CreateTempSubdirectory("localrun-root-").FullName;
    private readonly string feed = Directory.CreateTempSubdirectory("localrun-feed-").FullName;

    public NugetVersionCheckerTests()
    {
        File.WriteAllText(Path.Combine(root, "NuGet.Config"),
            "<configuration><packageSources><clear /></packageSources></configuration>");
    }

    public void Dispose()
    {
        Directory.Delete(root, recursive: true);
        Directory.Delete(feed, recursive: true);
    }

    private void Packed(string packageId, string version)
    {
        using var nupkg = ZipFile.Open(Path.Combine(feed, $"{packageId}.{version}.nupkg"), ZipArchiveMode.Create);
        using var writer = new StreamWriter(nupkg.CreateEntry($"{packageId}.nuspec").Open());
        writer.Write($"""
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata><id>{packageId}</id><version>{version}</version><authors>a</authors><description>d</description></metadata>
            </package>
            """);
    }

    [Fact]
    public async Task TheLocalFeed_IsQueried_EvenWhenNoNuGetConfigListsIt()
    {
        Packed("Lib", "1.5.0-alpha.9");
        Packed("Lib", "1.5.0-alpha.10");

        var latest = await new NugetVersionChecker().GetLatestVersionAcrossSourcesAsync("Lib", root, feed);

        Assert.Equal("1.5.0-alpha.10", latest?.ToNormalizedString());
    }

    [Fact]
    public async Task ASourceCredentialFromNuGetConfig_IsSent()
    {
        using var listener = new HttpListener();
        string baseUrl = $"http://localhost:{FreePort()}/";
        listener.Prefixes.Add(baseUrl);
        listener.Start();
        _ = ServeLibRequiringBasicAuthAsync(listener, baseUrl, "user:token");
        File.WriteAllText(Path.Combine(root, "NuGet.Config"), $"""
            <configuration>
              <packageSources><clear /><add key="private" value="{baseUrl}v3/index.json" allowInsecureConnections="true" /></packageSources>
              <packageSourceCredentials><private><add key="Username" value="user" /><add key="ClearTextPassword" value="token" /></private></packageSourceCredentials>
            </configuration>
            """);

        var latest = await new NugetVersionChecker().GetLatestVersionAcrossSourcesAsync("Lib", root);

        Assert.Equal("2.3.0", latest?.ToNormalizedString());
    }

    private static int FreePort()
    {
        var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        int port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();
        return port;
    }

    /// <summary>A minimal v3 feed (service index and registration of Lib 2.3.0), answering 401 without the credential, as Artifactory does.</summary>
    private static async Task ServeLibRequiringBasicAuthAsync(HttpListener listener, string baseUrl, string credential)
    {
        string expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(credential));
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync(); }
            catch (Exception) { return; } // the listener was disposed
            using var response = context.Response;
            if (context.Request.Headers["Authorization"] != expected)
            {
                response.StatusCode = 401;
                response.AddHeader("WWW-Authenticate", "Basic realm=\"feed\"");
                continue;
            }
            string path = context.Request.Url!.AbsolutePath;
            string? json = path switch
            {
                "/v3/index.json" => $$"""{ "version": "3.0.0", "resources": [ { "@id": "{{baseUrl}}reg/", "@type": "RegistrationsBaseUrl/3.6.0" } ] }""",
                "/reg/lib/index.json" => $$"""
                    { "count": 1, "items": [ { "@id": "{{baseUrl}}reg/lib/index.json#page", "lower": "2.3.0", "upper": "2.3.0", "count": 1,
                      "items": [ { "@id": "{{baseUrl}}reg/lib/2.3.0.json", "catalogEntry": { "@id": "{{baseUrl}}catalog/lib.2.3.0.json", "id": "Lib", "version": "2.3.0", "listed": true } } ] } ] }
                    """,
                _ => null,
            };
            response.StatusCode = json == null ? 404 : 200;
            if (json != null)
            {
                response.ContentType = "application/json";
                await response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(json));
            }
        }
    }

    [Fact]
    public async Task WithoutTheLocalFeed_OnlyTheConfiguredSourcesCount()
    {
        Packed("Lib", "1.5.0-alpha.9");

        Assert.Null(await new NugetVersionChecker().GetLatestVersionAcrossSourcesAsync("Lib", root));
    }
}
