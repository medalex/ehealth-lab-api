using EHealth.Lab.Models;

namespace EHealth.Lab.Data;

public static class Seeder
{
    // pat1 = 00000000-0000-0000-0000-000000000001 (Anna Koval)
    private static readonly Guid Pat1 = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public static void Seed(AppDbContext db)
    {
        if (db.LabResults.Any()) return;

        var r1 = new LabResult
        {
            Id = Guid.Parse("00000000-0000-0000-0001-000000000001"),
            PatientId = Pat1,
            LoincCode = "33914-3",
            Metric = "eGFR",
            Formula = EGfrFormula.CkdEpi,
            Value = 45,
            Unit = "mL/min/1.73m²",
            MeasuredBy = "DiagLab1",
            MeasuredAt = DateTime.UtcNow.AddDays(-5)
        };
        r1.LeafHash = LabResult.ComputeLeafHash(r1.Id, r1.PatientId, r1.LoincCode, r1.Value, r1.Unit);

        // Semantic conflict example: same patient, Cockcroft-Gault formula, different unit
        var r2 = new LabResult
        {
            Id = Guid.Parse("00000000-0000-0000-0001-000000000002"),
            PatientId = Pat1,
            LoincCode = "2164-2",
            Metric = "Creatinine Clearance",
            Formula = EGfrFormula.CockcroftGault,
            Value = 52,
            Unit = "mL/min",
            MeasuredBy = "DiagLab1",
            MeasuredAt = DateTime.UtcNow.AddDays(-5)
        };
        r2.LeafHash = LabResult.ComputeLeafHash(r2.Id, r2.PatientId, r2.LoincCode, r2.Value, r2.Unit);

        db.LabResults.AddRange(r1, r2);
        db.SaveChanges();
    }
}
