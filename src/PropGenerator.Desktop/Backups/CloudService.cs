namespace PropGenerator.Desktop.Backups;

/// <summary>An S3-compatible storage service the cloud backup names (easy-props' list); any other works as Other.</summary>
/// <param name="Id">Its id in the prefs, such as "r2".</param>
/// <param name="Label">Its name, such as "Cloudflare R2".</param>
/// <param name="EndpointHint">What its address looks like, or null when it needs none (Amazon S3).</param>
/// <param name="Region">Its usual region.</param>
/// <param name="KeysHint">Where its keys are found.</param>
internal sealed record CloudService(string Id, string Label, string? EndpointHint, string Region, string KeysHint);
