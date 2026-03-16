using System.Net.Http.Json;
using System.Text.Json;

namespace Collaboard.Api.Tests.Infrastructure;

public static class TestDataHelper
{
    public static async Task<Guid> GetLaneIdByIndexAsync(HttpClient client, Guid boardId, int index)
    {
        var response = await client.GetAsync($"/api/v1/boards/{boardId}/board");
        response.EnsureSuccessStatusCode();
        var board = await response.Content.ReadFromJsonAsync<JsonElement>();
        return board.GetProperty("lanes")[index].GetProperty("id").GetGuid();
    }

    public static async Task<Guid> GetFirstLaneIdAsync(HttpClient client, Guid boardId)
        => await GetLaneIdByIndexAsync(client, boardId, 0);

    public static async Task<Guid> GetSizeIdByNameAsync(HttpClient client, Guid boardId, string sizeName)
    {
        var response = await client.GetAsync($"/api/v1/boards/{boardId}/sizes");
        response.EnsureSuccessStatusCode();
        var sizes = await response.Content.ReadFromJsonAsync<JsonElement[]>();
        var size = sizes!.First(s => s.GetProperty("name").GetString() == sizeName);
        return size.GetProperty("id").GetGuid();
    }
}
