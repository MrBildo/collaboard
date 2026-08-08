using System.Net;
using Collaboard.Api.Tests.Infrastructure;
using Shouldly;

namespace Collaboard.Api.Tests;

public class MethodNotAllowedTests(CollaboardApiFactory factory) : IClassFixture<CollaboardApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    // The card description route (/cards/{id}) allows GET, PATCH, DELETE — a PUT is a method
    // mismatch, which is exactly the friction the KindKatch integrator hit: a bare 405 that read
    // like a permissions failure. No card need exist: routing produces the 405 before the endpoint
    // (or its auth filter) runs.
    private static readonly string _methodRestrictedRoute = $"/api/v1/cards/{Guid.NewGuid()}";

    [Fact]
    public async Task Put_To_methodRestrictedRoute_Returns405WithReadableBodyNamingAllowedMethods()
    {
        // Act
        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Put, _methodRestrictedRoute));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("Method not allowed. Supported:");
        body.ShouldContain("GET");
        body.ShouldContain("PATCH");
        body.ShouldContain("DELETE");
    }

    [Fact]
    public async Task MethodNotAllowedResponse_StillEmitsAllowHeader()
    {
        // Act
        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Put, _methodRestrictedRoute));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        response.Content.Headers.Allow.ShouldContain("GET");
        response.Content.Headers.Allow.ShouldContain("PATCH");
        response.Content.Headers.Allow.ShouldContain("DELETE");
    }

    [Fact]
    public async Task NonMethodNotAllowedError_IsNotGivenTheMethodNotAllowedBody()
    {
        // Arrange — a GET for a card that does not exist returns 404, exercising a non-405 error
        // path to confirm the middleware acts on 405 alone and leaves other responses untouched.
        TestAuthHelper.SetAdminAuth(_client, factory);

        // Act
        var response = await _client.GetAsync($"/api/v1/cards/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain("Method not allowed");
    }
}
