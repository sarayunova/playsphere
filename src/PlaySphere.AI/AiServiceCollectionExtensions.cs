using System;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using PlaySphere.Application.AI;
using PlaySphere.Domain.AI;

namespace PlaySphere.AI;

public static class AiServiceCollectionExtensions
{
    /// <summary>
    /// Registers the OpenAI service boundary (<see cref="IAiMediaService"/>) and the Windows secure
    /// credential store (<see cref="IAiCredentialStore"/>), along with the optional
    /// <see cref="AiServiceOptions"/>. All OpenAI access is confined to <c>PlaySphere.AI</c> behind
    /// this boundary (PICKLE.md §39). AI is strictly opt-in — no provider call happens unless a
    /// feature calls the service after explicit user action (§40).
    /// </summary>
    public static IServiceCollection AddPlaySphereAi(this IServiceCollection services, AiServiceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(options ?? new AiServiceOptions());

        services.AddSingleton<IAiCredentialStore, WindowsCredentialStore>();
        services.AddSingleton<HttpClient>(_ => new HttpClient());
        services.AddSingleton<IAiMediaService, OpenAiMediaService>();
        services.AddSingleton<ITranscribeMediaRunner, TranscribeMediaRunner>();
        services.AddSingleton<IGenerateChaptersRunner, GenerateChaptersRunner>();
        services.AddSingleton<ISummarizeMediaRunner, SummarizeMediaRunner>();
        services.AddSingleton<IAskMediaRunner, AskMediaRunner>();
        services.AddSingleton<ITranslateMediaRunner, TranslateMediaRunner>();

        return services;
    }
}
