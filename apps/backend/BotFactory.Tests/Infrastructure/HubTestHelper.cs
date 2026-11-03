using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;

namespace BotFactory.Tests.Infrastructure;

public static class HubTestHelper
{
    /// <summary>
    /// Builds a <see cref="HubConnection"/> against the in-process TestServer,
    /// authenticating with the given access token (session GUID or public JWT).
    /// </summary>
    public static HubConnection CreateHubConnection(
        WebApplicationFactory<Program> factory,
        string accessToken)
    {
        var server = factory.Server;
        return new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress!, "/hubs/conversation"), options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult(accessToken)!;
            })
            .WithAutomaticReconnect()
            .Build();
    }

    public static async Task<HubConnection> StartAsync(
        WebApplicationFactory<Program> factory,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        var connection = CreateHubConnection(factory, accessToken);
        await connection.StartAsync(cancellationToken);
        return connection;
    }
}
