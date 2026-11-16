namespace PropGenerator.Desktop.Tests.Support;

/// <summary>An HttpMessageHandler that answers every request as it is told (a storage misbehaving): no network.</summary>
/// <param name="answer">Makes the answer to a request.</param>
internal sealed class StubHttp(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
{
    /// <summary>Gets how many requests came.</summary>
    public int Requests { get; private set; }

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        return Task.FromResult(answer(request));
    }
}
