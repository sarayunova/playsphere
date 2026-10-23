using Microsoft.Extensions.DependencyInjection;
using PlaySphere.Application.Diagnostics;

namespace PlaySphere.Infrastructure.Diagnostics;

public static class DiagnosticsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the local diagnostic packer. Pass <paramref name="options"/> to point a tester
    /// build at a real support address (<see cref="DiagnosticReportDefaults.SupportEmail"/> is a
    /// placeholder). The packer never opens the network.
    /// </summary>
    public static IServiceCollection AddPlaySphereDiagnostics(
        this IServiceCollection services,
        DiagnosticReportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(options ?? new DiagnosticReportOptions());
        services.AddSingleton<IDiagnosticReportService, ZipDiagnosticReportService>();
        return services;
    }
}
