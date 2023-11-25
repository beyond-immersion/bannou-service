using JWT.Algorithms;
using JWT.Builder;
using JWT.Serializers;
using JWT;
using System.Security.Cryptography;
using System.Text;
using Xunit.Abstractions;
using Newtonsoft.Json.Linq;
using static BeyondImmersion.BannouService.Connect.ConnectService;

namespace BeyondImmersion.BannouService.Connect.UnitTests;

[Collection("connect unit tests")]
public class ConnectHelpers : IClassFixture<CollectionFixture>
{
    private CollectionFixture TestCollectionContext { get; }

    private string Test_Base64PublicKey { get; } = @"LS0tLS1CRUdJTiBQVUJMSUMgS0VZLS0tLS0NCk1JR2ZNQTBHQ1NxR1NJYjNEUUVCQVFVQUE0R05BRENCaVFLQmdRQ3k3MGJrMDlhckNBNWc1UDFNWnJLcWxEcWwNClNSSHVmOUdYWWZydXFGS0VVNUp5ZU9tOThHSmV6WUd6MUxoUlVObFZpSmpmMXltZGVNVzNnUitMQXlVN3JjMlcNCmkvdnNhNmwvcFQ1NW1QWncycG1xZWZlRHFNaVNWanZXQUs3eUI1ck45OFltWlRvVmh2MEVpd2ErQzNDRTNpQkYNCkIzZjhxSkZTUnYrb2cvcUFBUUlEQVFBQg0KLS0tLS1FTkQgUFVCTElDIEtFWS0tLS0t";
    private string Test_Base64PrivateKey { get; } = @"LS0tLS1CRUdJTiBSU0EgUFJJVkFURSBLRVktLS0tLQ0KTUlJQ1hRSUJBQUtCZ1FDeTcwYmswOWFyQ0E1ZzVQMU1acktxbERxbFNSSHVmOUdYWWZydXFGS0VVNUp5ZU9tOQ0KOEdKZXpZR3oxTGhSVU5sVmlKamYxeW1kZU1XM2dSK0xBeVU3cmMyV2kvdnNhNmwvcFQ1NW1QWncycG1xZWZlRA0KcU1pU1ZqdldBSzd5QjVyTjk4WW1aVG9WaHYwRWl3YStDM0NFM2lCRkIzZjhxSkZTUnYrb2cvcUFBUUlEQVFBQg0KQW9HQVN5Vkg3YU8xZktCbWdYVEpsOE50ZDB2SEVWRU5rYzdtTTZBM1pQRVZybkwyaHRLV3YyanJ0d3F4Vk5lYQ0KRExSYWdaeGJMMjVLRU5MK25lRkJZUDBTTGprWjAxd1ZiR0pGald6eXNzUk5BSG5xUVBwZnRLTHFXZFlRV04vLw0KY09RQ0hhWFhidTNkSE1lc0FwdGVEdWhTOEdON3gxMmpjZER5QlpEY3JaUFpFczBDUVFEZWJqQm0yWWpvK09qdA0KNm8yWjFTa2djc3hCazFBcXZMQlRvSjRTUEJaYnpjY21EcXZwaURmalVXZWlOWHBPM2xoZWw4YlA2S0RBU3J1VA0KZ0svQnhaN1hBa0VBemZDV2sreWREUjh2SkRCd3BJSnMxeFRYMVhBTkNQUExkYjdjbHQrMlhtQnY0TDh6WHpSWg0KRDNsNTdIZDNtTis5ZVVwYU5UQ2U5UFIyYjV6Y3JxKzA1d0pCQUtYaUw1N0VwV0o2SDkwdmpDTXA0ZDRkUDArNA0KZVpVbDI2ckNvcUNleDEzMy9ZblliMFZmSGE4ZVN0ZWlZbkRuU2FoaU1SZGxPbDJ1WG00SER3eklRVWtDUUR1bw0KUzJhVXI3WkNaMXliYjdZb1ZqRkVSM1g4SExxUUxVdHh1K00wOFZhTHB6MDdCajI1STJlWk1CbXhUZ01LSlpCQw0KV3JsMzJVUnFvUGlhUDArWGROY0NRUURCVkJscmhrV3gwY2s0OWdOZFFTdXpiZ0phRG16M0xaTUFLbVBVWDJ5aw0KdXZzcUw4L3orb05ZVjRQczUzekdSUXpMTEpiWjdMMXlpK3NqQS80dFkweFMNCi0tLS0tRU5EIFJTQSBQUklWQVRFIEtFWS0tLS0t";

