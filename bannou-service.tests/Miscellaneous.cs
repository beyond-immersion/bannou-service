#pragma warning disable CS0618 // Intentional obsolete usage for testing IsObsolete() extension method

using JWT;
using JWT.Algorithms;
using JWT.Builder;
using JWT.Serializers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace BeyondImmersion.BannouService.Tests;

[Collection("unit tests")]
public class Miscellaneous : IClassFixture<CollectionFixture>
{
    private CollectionFixture TestCollectionContext { get; }

    private string Test_PEMPublicKey { get; } = @"-----BEGIN PUBLIC KEY-----
MIGfMA0GCSqGSIb3DQEBAQUAA4GNADCBiQKBgQCy70bk09arCA5g5P1MZrKqlDql
SRHuf9GXYfruqFKEU5JyeOm98GJezYGz1LhRUNlViJjf1ymdeMW3gR+LAyU7rc2W
i/vsa6l/pT55mPZw2pmqefeDqMiSVjvWAK7yB5rN98YmZToVhv0Eiwa+C3CE3iBF
B3f8qJFSRv+og/qAAQIDAQAB
-----END PUBLIC KEY-----";

    private string Test_Base64PublicKey { get; } = @"LS0tLS1CRUdJTiBQVUJMSUMgS0VZLS0tLS0NCk1JR2ZNQTBHQ1NxR1NJYjNEUUVCQVFVQUE0R05BRENCaVFLQmdRQ3k3MGJrMDlhckNBNWc1UDFNWnJLcWxEcWwNClNSSHVmOUdYWWZydXFGS0VVNUp5ZU9tOThHSmV6WUd6MUxoUlVObFZpSmpmMXltZGVNVzNnUitMQXlVN3JjMlcNCmkvdnNhNmwvcFQ1NW1QWncycG1xZWZlRHFNaVNWanZXQUs3eUI1ck45OFltWlRvVmh2MEVpd2ErQzNDRTNpQkYNCkIzZjhxSkZTUnYrb2cvcUFBUUlEQVFBQg0KLS0tLS1FTkQgUFVCTElDIEtFWS0tLS0t";

    private string Test_PEMPrivateKey { get; } = @"-----BEGIN RSA PRIVATE KEY-----
MIICXQIBAAKBgQCy70bk09arCA5g5P1MZrKqlDqlSRHuf9GXYfruqFKEU5JyeOm9
8GJezYGz1LhRUNlViJjf1ymdeMW3gR+LAyU7rc2Wi/vsa6l/pT55mPZw2pmqefeD
qMiSVjvWAK7yB5rN98YmZToVhv0Eiwa+C3CE3iBFB3f8qJFSRv+og/qAAQIDAQAB
AoGASyVH7aO1fKBmgXTJl8Ntd0vHEVENkc7mM6A3ZPEVrnL2htKWv2jrtwqxVNea
DLRagZxbL25KENL+neFBYP0SLjkZ01wVbGJFjWzyssRNAHnqQPpftKLqWdYQWN//
cOQCHaXXbu3dHMesApteDuhS8GN7x12jcdDyBZDcrZPZEs0CQQDebjBm2Yjo+Ojt
6o2Z1SkgcsxBk1AqvLBToJ4SPBZbzccmDqvpiDfjUWeiNXpO3lhel8bP6KDASruT
gK/BxZ7XAkEAzfCWk+ydDR8vJDBwpIJs1xTX1XANCPPLdb7clt+2XmBv4L8zXzRZ
D3l57Hd3mN+9eUpaNTCe9PR2b5zcrq+05wJBAKXiL57EpWJ6H90vjCMp4d4dP0+4
eZUl26rCoqCex133/YnYb0VfHa8eSteiYnDnSahiMRdlOl2uXm4HDwzIQUkCQDuo
S2aUr7ZCZ1ybb7YoVjFER3X8HLqQLUtxu+M08VaLpz07Bj25I2eZMBmxTgMKJZBC
Wrl32URqoPiaP0+XdNcCQQDBVBlrhkWx0ck49gNdQSuzbgJaDmz3LZMAKmPUX2yk
uvsqL8/z+oNYV4Ps53zGRQzLLJbZ7L1yi+sjA/4tY0xS
-----END RSA PRIVATE KEY-----";

