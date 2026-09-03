using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using PlaySphere.AI;
using PlaySphere.Application.AI;
using PlaySphere.Domain.AI;
using Xunit;

namespace PlaySphere.UnitTests.AI;

public class AiServiceCollectionRegistrationTests
{
    [Fact]
    public void AddPlaySphereAi_registers_boundary_and_credential_store()
    {
        var services = new ServiceCollection();
        services.AddPlaySphereAi(new AiServiceOptions());
        var provider = services.BuildServiceProvider();

        // The boundary resolves to the AI module implementation.
        var service = provider.GetRequiredService<IAiMediaService>();
        Assert.IsType<OpenAiMediaService>(service);

        // The credential store is the Windows secure store.
        var store = provider.GetRequiredService<IAiCredentialStore>();
        Assert.IsType<WindowsCredentialStore>(store);
    }

    [Fact]
    public void AddPlaySphereAi_registers_the_transcription_runner()
    {
        var services = new ServiceCollection();
        services.AddPlaySphereAi(new AiServiceOptions());

        // Resolving the full runner graph needs the sibling modules' services (audio extraction,
        // catalog), which the real App registers before AddPlaySphereAi. Here assert the
        // registration descriptor exists and maps to the AI implementation.
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ITranscribeMediaRunner));
        Assert.NotNull(descriptor);
        Assert.Equal(typeof(TranscribeMediaRunner), descriptor?.ImplementationType);
    }

    [Fact]
    public void AddPlaySphereAi_defaults_options_when_none_provided()
    {
        var services = new ServiceCollection();
        services.AddPlaySphereAi();
        var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<AiServiceOptions>();
        Assert.NotNull(options);
    }
}
