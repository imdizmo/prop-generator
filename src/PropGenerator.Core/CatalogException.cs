namespace PropGenerator;

/// <summary>The catalog or the map could not be read or written safely. Nothing was changed.</summary>
public sealed class CatalogException(string message) : Exception(message);