    private string Test_Base64PrivateKey { get; } = @"LS0tLS1CRUdJTiBSU0EgUFJJVkFURSBLRVktLS0tLQ0KTUlJQ1hRSUJBQUtCZ1FDeTcwYmswOWFyQ0E1ZzVQMU1acktxbERxbFNSSHVmOUdYWWZydXFGS0VVNUp5ZU9tOQ0KOEdKZXpZR3oxTGhSVU5sVmlKamYxeW1kZU1XM2dSK0xBeVU3cmMyV2kvdnNhNmwvcFQ1NW1QWncycG1xZWZlRA0KcU1pU1ZqdldBSzd5QjVyTjk4WW1aVG9WaHYwRWl3YStDM0NFM2lCRkIzZjhxSkZTUnYrb2cvcUFBUUlEQVFBQg0KQW9HQVN5Vkg3YU8xZktCbWdYVEpsOE50ZDB2SEVWRU5rYzdtTTZBM1pQRVZybkwyaHRLV3YyanJ0d3F4Vk5lYQ0KRExSYWdaeGJMMjVLRU5MK25lRkJZUDBTTGprWjAxd1ZiR0pGald6eXNzUk5BSG5xUVBwZnRLTHFXZFlRV04vLw0KY09RQ0hhWFhidTNkSE1lc0FwdGVEdWhTOEdON3gxMmpjZER5QlpEY3JaUFpFczBDUVFEZWJqQm0yWWpvK09qdA0KNm8yWjFTa2djc3hCazFBcXZMQlRvSjRTUEJaYnpjY21EcXZwaURmalVXZWlOWHBPM2xoZWw4YlA2S0RBU3J1VA0KZ0svQnhaN1hBa0VBemZDV2sreWREUjh2SkRCd3BJSnMxeFRYMVhBTkNQUExkYjdjbHQrMlhtQnY0TDh6WHpSWg0KRDNsNTdIZDNtTis5ZVVwYU5UQ2U5UFIyYjV6Y3JxKzA1d0pCQUtYaUw1N0VwV0o2SDkwdmpDTXA0ZDRkUDArNA0KZVpVbDI2ckNvcUNleDEzMy9ZblliMFZmSGE4ZVN0ZWlZbkRuU2FoaU1SZGxPbDJ1WG00SER3eklRVWtDUUR1bw0KUzJhVXI3WkNaMXliYjdZb1ZqRkVSM1g4SExxUUxVdHh1K00wOFZhTHB6MDdCajI1STJlWk1CbXhUZ01LSlpCQw0KV3JsMzJVUnFvUGlhUDArWGROY0NRUURCVkJscmhrV3gwY2s0OWdOZFFTdXpiZ0phRG16M0xaTUFLbVBVWDJ5aw0KdXZzcUw4L3orb05ZVjRQczUzekdSUXpMTEpiWjdMMXlpK3NqQS80dFkweFMNCi0tLS0tRU5EIFJTQSBQUklWQVRFIEtFWS0tLS0t";

    [Obsolete(message: "Test message")]
    private readonly bool ObsoleteTestField = true;

    [Obsolete(message: "Test property message")]
    private bool ObsoleteTestProperty { get; set; } = true;

    [Obsolete(message: "Test method message")]
    private bool ObsoleteTestMethod() => true;

    private Miscellaneous(CollectionFixture collectionContext) => TestCollectionContext = collectionContext;

    public Miscellaneous(CollectionFixture collectionContext, ITestOutputHelper output)
    {
        TestCollectionContext = collectionContext;
        Program.Logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<Miscellaneous>.Instance;
    }

    [Fact]
    public void RemoveAccents()
    {
        Assert.Equal("Oceano", "Océano".RemoveAccent());
        Assert.Equal("Malmo", "Malmö".RemoveAccent());
        Assert.Equal("Dusseldorf", "Düsseldorf".RemoveAccent());
    }

    [Fact]
    public void GenerateWebsafeSlugs()
    {
        Assert.Equal("basic", "Basic".GenerateSlug());
        Assert.Equal("test-service", "Test Service".GenerateSlug());
        Assert.Equal("youll-never-believe-this-one", "You'll never believe this one".GenerateSlug());
    }

    [Fact]
    public void GenerateWebsafeSlugs_45CharacterLimit()
    {
        var testParagraph = "This is just a really long meaningless paragraph to test the slug length.";
        var testResult = "this-is-just-a-really-long-meaningless-paragr";

        Assert.Equal(45, testResult.Length);
        Assert.Equal(testResult, testParagraph.GenerateSlug());
    }

