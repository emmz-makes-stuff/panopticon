using System.Net;
using System.Security.Cryptography;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace Panopticon.Server.Documents;

/// <summary>S3-compatible object storage settings (design D7). Any provider: Railway Bucket, AWS, SeaweedFS.</summary>
public sealed class S3Options
{
    public const string Section = "Panopticon:S3";

    public string? ServiceUrl { get; set; }
    public string Region { get; set; } = "us-east-1";
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
    public string? Bucket { get; set; }
    public bool ForcePathStyle { get; set; }

    /// <summary>Throws naming every missing setting, so a misconfigured server fails at startup.</summary>
    public void Validate()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(Bucket))
        {
            missing.Add($"{Section}:Bucket");
        }

        if (string.IsNullOrWhiteSpace(AccessKey))
        {
            missing.Add($"{Section}:AccessKey");
        }

        if (string.IsNullOrWhiteSpace(SecretKey))
        {
            missing.Add($"{Section}:SecretKey");
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"Missing S3 setting(s): {string.Join(", ", missing)}.");
        }
    }

    public IAmazonS3 CreateClient()
    {
        Validate();
        var config = new AmazonS3Config
        {
            ForcePathStyle = ForcePathStyle,
            AuthenticationRegion = Region,
        };

        if (string.IsNullOrWhiteSpace(ServiceUrl))
        {
            config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(Region);
        }
        else
        {
            config.ServiceURL = ServiceUrl;
        }

        return new AmazonS3Client(new BasicAWSCredentials(AccessKey, SecretKey), config);
    }
}

/// <summary>A document stored by content: its SHA-256 (lower-case hex) is its address.</summary>
public interface IDocumentStore
{
    /// <summary>Stores the content if no object with its hash exists yet, and returns the hash.</summary>
    Task<string> PutAsync(string content, CancellationToken cancellationToken);

    /// <summary>Returns the content stored under the hash, or null when there is none.</summary>
    Task<string?> GetAsync(string sha256, CancellationToken cancellationToken);
}

/// <summary>Content-addressed documents at <c>documents/sha256/&lt;hex&gt;</c> (design D7).</summary>
public sealed class S3DocumentStore(IAmazonS3 s3, string bucket) : IDocumentStore
{
    public static string Hash(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    public static string KeyFor(string sha256) => $"documents/sha256/{sha256}";

    public async Task<string> PutAsync(string content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var sha256 = Hash(content);
        var key = KeyFor(sha256);

        if (await ExistsAsync(key, cancellationToken))
        {
            return sha256;
        }

        // A concurrent writer may store the same object between the check and the put. That is
        // harmless: the content is identical by construction.
        await s3.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = bucket,
                Key = key,
                ContentBody = content,
                ContentType = "text/markdown; charset=utf-8",
            },
            cancellationToken);

        return sha256;
    }

    public async Task<string?> GetAsync(string sha256, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await s3.GetObjectAsync(bucket, KeyFor(sha256), cancellationToken);
            using var reader = new StreamReader(response.ResponseStream, Encoding.UTF8);
            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            await s3.GetObjectMetadataAsync(bucket, key, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}
