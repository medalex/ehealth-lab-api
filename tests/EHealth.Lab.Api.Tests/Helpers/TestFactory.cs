using System.Net;
using System.Text;
using EHealth.Lab.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace EHealth.Lab.Api.Tests.Helpers;

public class TestFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public TestFactory()
    {
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var toRemove = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>))
                .ToList();
            foreach (var d in toRemove)
                services.Remove(d);

            // Reuse the same open connection so :memory: DB persists across requests
            services.AddDbContext<AppDbContext>(opt =>
                opt.UseSqlite(_connection));

            // POST /api/results calls patient-api to check consent and rejects with 403 when
            // that call fails, so the upstream has to answer for the endpoint to be testable.
            services.ConfigureAll<HttpClientFactoryOptions>(o =>
                o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = new UpstreamStub()));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}

// Stands in for the services the lab API calls out to. Consent is granted; everything else
// (the mfssia DKG publish) is left unavailable on purpose — PublishToDkg swallows that and
// leaves DkgUal null, which is the documented behaviour when mfssia is down.
internal sealed class UpstreamStub : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = request.RequestUri!.AbsolutePath == "/api/consents/check"
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"granted\":true}", Encoding.UTF8, "application/json"),
            }
            : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        return Task.FromResult(response);
    }
}
