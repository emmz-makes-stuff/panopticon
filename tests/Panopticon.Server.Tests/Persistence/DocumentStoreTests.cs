using Amazon.S3.Model;
using Panopticon.Server.Documents;
using Panopticon.Server.Tests.Infrastructure;

namespace Panopticon.Server.Tests.Persistence;

public class DocumentStoreTests(S3Fixture s3)
{
    [Fact]
    public async Task Content_round_trips_under_its_sha256()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = await s3.CreateBucketAsync(ct);
        using var client = options.CreateClient();
        var store = new S3DocumentStore(client, options.Bucket!);

        var sha = await store.PutAsync("## 1. Setup\n- [x] 1.1 done\n", ct);

        Assert.Equal(S3DocumentStore.Hash("## 1. Setup\n- [x] 1.1 done\n"), sha);
        Assert.Matches("^[0-9a-f]{64}$", sha);
        Assert.Equal("## 1. Setup\n- [x] 1.1 done\n", await store.GetAsync(sha, ct));
    }

    [Fact]
    public async Task Unchanged_document_is_stored_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = await s3.CreateBucketAsync(ct);
        using var client = options.CreateClient();
        var store = new S3DocumentStore(client, options.Bucket!);

        var first = await store.PutAsync("same tasks", ct);
        var second = await store.PutAsync("same tasks", ct);

        Assert.Equal(first, second);
        var listing = await client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = options.Bucket }, ct);
        var only = Assert.Single(listing.S3Objects);
        Assert.Equal(S3DocumentStore.KeyFor(first), only.Key);
    }

    [Fact]
    public async Task Missing_document_is_null()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = await s3.CreateBucketAsync(ct);
        using var client = options.CreateClient();
        var store = new S3DocumentStore(client, options.Bucket!);

        Assert.Null(await store.GetAsync(new string('0', 64), ct));
    }

    [Fact]
    public void Missing_settings_are_named()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new S3Options().Validate());

        Assert.Contains("Panopticon:S3:Bucket", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Panopticon:S3:AccessKey", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Panopticon:S3:SecretKey", ex.Message, StringComparison.Ordinal);
    }
}
