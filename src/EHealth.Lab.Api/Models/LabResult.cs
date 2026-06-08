namespace EHealth.Lab.Models;

/// <summary>
/// Calculation formula used for the measurement.
/// Demonstrates the numerical semantic conflict from the paper:
/// hospital policy uses CKD-EPI; labs may report via Cockcroft-Gault.
/// The Theory T numerical bridge in the DKG reconciles both.
/// </summary>
public enum EGfrFormula { CkdEpi, CockcroftGault, Other }

public class LabResult
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }

    // LOINC classification
    public string LoincCode { get; set; } = default!;   // e.g. "33914-3" (CKD-EPI), "2164-2" (Cockcroft-Gault)
    public string Metric { get; set; } = default!;      // human-readable: "eGFR"
    public EGfrFormula Formula { get; set; } = EGfrFormula.Other;

    public decimal Value { get; set; }
    public string Unit { get; set; } = default!;         // e.g. "mL/min/1.73m²" or "mL/min"
    public string MeasuredBy { get; set; } = default!;   // lab name
    public DateTime MeasuredAt { get; set; } = DateTime.UtcNow;

    // SHA-256(Id || PatientId || LoincCode || Value || Unit)
    // Merkle leaf commitment published to DKG instead of raw patient attributes.
    // Used in root_M so the ZKP circuit can attest record membership without
    // revealing patient identity.
    public string? LeafHash { get; set; }

    // DKG UAL set after publishing the anonymised record entry
    public string? DkgUal { get; set; }

    public static string ComputeLeafHash(Guid id, Guid patientId, string loincCode, decimal value, string unit)
    {
        var input = $"{id}|{patientId}|{loincCode}|{value}|{unit}";
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