    [Fact]
    public void ObsoleteTest_Field()
    {
        System.Reflection.FieldInfo? obsMemberInfo = GetType().GetField("ObsoleteTestField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(obsMemberInfo);
        Assert.True(obsMemberInfo.IsObsolete());
    }

    [Fact]
    public void ObsoleteTest_Property()
    {
        System.Reflection.PropertyInfo? obsMemberInfo = GetType().GetProperty("ObsoleteTestProperty", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(obsMemberInfo);
        Assert.True(obsMemberInfo.IsObsolete());
    }

    [Fact]
    public void ObsoleteTest_Method()
    {
        System.Reflection.MethodInfo? obsMemberInfo = GetType().GetMethod(nameof(ObsoleteTestMethod), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(obsMemberInfo);
        Assert.True(obsMemberInfo.IsObsolete());
    }

    [Fact]
    public void ObsoleteTest_GetMessage()
    {
        System.Reflection.FieldInfo? obsMemberInfo = GetType().GetField(nameof(ObsoleteTestField), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(obsMemberInfo);

        _ = obsMemberInfo.IsObsolete(out var message);
        Assert.Equal("Test message", message);
    }

    [Fact]
    public void CreateRSA_Test()
    {
        using var publicRSA = RSA.Create();
        publicRSA.ImportFromPem(Test_PEMPublicKey);
        using var privateRSA = RSA.Create();
        privateRSA.ImportFromPem(Test_PEMPrivateKey);
        var keyAlgorithm = new RS512Algorithm(publicRSA, privateRSA);

        Assert.NotNull(keyAlgorithm);
        Assert.True(keyAlgorithm.IsAsymmetric());
    }

    [Fact]
    public void EncodeJWT()
    {
        var jwtBuilder = CreateJWTBuilder();
        jwtBuilder.AddHeader("email", "test_user1@celestialmail.com");
        jwtBuilder.AddHeader("display-name", "Test User 1");

        jwtBuilder.Id(Guid.NewGuid().ToString());
        jwtBuilder.Issuer("UNIT_TEST_FRAMEWORK");
        jwtBuilder.IssuedAt(DateTime.Now);
        jwtBuilder.ExpirationTime(DateTime.Now + TimeSpan.FromDays(1));
        jwtBuilder.MustVerifySignature();
        jwtBuilder.AddClaim("role", "user");

        var jwtString = jwtBuilder.Encode();
        Assert.NotEmpty(jwtString);
    }

    [Fact]
    public void DecodeJWT()
    {
        var jwtBuilder = CreateJWTBuilder();
        jwtBuilder.AddHeader("email", "test_user1@celestialmail.com");
        jwtBuilder.AddHeader("display-name", "Test User 1");

        jwtBuilder.Id(Guid.NewGuid().ToString());
        jwtBuilder.Issuer("UNIT_TEST_FRAMEWORK");
        jwtBuilder.IssuedAt(DateTime.Now);
        jwtBuilder.ExpirationTime(DateTime.Now + TimeSpan.FromDays(1));
        jwtBuilder.MustVerifySignature();
        jwtBuilder.AddClaim("role", "user");

        var encodedJwt = jwtBuilder.Encode();
        var decodedJwt = jwtBuilder.Decode(encodedJwt);

        Assert.NotNull(decodedJwt);
        var jwtObj = JsonNode.Parse(decodedJwt);
        Assert.NotNull(jwtObj);
        Assert.Equal("UNIT_TEST_FRAMEWORK", jwtObj["iss"]?.GetValue<string>());
        Assert.Equal("user", jwtObj["role"]?.GetValue<string>());
    }

    private JwtBuilder CreateJWTBuilder()
    {
        var jwtBuilder = new JwtBuilder();

        var publicKeyByes = Convert.FromBase64String(Test_Base64PublicKey);
        var publicKey = Encoding.UTF8.GetString(publicKeyByes);
        var privateKeyBytes = Convert.FromBase64String(Test_Base64PrivateKey);
        var privateKey = Encoding.UTF8.GetString(privateKeyBytes);

        using var publicRSA = RSA.Create();
        publicRSA.ImportFromPem(publicKey);
        using var privateRSA = RSA.Create();
        privateRSA.ImportFromPem(privateKey);
        var jwtAlgorithm = new RS512Algorithm(publicRSA, privateRSA);

        var jwtSerializer = new SystemTextSerializer();
        var jwtDateTimeProvider = new UtcDateTimeProvider();
        var jwtUrlEncoder = new JwtBase64UrlEncoder();
        var jwtValidator = new JwtValidator(jwtSerializer, jwtDateTimeProvider);
        var jwtEncoder = new JwtEncoder(jwtAlgorithm, jwtSerializer, jwtUrlEncoder);
        var jwtDecoder = new JwtDecoder(jwtSerializer, jwtValidator, jwtUrlEncoder, jwtAlgorithm);
        jwtBuilder.WithJsonSerializer(jwtSerializer);
        jwtBuilder.WithDateTimeProvider(jwtDateTimeProvider);
        jwtBuilder.WithUrlEncoder(jwtUrlEncoder);
        jwtBuilder.WithAlgorithm(jwtAlgorithm);
        jwtBuilder.WithEncoder(jwtEncoder);
        jwtBuilder.WithDecoder(jwtDecoder);
        jwtBuilder.WithValidator(jwtValidator);

        return jwtBuilder;
    }

}
