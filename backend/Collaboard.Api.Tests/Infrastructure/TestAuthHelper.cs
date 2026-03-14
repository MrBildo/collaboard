using System.Net.Http.Json;
using Collaboard.Api.Models;

namespace Collaboard.Api.Tests.Infrastructure;

public static class TestAuthHelper
{
    public static void SetAuth(HttpClient client, string userKey)
    {
        client.DefaultRequestHeaders.Remove("X-User-Key");
        client.DefaultRequestHeaders.Add("X-User-Key", userKey);
    }

    public static void SetAdminAuth(HttpClient client, CollaboardApiFactory factory)
    {
        SetAuth(client, factory.AdminAuthKey);
    }

    public static async Task<BoardUser> CreateUserAsync(
        HttpClient client, CollaboardApiFactory factory, string name, UserRole role)
    {
        SetAdminAuth(client, factory);
        var response = await client.PostAsJsonAsync("/api/v1/users", new { name, role });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BoardUser>())!;
    }
}
