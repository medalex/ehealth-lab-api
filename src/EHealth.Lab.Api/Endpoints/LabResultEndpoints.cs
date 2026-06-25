using System.Text.Json;
using EHealth.Lab.Data;
using EHealth.Lab.Models;
using Microsoft.EntityFrameworkCore;

namespace EHealth.Lab.Endpoints;

public static class LabResultEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/results").WithTags("Lab Results");

        group.MapGet("/", async (AppDbContext db) =>
            await db.LabResults.OrderByDescending(r => r.MeasuredAt).ToListAsync());

        group.MapGet("/patient/{patientId:guid}", async (Guid patientId, AppDbContext db) =>
            await db.LabResults
                .Where(r => r.PatientId == patientId)
                .OrderByDescending(r => r.MeasuredAt)
                .ToListAsync());

        group.MapGet("/{id:guid}", async (Guid id, AppDbContext db) =>
            await db.LabResults.FindAsync(id) is { } result
                ? Results.Ok(result)
                : Results.NotFound());

        group.MapPost("/", async (LabResult result, AppDbContext db,
            IHttpClientFactory http, IConfiguration config) =>
        {
            // Check patient consent for lab data access
            var orgId = config["LabOrganizationId"] ?? "lab-1";
            if (!await CheckConsent(result.PatientId, orgId, http, config))
                return Results.Json(
                    new { error = $"Patient {result.PatientId} has not granted consent to {orgId}" },
                    statusCode: 403);

            result.Id = Guid.NewGuid();
            result.MeasuredAt = DateTime.UtcNow;
            result.LeafHash = LabResult.ComputeLeafHash(
                result.Id, result.PatientId, result.LoincCode, result.Value, result.Unit);

            db.LabResults.Add(result);
            await db.SaveChangesAsync();

            // Publish anonymised Turtle record to DKG via mfssia-ehealth.
            // PatientId is NOT included — only the leafHash commitment binds
            // this DKG entry to the patient for use in root_M Merkle proofs.
            var ual = await PublishToDkg(result, http, config);
            if (ual is not null)
            {
                result.DkgUal = ual;
                await db.SaveChangesAsync();
            }

            return Results.Created($"/api/results/{result.Id}", result);
        });

        group.MapDelete("/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var result = await db.LabResults.FindAsync(id);
            if (result is null) return Results.NotFound();
            db.LabResults.Remove(result);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    private static async Task<bool> CheckConsent(
        Guid patientId, string organizationId, IHttpClientFactory http, IConfiguration config)
    {
        try
        {
            var patientApiUrl = config["PatientApiUrl"] ?? "http://patient-api:3001";
            var client = http.CreateClient();
            var resp = await client.GetAsync(
                $"{patientApiUrl}/api/consents/check?patientId={patientId}&organizationId={organizationId}");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private static async Task<string?> PublishToDkg(
        LabResult result, IHttpClientFactory http, IConfiguration config)
    {
        try
        {
            var mfssiaUrl = config["MfssiaUrl"] ?? "http://mfssia-ehealth:4000/api";
            var client = http.CreateClient();

            // Patient identity is kept off-chain (paper §3.3, R4 Witness confidentiality).
            // The leafHash commits Id + PatientId + LoincCode + Value + Unit so the ZKP
            // circuit can verify Merkle membership in root_M without exposing the patient.
            // JSON-LD so the lab result is queryable in the DKG graph (raw Turtle is not parsed).
            var response = await client.PostAsJsonAsync($"{mfssiaUrl}/rdf/jsonld", new
            {
                id = $"urn:lab:result:{result.Id}",
                type = "LabResult",
                literals = new Dictionary<string, string>
                {
                    ["loincCode"] = result.LoincCode,
                    ["conditionType"] = result.Metric,
                    ["formula"] = result.Formula.ToString(),
                    ["clinicalValue"] = result.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["unit"] = result.Unit,
                    ["measuredBy"] = result.MeasuredBy,
                    ["leafHash"] = result.LeafHash,
                },
                dateTimes = new Dictionary<string, string>
                {
                    ["measuredAt"] = result.MeasuredAt.ToUniversalTime().ToString("O"),
                },
            });

            if (!response.IsSuccessStatusCode) return null;

            // Use JsonDocument for reliable mfssia response parsing.
            // Format: { "data": { "UAL": "did:dkg:..." } } or { "UAL": "did:dkg:..." }
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("data", out var dataEl) &&
                dataEl.ValueKind == JsonValueKind.Object &&
                dataEl.TryGetProperty("UAL", out var nestedUal))
                return nestedUal.GetString();

            if (root.TryGetProperty("UAL", out var topUal))
                return topUal.GetString();

            return null;
        }
        catch
        {
            return null; // DKG error does not block result persistence
        }
    }
}
