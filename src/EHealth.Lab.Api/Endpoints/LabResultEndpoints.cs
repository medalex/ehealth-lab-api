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
            var turtle = $"""
                @prefix rx: <https://mfssia.io/ontology/prescription#> .
                @prefix xsd: <http://www.w3.org/2001/XMLSchema#> .
                @prefix loinc: <https://loinc.org/> .

                <urn:lab:result:{result.Id}> a rx:LabResult ;
                    rx:loincCode loinc:{result.LoincCode} ;
                    rx:conditionType "{result.Metric}" ;
                    rx:formula "{result.Formula}" ;
                    rx:clinicalValue "{result.Value}"^^xsd:decimal ;
                    rx:unit "{result.Unit}" ;
                    rx:measuredBy "{result.MeasuredBy}" ;
                    rx:measuredAt "{result.MeasuredAt:O}"^^xsd:dateTime ;
                    rx:leafHash "{result.LeafHash}" .
                """;

            var response = await client.PostAsync(
                $"{mfssiaUrl}/rdf",
                new StringContent(turtle, System.Text.Encoding.UTF8, "text/turtle"));

            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadFromJsonAsync<DkgResponse>();
            return json?.Data?.UAL ?? json?.UAL;
        }
        catch
        {
            return null; // DKG publish failure is non-fatal for local storage
        }
    }

    private record DkgData(string? UAL);
    private record DkgResponse(string? UAL, DkgData? Data);
}
