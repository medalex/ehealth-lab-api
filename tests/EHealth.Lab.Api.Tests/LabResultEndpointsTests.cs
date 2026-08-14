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

    // Nothing is seeded — lab results are entered live during the demo — so every test
    // that needs a record creates it first.
    private async Task<LabResult> CreateResult(
        Guid patientId, string loincCode = "33914-3", string metric = "eGFR", decimal value = 60)
    {
        var response = await _client.PostAsJsonAsync("/api/results", new LabResult
        {
            PatientId = patientId,
            LoincCode = loincCode,
            Metric = metric,
            Formula = EGfrFormula.CkdEpi,
            Value = value,
            Unit = "mL/min/1.73m²",
            MeasuredBy = "TestLab",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LabResult>())!;
    }

    [Fact]
    public async Task GetAllResults_ReturnsCreatedRecords()
    {
        await CreateResult(PatientId);
        await CreateResult(Guid.NewGuid());

        var response = await _client.GetAsync("/api/results");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<List<LabResult>>();
        Assert.NotNull(results);
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task GetResultsByPatient_ReturnsOnlyThatPatientsRecords()
    {
        await CreateResult(PatientId);
        await CreateResult(PatientId, loincCode: "2164-2", metric: "Creatinine Clearance");
        await CreateResult(Guid.NewGuid());

        var response = await _client.GetAsync($"/api/results/patient/{PatientId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<List<LabResult>>();
        Assert.NotNull(results);
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(PatientId, r.PatientId));
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
        var created = await CreateResult(PatientId);

        var response = await _client.GetAsync($"/api/results/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<LabResult>();
        Assert.Equal(created.Id, result!.Id);
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
        var created = await CreateResult(PatientId);

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.NotNull(created.LeafHash);
        Assert.Equal(64, created.LeafHash.Length); // SHA-256 hex = 64 chars
    }

    [Fact]
    public async Task PostResult_LeafHash_DoesNotContainPatientId()
    {
        // The leafHash must be an opaque hex string with no patient identity embedded
        var created = await CreateResult(
            PatientId, loincCode: "2164-2", metric: "Creatinine Clearance", value: 55);

        Assert.DoesNotContain(PatientId.ToString(), created.LeafHash!);
    }

    [Fact]
    public async Task DeleteResult_KnownId_ReturnsNoContent()
    {
        var created = await CreateResult(PatientId);

        var response = await _client.DeleteAsync($"/api/results/{created.Id}");

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
