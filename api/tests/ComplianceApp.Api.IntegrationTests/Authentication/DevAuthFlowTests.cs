using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace ComplianceApp.Api.IntegrationTests.Authentication;

/// <summary>
/// End-to-end check of the dev auth flow:
///   1. <c>POST /api/auth/dev-token</c> issues a JWT
///   2. <c>GET /api/me</c> with that JWT returns the same user + org
/// Uses <see cref="WebApplicationFactory{TEntryPoint}"/> against the Development
/// environment so DevAuth is enabled with the values from appsettings.Development.json.
/// </summary>
public class DevAuthFlowTests : IClassFixture<DevAuthFlowTests.Factory>
{
    private readonly HttpClient _client;

    public DevAuthFlowTests(Factory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetMe_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync("/api/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMe_AfterIssuingDevToken_ReturnsSameUserAndOrganisation()
    {
        var userId = Guid.NewGuid();
        var organisationId = Guid.NewGuid();

        var tokenResponse = await _client.PostAsJsonAsync(
            "/api/auth/dev-token",
            new { userId, organisationId });

        tokenResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = await tokenResponse.Content.ReadFromJsonAsync<DevTokenPayload>();
        token.Should().NotBeNull();
        token!.AccessToken.Should().NotBeNullOrEmpty();
        token.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);

        var authedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        authedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var meResponse = await _client.SendAsync(authedRequest);

        meResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var me = await meResponse.Content.ReadFromJsonAsync<MePayload>();
        me.Should().NotBeNull();
        me!.IsAuthenticated.Should().BeTrue();
        me.UserId.Should().Be(userId);
        me.OrganisationId.Should().Be(organisationId);
    }

    [Fact]
    public async Task DevToken_IssuesDifferentTokensForDifferentUsers()
    {
        var first = await IssueToken(Guid.NewGuid(), Guid.NewGuid());
        var second = await IssueToken(Guid.NewGuid(), Guid.NewGuid());

        first!.AccessToken.Should().NotBe(second!.AccessToken);
    }

    private async Task<DevTokenPayload?> IssueToken(Guid userId, Guid organisationId)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/auth/dev-token",
            new { userId, organisationId });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DevTokenPayload>();
    }

    private record DevTokenPayload(string AccessToken, DateTimeOffset ExpiresAt);

    private record MePayload(Guid? UserId, Guid? OrganisationId, bool IsAuthenticated);

    public class Factory : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Development);
            return base.CreateHost(builder);
        }
    }
}
