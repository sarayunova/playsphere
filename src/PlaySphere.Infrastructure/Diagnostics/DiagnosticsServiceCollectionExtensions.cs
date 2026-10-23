using Microsoft.Extensions.DependencyInjection;
using PlaySphere.Application.Diagnostics;

namespace PlaySphere.Infrastructure.Diagnostics;

public static class DiagnosticsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the local diagnostic packer. The support address defaults to
    /// <see cref="DiagnosticReportDefaults.SupportEmail"/>. Pass <paramref name="options"/> to
    /// point a build at a different mailbox. The packer never opens the network.
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
