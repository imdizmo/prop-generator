namespace PropGenerator.Engine;

/// <summary>A call into easy-props' bundle failed.</summary>
public class EngineException : Exception
{
    /// <summary>Makes an error with no message.</summary>
    public EngineException()
    {
    }

    /// <summary>Makes an error.</summary>
    /// <param name="message">What failed.</param>
    public EngineException(string message)
        : base(message)
    {
    }

    /// <summary>Makes an error with its cause.</summary>
    /// <param name="message">What failed.</param>
    /// <param name="inner">The cause.</param>
    public EngineException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>The bundle threw (a JavaScript error, with its stack).</summary>
public sealed class EngineScriptException : EngineException
{
    /// <summary>Makes an error with no message.</summary>
    public EngineScriptException()
    {
    }

    /// <summary>Makes an error.</summary>
    /// <param name="message">What failed.</param>
    public EngineScriptException(string message)
        : base(message)
    {
    }

    /// <summary>Makes an error with its cause.</summary>
    /// <param name="message">What failed, with the script's stack.</param>
    /// <param name="inner">The cause.</param>
    public EngineScriptException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>The call ran past the engine's heap cap. That engine was thrown away; the next call gets a new one.</summary>
public sealed class EngineOutOfMemoryException : EngineException
{
    /// <summary>Makes an error with no message.</summary>
    public EngineOutOfMemoryException()
    {
    }

    /// <summary>Makes an error.</summary>
    /// <param name="message">What failed.</param>
    public EngineOutOfMemoryException(string message)
        : base(message)
    {
    }

    /// <summary>Makes an error with its cause.</summary>
    /// <param name="message">What failed.</param>
    /// <param name="inner">The cause.</param>
    public EngineOutOfMemoryException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
