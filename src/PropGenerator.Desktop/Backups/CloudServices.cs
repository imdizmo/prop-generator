namespace PropGenerator.Desktop.Backups;

/// <summary>The services the cloud backup names, in easy-props' order (Cloudflare R2 first).</summary>
internal static class CloudServices
{
    /// <summary>Gets every service.</summary>
    internal static IReadOnlyList<CloudService> All { get; } =
    [
        new("r2", "Cloudflare R2", "<account-id>.r2.cloudflarestorage.com", "auto", "In Cloudflare: R2, Manage R2 API tokens, Create API token (Object Read and Write)."),
        new("b2", "Backblaze B2", "s3.<region>.backblazeb2.com", "us-west-004", "In Backblaze: Application Keys, Add a New Application Key (the keyID and the applicationKey)."),
        new("aws", "Amazon S3", null, "us-east-1", "In AWS: IAM, Users, Security credentials, Create access key."),
        new("wasabi", "Wasabi", "s3.<region>.wasabisys.com", "us-east-1", "In Wasabi: Access Keys, Create New Access Key."),
        new("spaces", "DigitalOcean Spaces", "<region>.digitaloceanspaces.com", "nyc3", "In DigitalOcean: API, Spaces Keys, Generate New Key."),
        new("minio", "MinIO", "minio.example.com:9000", "us-east-1", "In MinIO's console: Access Keys, Create access key."),
        new("other", "Other", "storage.example.com", "us-east-1", "Your storage's own page has them, often under Keys, Access keys or API."),
    ];

    /// <summary>The service with this id.</summary>
    /// <param name="id">The id.</param>
    /// <returns>The service, or Other for an unknown id.</returns>
    internal static CloudService Find(string? id) => All.FirstOrDefault(s => s.Id == id) ?? All[^1];
}
