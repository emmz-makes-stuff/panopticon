using Amazon.S3;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Panopticon.Server.Documents;

[assembly: AssemblyFixture(typeof(Panopticon.Server.Tests.Infrastructure.S3Fixture))]

namespace Panopticon.Server.Tests.Infrastructure;

/// <summary>
/// One SeaweedFS S3 gateway for the whole test assembly (design D13; MinIO no longer publishes images).
/// Uses the same identities as local compose. Each test asks for its own bucket.
/// </summary>
public sealed class S3Fixture : IAsyncLifetime
{
    private const int S3Port = 8333;
    public const string AccessKey = "panopticon-local";
    public const string SecretKey = "panopticon-local-secret";

    private readonly IContainer _container = new ContainerBuilder("chrislusf/seaweedfs:4.50")
        // Every test makes its own bucket, and each bucket is a SeaweedFS collection that claims volumes.
        // The default `-volume.max=8` runs out after a few buckets ("internal error" on PutObject), so
        // size the limit from free disk and keep each volume small.
        .WithCommand(
            "server", "-dir=/data", "-volume.max=0", "-master.volumeSizeLimitMB=64",
            "-s3", $"-s3.port={S3Port}", "-s3.config=/etc/seaweedfs/s3.json")
        .WithResourceMapping(
            new FileInfo(Path.Combine(AppContext.BaseDirectory, "Infrastructure", "seaweedfs-s3.json")),
            new FileInfo("/etc/seaweedfs/s3.json"))
        .WithPortBinding(S3Port, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilHttpRequestIsSucceeded(request => request.ForPort(S3Port).ForPath("/healthz")))
        .Build();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    public string ServiceUrl => $"http://{_container.Hostname}:{_container.GetMappedPublicPort(S3Port)}";

    /// <summary>Options for a fresh, empty bucket that exists before the test runs.</summary>
    public async Task<S3Options> CreateBucketAsync(CancellationToken cancellationToken = default)
    {
        var options = new S3Options
        {
            ServiceUrl = ServiceUrl,
            AccessKey = AccessKey,
            SecretKey = SecretKey,
            Bucket = "test-" + Guid.NewGuid().ToString("N"),
            ForcePathStyle = true,
        };

        using var client = options.CreateClient();
        await client.PutBucketAsync(options.Bucket, cancellationToken);
        return options;
    }
}
