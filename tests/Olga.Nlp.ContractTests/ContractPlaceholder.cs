using System.Text.Json;
using System.Text.Json.Serialization;
using Olga.Nlp.Contracts;

namespace Olga.Nlp.ContractTests;

public sealed class ErrorContractTests
{
    [Fact]
    public void Error_envelope_uses_stable_snake_case_contract()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        var json = JsonSerializer.Serialize(new ApiError("RESOURCE_VERSION_CONFLICT", "The resource changed.", "correlation-1"), options);

        Assert.Contains("\"code\":\"RESOURCE_VERSION_CONFLICT\"", json);
        Assert.Contains("\"correlation_id\":\"correlation-1\"", json);
        Assert.DoesNotContain("field_errors", json);
    }
}
