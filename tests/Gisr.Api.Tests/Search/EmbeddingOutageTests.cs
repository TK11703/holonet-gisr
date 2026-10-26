using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections;
using Azure.Identity;
using Gisr.Api.Search;

namespace Gisr.Api.Tests.Search;

public class EmbeddingOutageTests
{
    private sealed class StatusResponse(int status) : PipelineResponse
    {
        private Stream? _content;

        public override int Status { get; } = status;

        public override string ReasonPhrase => "test";

        public override Stream? ContentStream
        {
            get => _content;
            set => _content = value;
        }

        public override BinaryData Content => BinaryData.Empty;

        protected override PipelineResponseHeaders HeadersCore { get; } = new EmptyHeaders();

        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => BinaryData.Empty;

        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) => new(BinaryData.Empty);

        public override void Dispose()
        {
        }

        private sealed class EmptyHeaders : PipelineResponseHeaders
        {
            public override bool TryGetValue(string name, out string? value)
            {
                value = null;
                return false;
            }

            public override bool TryGetValues(string name, out IEnumerable<string>? values)
            {
                values = null;
                return false;
            }

            public override IEnumerator<KeyValuePair<string, string>> GetEnumerator() => Enumerable.Empty<KeyValuePair<string, string>>().GetEnumerator();
        }
    }

    private static ClientResultException Http(int status) => new("service said no", new StatusResponse(status));

    [Fact]
    public void ServiceAndNetworkProblems_AreOutages()
    {
        Assert.True(EmbeddingHelper.IsOutage(new HttpRequestException("down")));
        Assert.True(EmbeddingHelper.IsOutage(new TimeoutException()));
        Assert.True(EmbeddingHelper.IsOutage(new AuthenticationFailedException("no credential")));
        Assert.True(EmbeddingHelper.IsOutage(new CredentialUnavailableException("no az login")));
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public void ThrottlingAuthenticationAndServerErrors_AreOutages(int status) =>
        Assert.True(EmbeddingHelper.IsOutage(Http(status)));

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(422)]
    public void AServiceRejectingTheRequestItself_IsNotAnOutage(int status) =>
        Assert.False(EmbeddingHelper.IsOutage(Http(status)));

    [Fact]
    public void OtherErrors_AreNotOutages()
    {
        Assert.False(EmbeddingHelper.IsOutage(new InvalidOperationException("The embedding service returned an unexpected result.")));
        Assert.False(EmbeddingHelper.IsOutage(new InvalidDataException("corrupt")));
    }
}
