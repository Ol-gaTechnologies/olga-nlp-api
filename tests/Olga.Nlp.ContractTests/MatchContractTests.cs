using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Olga.Nlp.Contracts;

namespace Olga.Nlp.ContractTests;

public sealed class MatchContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web) { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower };
    private readonly HttpClient client;
    public MatchContractTests(WebApplicationFactory<Program> factory) { client = factory.CreateClient(); client.DefaultRequestHeaders.Add("X-Member-Id", "A123"); }

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
}
