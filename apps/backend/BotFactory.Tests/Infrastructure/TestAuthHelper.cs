using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BotFactory.Tests.Infrastructure;

public static class TestAuthHelper
{
    /// <summary>
    /// Creates an authenticated HttpClient by calling POST /auth/dev/token to create a real session.
    /// The session cookie is preserved across requests via HandleCookies.
    /// </summary>
    public static Task<HttpClient> CreateAuthenticatedClientAsync(
        BotFactoryWebApplicationFactory factory,
        string email = "superadmin@example.com",
        int organizationApplicationId = 1) =>
        CreateAuthenticatedClientAsync((WebApplicationFactory<Program>)factory, email, organizationApplicationId);

    public static async Task<HttpClient> CreateAuthenticatedClientAsync(
        WebApplicationFactory<Program> factory,
        string email = "superadmin@example.com",
        int organizationApplicationId = 1)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
        });

        var response = await client.PostAsJsonAsync("/api/auth/dev/token", new
        {
            email,
            organizationApplicationId,
        });
        response.EnsureSuccessStatusCode();

        return client;
    }
}
