using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Olga.Nlp.Contracts;

namespace Olga.Nlp.ContractTests;

public sealed class MatchContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web) { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower };
    private readonly WebApplicationFactory<Program> factory;
    private readonly HttpClient client;
    public MatchContractTests(WebApplicationFactory<Program> factory) { this.factory = factory; client = factory.CreateClient(); client.DefaultRequestHeaders.Add("X-Member-Id", "A123"); }

    [Fact]
    public async Task Valid_local_search_returns_versioned_explainable_match()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/matches/search") { Content = JsonContent.Create(new MatchSearchRequest("contract-1", "a-want", "event-001", 7), options: Json) };
        request.Headers.Add("Idempotency-Key", "contract-1");
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<MatchSearchResponse>(Json);
        Assert.NotNull(body);
        Assert.Contains(body.Matches, x => x.MemberId == "B456" && x.ReasonCodes.Count > 0);
        Assert.DoesNotContain(body.Matches, x => x.MemberId == "D111");
        Assert.Equal("fake-embedding-v2", body.ModelVersion);
    }

    [Fact]
    public async Task Match_request_endpoint_honors_idempotency_and_status_lookup()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/match-requests")
        {
            Content = JsonContent.Create(new MatchSearchRequest("contract-2", "a-want", "event-001", 7), options: Json)
        };
        request.Headers.Add("Idempotency-Key", "contract-2");
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<MatchSearchResponse>(Json);

        var status = await client.GetFromJsonAsync<MatchRequestStatusResponse>("/v1/match-requests/contract-2", Json);

        Assert.NotNull(created);
        Assert.Equal("COMPLETED", created.Status);
        Assert.NotNull(status);
        Assert.Equal("COMPLETED", status.Status);
        Assert.Equal(created.Matches.Count, status.Matches.Count);
        Assert.All(status.Matches, x => Assert.True(x.MatchResultId > 0));
    }

    [Fact]
    public async Task OpenApi_server_resolves_against_the_https_browser_origin()
    {
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var server = Assert.Single(document.RootElement.GetProperty("servers").EnumerateArray());
        Assert.Equal("/", server.GetProperty("url").GetString());
    }

    [Fact]
    public async Task OpenApi_describes_service_token_on_protected_operations_only()
    {
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        var scheme = document.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty("serviceToken");
        Assert.Equal("apiKey", scheme.GetProperty("type").GetString());
        Assert.Equal("header", scheme.GetProperty("in").GetString());
        Assert.Equal("X-Service-Token", scheme.GetProperty("name").GetString());

        var protectedOperation = document.RootElement.GetProperty("paths").GetProperty("/v1/normalize").GetProperty("post");
        var requirement = Assert.Single(protectedOperation.GetProperty("security").EnumerateArray());
        Assert.True(requirement.TryGetProperty("serviceToken", out _));

        var anonymousOperation = document.RootElement.GetProperty("paths").GetProperty("/ready").GetProperty("get");
        Assert.False(anonymousOperation.TryGetProperty("security", out _));
    }

    [Fact]
    public async Task Service_token_rejects_missing_and_invalid_credentials_but_accepts_valid_credential()
    {
        var token = Guid.NewGuid().ToString("N");
        using var protectedFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ServiceAuthorization:Token"] = token })));
        using var protectedClient = protectedFactory.CreateClient();

        using var missing = await protectedClient.PostAsJsonAsync("/v1/normalize", new NormalizeRequest("hello", null));
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, missing.StatusCode);

        protectedClient.DefaultRequestHeaders.Add("X-Service-Token", "invalid");
        using var invalid = await protectedClient.PostAsJsonAsync("/v1/normalize", new NormalizeRequest("hello", null));
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, invalid.StatusCode);

        protectedClient.DefaultRequestHeaders.Remove("X-Service-Token");
        protectedClient.DefaultRequestHeaders.Add("X-Service-Token", token);
        using var valid = await protectedClient.PostAsJsonAsync("/v1/normalize", new NormalizeRequest("hello", null));
        valid.EnsureSuccessStatusCode();
    }
}