    public ConnectHelpers(CollectionFixture collectionContext, ITestOutputHelper output)
    {
        TestCollectionContext = collectionContext;
        Program.Logger = output.BuildLoggerFor<ConnectHelpers>();
    }

    [Fact]
    public void GenerateServiceRequestUri()
    {
        var serviceUri = GenerateServiceRequestUrl("bannou");
        var baseDaprUri = new Uri("http://127.0.0.1:80/v1.0/invoke/", UriKind.Absolute);

        Assert.True(serviceUri?.IsAbsoluteUri ?? false);
        Assert.Equal(80, serviceUri.Port);
        Assert.True(serviceUri.IsLoopback);
        Assert.Equal("bannou/method/", baseDaprUri.MakeRelativeUri(serviceUri).ToString());
        Assert.Equal("../../", serviceUri.MakeRelativeUri(baseDaprUri).ToString());
    }

    [Fact]
    public void ValidateAndDecodeToken()
    {
        var jwtBuilder = CreateJWTBuilder();
        jwtBuilder.AddHeader("email", "test_user1@celestialmail.com");
        jwtBuilder.AddHeader("display-name", "Test User 1");

        var clientID = Guid.NewGuid().ToString();
        jwtBuilder.Id(clientID);
        jwtBuilder.Issuer("UNIT_TEST_FRAMEWORK");
        jwtBuilder.IssuedAt(DateTime.Now);
        jwtBuilder.ExpirationTime(DateTime.Now + TimeSpan.FromDays(1));
        jwtBuilder.MustVerifySignature();
        jwtBuilder.AddClaim("role", "user");

        var jwtString = jwtBuilder.Encode();
        Assert.NotEmpty(jwtString);

        var tokenProps = ConnectService.ValidateAndDecodeToken(jwtString, Test_Base64PublicKey);
        Assert.NotNull(tokenProps);
        Assert.Equal(clientID, tokenProps["jti"]?.FirstOrDefault());
    }

    [Fact]
    public void GetMessageID()
    {
        var guid1 = Guid.NewGuid();
        var guid2 = Guid.NewGuid();
        var guid3 = Guid.NewGuid();
        var guid1Bytes = guid1.ToByteArray();
        var guid2Bytes = guid2.ToByteArray();
        var guid3Bytes = new byte[32];
        guid3.ToByteArray().CopyTo(guid3Bytes, 16);

        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(guid1Bytes);
            Array.Reverse(guid2Bytes);
            Array.Reverse(guid3Bytes, 16, 16);
        }

        var byteCounter = 0;
        var messageID1 = ConnectService.GetMessageID(guid1Bytes, ref byteCounter);
        Assert.Equal(guid1, messageID1);
        Assert.Equal(16, byteCounter);

        byteCounter = 0;
        var messageID2 = ConnectService.GetMessageID(guid2Bytes, ref byteCounter);
        Assert.Equal(guid2, messageID2);
        Assert.Equal(16, byteCounter);

