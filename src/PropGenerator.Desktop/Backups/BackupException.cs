namespace PropGenerator.Desktop.Backups;

/// <summary>A backup could not be made, read or sent; the message says why in plain words.</summary>
public sealed class BackupException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="BackupException"/> class.</summary>
    public BackupException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="BackupException"/> class.</summary>
    /// <param name="message">What went wrong.</param>
    public BackupException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="BackupException"/> class.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="inner">The failure behind it.</param>
    public BackupException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
