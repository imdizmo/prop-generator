namespace PropGenerator;

/// <summary>Thrown when a work file cannot be read.</summary>
public sealed class WorkFileException : Exception
{
    /// <summary>Makes the exception.</summary>
    public WorkFileException()
    {
    }

    /// <summary>Makes the exception.</summary>
    /// <param name="message">What is wrong with the file.</param>
    public WorkFileException(string message)
        : base(message)
    {
    }

    /// <summary>Makes the exception.</summary>
    /// <param name="message">What is wrong with the file.</param>
    /// <param name="inner">The cause.</param>
    public WorkFileException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