        var messageID3 = ConnectService.GetMessageID(guid3Bytes, ref byteCounter);
        Assert.Equal(guid3, messageID3);
        Assert.Equal(32, byteCounter);
    }

    [Fact]
    public void GetMessageChannel()
    {
        ushort channel1 = 0;
        ushort channel2 = 30;
        ushort channel3 = 300;
        var channel4 = ushort.MaxValue;
        var channel1Bytes = BitConverter.GetBytes(channel1);
        var channel2Bytes = BitConverter.GetBytes(channel2);
        var channel3Bytes = BitConverter.GetBytes(channel3);
        var channel4Bytes = new byte[4];
        BitConverter.GetBytes(channel4).CopyTo(channel4Bytes, 2);

        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(channel1Bytes);
            Array.Reverse(channel2Bytes);
            Array.Reverse(channel3Bytes);
            Array.Reverse(channel4Bytes, 2, 2);
        }

        var byteCounter = 0;
        var messageChannel1 = ConnectService.GetMessageChannel(channel1Bytes, ref byteCounter);
        Assert.Equal(channel1, messageChannel1);
        Assert.Equal(2, byteCounter);

        byteCounter = 0;
        var messageChannel2 = ConnectService.GetMessageChannel(channel2Bytes, ref byteCounter);
        Assert.Equal(channel2, messageChannel2);
        Assert.Equal(2, byteCounter);

        byteCounter = 0;
        var messageChannel3 = ConnectService.GetMessageChannel(channel3Bytes, ref byteCounter);
        Assert.Equal(channel3, messageChannel3);
        Assert.Equal(2, byteCounter);

        var messageChannel4 = ConnectService.GetMessageChannel(channel4Bytes, ref byteCounter);
        Assert.Equal(channel4, messageChannel4);
        Assert.Equal(4, byteCounter);
    }

    [Fact]
    public void GetServiceID()
    {
        var guid1 = Guid.NewGuid();
        var guid2 = Guid.NewGuid();
        var guid3 = Guid.NewGuid();
        var guid1Bytes = guid1.ToByteArray();
        var guid2Bytes = guid2.ToByteArray();
        var guid3Bytes = new byte[32];
        guid3.ToByteArray().CopyTo(guid3Bytes, 16);

        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(guid1Bytes);
            Array.Reverse(guid2Bytes);
            Array.Reverse(guid3Bytes, 16, 16);
        }

        var byteCounter = 0;
        var serviceID1 = ConnectService.GetServiceID(guid1Bytes, ref byteCounter);
        Assert.Equal(guid1, serviceID1);
        Assert.Equal(16, byteCounter);

        byteCounter = 0;
        var serviceID2 = ConnectService.GetServiceID(guid2Bytes, ref byteCounter);
        Assert.Equal(guid2, serviceID2);
        Assert.Equal(16, byteCounter);

        var serviceID3 = ConnectService.GetServiceID(guid3Bytes, ref byteCounter);
        Assert.Equal(guid3, serviceID3);
        Assert.Equal(32, byteCounter);
    }

    [Fact]
    public void GetMessageContent()
    {
        var contentObj = new JObject() { ["Test"] = true };
        var contentStr = contentObj.ToString(Newtonsoft.Json.Formatting.None);
        var contentBytes = Encoding.UTF8.GetBytes(contentStr);

        if (BitConverter.IsLittleEndian)
            Array.Reverse(contentBytes);

        var messageBytes = new byte[200];

        var byteCounter = 50;
        contentBytes.CopyTo(messageBytes, 50);
        var messageContent = ConnectService.GetMessageContent(messageBytes, ref byteCounter, contentBytes.Length);
        Assert.NotNull(messageContent);

        if (BitConverter.IsLittleEndian)
            Array.Reverse(messageContent);

        var messageContentStr = Encoding.UTF8.GetString(messageContent);
        Assert.Equal(messageContentStr, contentStr);
        Assert.Equal(50 + contentBytes.Length, byteCounter);

        byteCounter = 10;
        contentBytes.CopyTo(messageBytes, 10);
        messageContent = ConnectService.GetMessageContent(messageBytes, ref byteCounter, contentBytes.Length);
        Assert.NotNull(messageContent);

        if (BitConverter.IsLittleEndian)
            Array.Reverse(messageContent);

        messageContentStr = Encoding.UTF8.GetString(messageContent);
        Assert.Equal(messageContentStr, contentStr);
        Assert.Equal(10 + contentBytes.Length, byteCounter);
    }

    [Fact]
    public void GetMessageResponseCode()
    {
        var responseCode1 = ResponseCodes.OK;
        var responseCode2 = ResponseCodes.ServiceNotFound;
        var responseCode3 = ResponseCodes.Service_BadRequest;
        var responseBytes1 = new byte[] { (byte)responseCode1 };
        var responseBytes2 = new byte[] { (byte)responseCode2 };
        var responseBytes3 = new byte[] { default, (byte)responseCode3 };

        var byteCounter = 0;
        var response1 = ConnectService.GetMessageResponseCode(responseBytes1, ref byteCounter);
        Assert.Equal(response1, responseCode1);
        Assert.Equal(1, byteCounter);

        byteCounter = 0;
        var response2 = ConnectService.GetMessageResponseCode(responseBytes2, ref byteCounter);
        Assert.Equal(response2, responseCode2);
        Assert.Equal(1, byteCounter);

        var response3 = ConnectService.GetMessageResponseCode(responseBytes3, ref byteCounter);
        Assert.Equal(response3, responseCode3);
        Assert.Equal(2, byteCounter);
    }

    [Fact]
    public void CreateResponseMessageBytes()
    {
        var messageFlags = MessageFlags.None;
        var messageID = Guid.NewGuid();
        var responseCode = ResponseCodes.OK;
        var responseMessage = ConnectService.CreateResponseMessageBytes(messageFlags, messageID, responseCode);
        Assert.NotNull(responseMessage);
        Assert.Equal(18, responseMessage.Length);

        var parsedMessageFlags = (MessageFlags)responseMessage[0];
        Assert.Equal(messageFlags, parsedMessageFlags);

        if (BitConverter.IsLittleEndian)
            Array.Reverse(responseMessage, 1, 16);

        var parsedMessageID = new Guid(new Span<byte>(responseMessage, 1, 16));
        Assert.Equal(messageID, parsedMessageID);

        var parsedResponseCode = (ResponseCodes)responseMessage[17];
        Assert.Equal(responseCode, parsedResponseCode);
    }

    [Fact]
    public void CreateResponseMessageBytes_WithContent()
    {
        var contentObj = new JObject() { ["Test"] = true };
        var contentStr = contentObj.ToString(Newtonsoft.Json.Formatting.None);
        var contentBytes = Encoding.UTF8.GetBytes(contentStr);

        if (BitConverter.IsLittleEndian)
            Array.Reverse(contentBytes);

        var messageFlags = MessageFlags.None;
        var messageID = Guid.NewGuid();
        var responseCode = ResponseCodes.OK;
        var responseMessageBytes = ConnectService.CreateResponseMessageBytes(messageFlags, messageID, responseCode, contentBytes);
        Assert.NotNull(responseMessageBytes);
        Assert.Equal(contentBytes.Length + 18, responseMessageBytes.Length);

        var responseMessageFlags = (MessageFlags)responseMessageBytes[0];
        Assert.Equal(messageFlags, responseMessageFlags);

        if (BitConverter.IsLittleEndian)
            Array.Reverse(responseMessageBytes, 1, 16);

        var responseMessageID = new Guid(new Span<byte>(responseMessageBytes, 1, 16));
        Assert.Equal(messageID, responseMessageID);

        var responseResponseCode = (ResponseCodes)responseMessageBytes[17];
        Assert.Equal(responseCode, responseResponseCode);

        var responseContentBytes = new Span<byte>(responseMessageBytes, 18, responseMessageBytes.Length - 18).ToArray();

        if (BitConverter.IsLittleEndian)
            Array.Reverse(responseContentBytes);

        var responseMessageContentStr = Encoding.UTF8.GetString(responseContentBytes);
        Assert.Equal(contentStr, responseMessageContentStr);
    }

    [Fact]
    public void CreateRPCMessageBytes()
    {
        var contentObj = new JObject() { ["Test"] = true };
        var contentStr = contentObj.ToString(Newtonsoft.Json.Formatting.None);
        var contentBytes = Encoding.UTF8.GetBytes(contentStr);

        if (BitConverter.IsLittleEndian)
            Array.Reverse(contentBytes);

        var messageFlags = MessageFlags.None;
        var messageID = Guid.NewGuid();
        var messageChannel = (ushort)10;
        var serviceID = Guid.NewGuid();
        var rpcMessageBytes = ConnectService.CreateRPCMessageBytes(messageFlags, messageID, messageChannel, serviceID, contentBytes);
        Assert.NotNull(rpcMessageBytes);
        Assert.Equal(contentBytes.Length + 35, rpcMessageBytes.Length);

        var responseMessageFlags = (MessageFlags)rpcMessageBytes[0];
        Assert.Equal(messageFlags, responseMessageFlags);

        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(rpcMessageBytes, 1, 16);
            Array.Reverse(rpcMessageBytes, 17, 2);
            Array.Reverse(rpcMessageBytes, 19, 16);
        }

        var responseMessageID = new Guid(new Span<byte>(rpcMessageBytes, 1, 16));
        Assert.Equal(messageID, responseMessageID);

        var responseMessageChannel = BitConverter.ToUInt16(rpcMessageBytes, 17);
        Assert.Equal(messageChannel, responseMessageChannel);

        var responseServiceID = new Guid(new Span<byte>(rpcMessageBytes, 19, 16));
        Assert.Equal(serviceID, responseServiceID);

        var responseContentBytes = new Span<byte>(rpcMessageBytes, 35, rpcMessageBytes.Length - 35).ToArray();

        if (BitConverter.IsLittleEndian)
            Array.Reverse(responseContentBytes);

        var responseMessageContentStr = Encoding.UTF8.GetString(responseContentBytes);
        Assert.Equal(contentStr, responseMessageContentStr);
    }

    private JwtBuilder CreateJWTBuilder()
    {
        var jwtBuilder = new JwtBuilder();

        var publicKeyByes = Convert.FromBase64String(Test_Base64PublicKey);
        var publicKey = Encoding.UTF8.GetString(publicKeyByes);
        var privateKeyBytes = Convert.FromBase64String(Test_Base64PrivateKey);
        var privateKey = Encoding.UTF8.GetString(privateKeyBytes);

        var publicRSA = RSA.Create();
        publicRSA.ImportFromPem(publicKey);
        var privateRSA = RSA.Create();
        privateRSA.ImportFromPem(privateKey);
        var jwtAlgorithm = new RS512Algorithm(publicRSA, privateRSA);

        var jwtSerializer = new JsonNetSerializer();
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
