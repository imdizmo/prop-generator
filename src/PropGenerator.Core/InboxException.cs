namespace PropGenerator;

/// <summary>A delivery to Groundwork's inbox broke the inbox's rules, or its answer could not be read.</summary>
public sealed class InboxException(string message) : Exception(message);
