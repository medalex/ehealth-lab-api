using System.Net;
using System.Net.Http.Json;
using EHealth.Lab.Models;
using EHealth.Lab.Api.Tests.Helpers;

namespace EHealth.Lab.Api.Tests;

public class LabResultEndpointsTests : IDisposable
{
    private readonly TestFactory _factory = new();
    private readonly HttpClient _client;
    private static readonly Guid PatientId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public LabResultEndpointsTests()
    {
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task GetAllResults_ReturnsSeededRecords()
    {
        var response = await _client.GetAsync("/api/results");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<List<LabResult>>();
        Assert.NotNull(results);
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task GetResultsByPatient_ReturnsSeededRecords()
    {
        var response = await _client.GetAsync($"/api/results/patient/{PatientId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<List<LabResult>>();
        Assert.NotNull(results);
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task GetResultsByPatient_UnknownPatient_ReturnsEmptyList()
    {
        var response = await _client.GetAsync($"/api/results/patient/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<List<LabResult>>();
        Assert.Empty(results!);
    }

    [Fact]
    public async Task GetResultById_ReturnsRecord()
    {
        var seeded = await _client.GetFromJsonAsync<List<LabResult>>($"/api/results/patient/{PatientId}");
        var id = seeded![0].Id;

        var response = await _client.GetAsync($"/api/results/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<LabResult>();
        Assert.Equal(id, result!.Id);
    }

    [Fact]
    public async Task GetResultById_UnknownId_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/api/results/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostResult_CreatesRecord_WithLeafHash()
    {
        var newResult = new LabResult
        {
            PatientId = PatientId,
            LoincCode = "33914-3",
            Metric = "eGFR",
            Formula = EGfrFormula.CkdEpi,
            Value = 60,
            Unit = "mL/min/1.73m²",
            MeasuredBy = "TestLab"
        };

        var response = await _client.PostAsJsonAsync("/api/results", newResult);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<LabResult>();
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.NotNull(created.LeafHash);
        Assert.Equal(64, created.LeafHash.Length); // SHA-256 hex = 64 chars
    }

    [Fact]
    public async Task PostResult_LeafHash_DoesNotContainPatientId()
    {
        // The leafHash must be an opaque hex string with no patient identity embedded
        var newResult = new LabResult
        {
            PatientId = PatientId,
            LoincCode = "2164-2",
            Metric = "Creatinine Clearance",
            Formula = EGfrFormula.CockcroftGault,
            Value = 55,
            Unit = "mL/min",
            MeasuredBy = "TestLab"
        };

        var response = await _client.PostAsJsonAsync("/api/results", newResult);
        var created = await response.Content.ReadFromJsonAsync<LabResult>();

        Assert.DoesNotContain(PatientId.ToString(), created!.LeafHash!);
    }

    [Fact]
    public async Task DeleteResult_KnownId_ReturnsNoContent()
    {
        var results = await _client.GetFromJsonAsync<List<LabResult>>($"/api/results/patient/{PatientId}");
        var id = results![0].Id;

        var response = await _client.DeleteAsync($"/api/results/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteResult_UnknownId_ReturnsNotFound()
    {
        var response = await _client.DeleteAsync($"/api/results/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public void Dispose() => _factory.Dispose();
}
