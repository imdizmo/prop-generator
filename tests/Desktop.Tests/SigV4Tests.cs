using System.Security.Cryptography;
using System.Text;
using PropGenerator.Desktop.Backups;
using Xunit;

namespace PropGenerator.Desktop.Tests;

// AWS's published Signature Version 4 test suite (aws-sig-v4-test-suite, as botocore ships it) and the signing-key
// example of the IAM guide: the signing is checked against the values AWS gives, never against itself.
public class SigV4Tests
{
    private const string SuiteKey = "AKIDEXAMPLE";
    private const string SuiteSecret = "wJalrXUtnFEMI/K7MDENG+bPxRfiCYEXAMPLEKEY";
    private static readonly DateTime SuiteDate = new(2015, 8, 30, 12, 36, 0, DateTimeKind.Utc);
    private static readonly string EmptyHash = Hex(SHA256.HashData([]));

    private static string Hex(byte[] b) => Convert.ToHexStringLower(b);

    private static SigV4Request Suite(string method, string path, params (string, string)[] query) =>
        new(method, path, query, [("Host", "example.amazonaws.com"), ("X-Amz-Date", "20150830T123600Z")], EmptyHash);

    [Fact]
    public void TheSigningKeyMatchesTheIamGuidesExample()
    {
        Assert.Equal(
            "f4780e2d9f65fa895f9c67b32ce1baf0b0d8a43505a000a1a9e090d414db404d",
            Hex(SigV4.SigningKey(SuiteSecret, "20120215", "us-east-1", "iam")));
        Assert.Equal(
            "c4afb1cc5771d871763a393e44b703571b55cc28424d1a5e86da6ed3c154a4b9",
            Hex(SigV4.SigningKey(SuiteSecret, "20150830", "us-east-1", "iam")));
    }

    [Fact]
    public void GetVanillaMatchesTheSuite()
    {
        SigV4Request r = Suite("GET", "/");
        string canonical = SigV4.CanonicalRequest(r);
        Assert.Equal(
            "GET\n/\n\nhost:example.amazonaws.com\nx-amz-date:20150830T123600Z\n\nhost;x-amz-date\n" + EmptyHash,
            canonical);
        Assert.Equal(
            "AWS4-HMAC-SHA256\n20150830T123600Z\n20150830/us-east-1/service/aws4_request\nbb579772317eb040ac9ed261061d46c1f17a8133879d6129b6e1c25292927e63",
            SigV4.StringToSign(r, SuiteDate, "us-east-1", "service"));
        Assert.Equal(
            "AWS4-HMAC-SHA256 Credential=AKIDEXAMPLE/20150830/us-east-1/service/aws4_request, SignedHeaders=host;x-amz-date, Signature=5fa00fa31553b73ebf1942676e86291e8372ff2a2260956d9b8aae1d763fbf31",
            SigV4.Authorization(r, SuiteKey, SuiteSecret, SuiteDate, "us-east-1", "service"));
    }

    [Theory]
    [InlineData("POST", "/", "", "5da7c1a2acd57cee7505fc6676e4e544621c30862966e37dddb68e92efbe5d6b")]
    [InlineData("GET", "/", "Param2=value2&Param1=value1", "b97d918cfa904a5beff61c982a1b6f458b799221646efd99d3219ec94cdf2500")]
    [InlineData("GET", "/-._~0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz", "", "07ef7494c76fa4850883e2b006601f940f8a34d404d0cfa977f52a65bbf5f24f")]
    public void OtherSuiteRequestsMatch(string method, string path, string query, string signature)
    {
        (string, string)[] pairs = [.. query.Split('&', StringSplitOptions.RemoveEmptyEntries).Select(p => (p.Split('=')[0], p.Split('=')[1]))];
        string auth = SigV4.Authorization(Suite(method, path, pairs), SuiteKey, SuiteSecret, SuiteDate, "us-east-1", "service");
        Assert.EndsWith("Signature=" + signature, auth, StringComparison.Ordinal);
    }

    [Fact]
    public void AnS3GetObjectWithARangeMatchesTheS3GuidesExample()
    {
        var r = new SigV4Request(
            "GET", "/test.txt", [],
            [("Host", "examplebucket.s3.amazonaws.com"), ("Range", "bytes=0-9"), ("x-amz-content-sha256", EmptyHash), ("x-amz-date", "20130524T000000Z")],
            EmptyHash);
        string auth = SigV4.Authorization(
            r, "AKIAIOSFODNN7EXAMPLE", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY", new DateTime(2013, 5, 24, 0, 0, 0, DateTimeKind.Utc), "us-east-1", "s3");
        Assert.Equal(
            "AWS4-HMAC-SHA256 Credential=AKIAIOSFODNN7EXAMPLE/20130524/us-east-1/s3/aws4_request, SignedHeaders=host;range;x-amz-content-sha256;x-amz-date, Signature=f0e8bdb87c964420e857bd35b5d6ed310bd44f0170aba48dd91039c6036bdb41",
            auth);
    }

    [Fact]
    public void KeysAreEncodedOnceWithSlashesKept()
    {
        Assert.Equal("/bucket/my%20props/a%2Bb.zip", SigV4.EncodePath("/bucket/my props/a+b.zip"));
        Assert.Equal("a%2Fb%3Dc", SigV4.Encode("a/b=c"));
        Assert.Equal(Encoding.UTF8.GetByteCount("ሴ") * 3, SigV4.Encode("ሴ").Length);
    }
}
