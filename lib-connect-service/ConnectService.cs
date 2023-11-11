using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services;
using JWT.Algorithms;
using JWT.Builder;
using JWT.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using StackExchange.Redis;
using System;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// Service component responsible for client connection handling.
/// </summary>
[DaprService("connect", typeof(IConnectService))]
public sealed class ConnectService : DaprService<ConnectServiceConfiguration>, IConnectService
{
    private IConnectionMultiplexer? _redisConnection;

    async Task IDaprService.OnStart(CancellationToken cancellationToken)
    {
        if (Configuration.Redis_Connection_String == null)
            throw new NullReferenceException();

        _redisConnection = await ConnectionMultiplexer.ConnectAsync(Configuration.Redis_Connection_String);
        if (!_redisConnection.IsConnected)
            throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Could not connect to Redis.");
    }

    public async Task<ServiceResponse> Connect(HttpContext requestContext)
    {
        Dictionary<string, List<string>?>? clientClaims = null;

        // process connection request
        try
        {
            Program.Logger.Log(LogLevel.Warning, "Connection request received from client.");

            // only handling websockets right now
            if (!requestContext.WebSockets.IsWebSocketRequest)
                return new(StatusCodes.BadRequest);

            Program.Logger.Log(LogLevel.Warning, "Websocket connection request received from client.");

            var authorization = requestContext.Request.Headers.Authorization.FirstOrDefault();
            var accessToken = authorization?.Remove(0, "Bearer ".Length);
            if (string.IsNullOrWhiteSpace(accessToken))
                return new(StatusCodes.BadRequest);

            clientClaims = ValidateAndDecodeToken(accessToken);
            if (clientClaims == null)
                return new(StatusCodes.Forbidden);
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, "An exception occurred setting up client connection");
            return new(StatusCodes.InternalServerError);
        }

        // accept and start connection task
        try
        {
            var websocket = await requestContext.WebSockets.AcceptWebSocketAsync();
            _ = Task.Run(() => WebSocketClientConnectionHandler(websocket, clientClaims));

            return new(StatusCodes.OK);
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, "An exception occurred maintaining client connection");
            return new(StatusCodes.InternalServerError);
        }
    }

    [Flags]
    public enum MessageFlags : byte
    {
        None = 0,
        AckRequested = 1 << 0,
        HighPriority = 1 << 1,
        Encrypted = 1 << 2,
        Compressed = 1 << 3,
    };

    private static List<string> ServiceNames { get; } = new List<string> { "accounts", "authorization", "connect", "leaderboards" };
    private static Dictionary<uint, string> ServiceLookup { get; } = ServiceNames.ToDictionary(name => Misc.Crc32.ComputeCRC32(name), name => name);

    private static async Task WebSocketClientConnectionHandler(WebSocket webSocket, Dictionary<string, List<string>?> clientClaims)
    {
        // the roles and scopes the client has access to are forwarded
        // to every action endpoint, so the service handler has them too
        string? roleClaimsStr = null;
        string? scopeClaimsStr = null;
        if (clientClaims.TryGetValue("roles", out var roleClaims) && roleClaims != null && roleClaims.Any())
            roleClaimsStr = JArray.FromObject(roleClaims).ToString(Newtonsoft.Json.Formatting.None);
        if (clientClaims.TryGetValue("scopes", out var scopesClaims) && scopesClaims != null && scopesClaims.Any())
            scopeClaimsStr = JArray.FromObject(scopesClaims).ToString(Newtonsoft.Json.Formatting.None);

        // services the client has specific access to
        _ = clientClaims.TryGetValue("services", out List<string>? serviceClaims);

        // if admin, the client has access to ALL services
        if (roleClaims?.Contains("administrator") ?? false)
            serviceClaims = ServiceNames.ToList();

        // 8kb max message size
        var buffer = new byte[(1024 * 8) + 5];
        var offset = 0;

        WebSocketReceiveResult? receiveResult = null;
        while (!receiveResult?.CloseStatus.HasValue ?? false)
        {
            receiveResult = await webSocket.ReceiveAsync(
                new ArraySegment<byte>(buffer, offset, buffer.Length - offset),
                Program.ShutdownCancellationTokenSource.Token);

            offset += receiveResult.Count;
            if (receiveResult.EndOfMessage)
            {
                if (receiveResult.Count <= 5)
                {
                    Program.Logger.Log(LogLevel.Error, $"A message was received that was too small to process.");
                    continue;
                }

                var serviceCode = GetUIntServiceCode(buffer);
                if (!ServiceLookup.TryGetValue(serviceCode, out var serviceName))
                {
                    Program.Logger.Log(LogLevel.Error, $"Service for identifier code '{serviceCode}' not found.");
                    continue;
                }

                var messageFlags = (MessageFlags)buffer[0];
                if (!serviceClaims?.Contains(serviceName) ?? false)
                {
                    Program.Logger.Log(LogLevel.Error, $"Client access token doesn't have permission to access APIs for service '{serviceName}'.");
                    continue;
                }

                // forward the remainder (assuming application/JSON) to the service to handle
                var messageContent = new ByteArrayContent(buffer, 5, receiveResult.Count - 5);
                _ = Task.Run(() => ExecuteServiceAction(roleClaimsStr, scopeClaimsStr, serviceName, messageContent));

                offset = 0;
            }
        }

        if (receiveResult != null && receiveResult.CloseStatus != null)
        await webSocket.CloseAsync(
            receiveResult.CloseStatus.Value,
            receiveResult.CloseStatusDescription,
            CancellationToken.None);
    }

    /// <summary>
    /// Converts 4 bytes from the provided byte array into a uint.
    /// Will reverse the byte order for little-endian systems, for consistency.
    /// </summary>
    private static uint GetUIntServiceCode(byte[] bytes)
    {
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes, 1, 4);

        return BitConverter.ToUInt32(bytes, 1);
    }

    private static async Task<bool> ExecuteServiceAction(string? rolesStr, string? scopesStr, string service, HttpContent content)
    {
        try
        {
            var coordinatorService = Program.Configuration.Network_Mode ?? "bannou";
            var requestUrl = $"{service}/action";

            HttpRequestMessage requestMsg = Program.DaprClient.CreateInvokeMethodRequest(HttpMethod.Post, coordinatorService, requestUrl, content);
            if (!string.IsNullOrWhiteSpace(rolesStr))
                requestMsg.Headers.Add("roles", rolesStr);
            if (!string.IsNullOrWhiteSpace(scopesStr))
                requestMsg.Headers.Add("scopes", scopesStr);

            await Program.DaprClient.InvokeMethodAsync(requestMsg, Program.ShutdownCancellationTokenSource.Token);
            return true;
        }
        catch (HttpRequestException exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"An HTTP failure occurred executing action API method on service [{service}].");
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"An exception occurred executing action API method on service [{service}].");
        }

        return false;
    }

    private Dictionary<string, List<string>?>? ValidateAndDecodeToken(string token)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Configuration.Token_Public_Key))
                throw new NullReferenceException();

            var publicKeyByes = Convert.FromBase64String(Configuration.Token_Public_Key);
            var publicKeyDecoded = Encoding.UTF8.GetString(publicKeyByes);

            var publicRSA = RSA.Create();
            publicRSA.ImportFromPem(publicKeyDecoded);

            var json = new JwtBuilder()
                .WithAlgorithm(new RS512Algorithm(publicRSA, null))
                .MustVerifySignature()
                .Decode(token);

            var payload = JObject.Parse(json);
            return payload?.Properties()
                .Where(prop => prop.Value != null && prop.Value.Type != JTokenType.Null && prop.Value.HasValues)
                .ToDictionary(
                    prop => prop.Name,
                    prop => prop.Value.Type == JTokenType.Array
                        ? prop.Value.ToObject<List<string>>()
                        : new List<string> { prop.Value.ToString() }
                );
        }
        catch (TokenExpiredException exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, "JWT is expired.");
        }
        catch (SignatureVerificationException exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, "JWT has invalid signature.");
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, "An exception has occurred validating and decoding a JWT.");
        }

        return null;
    }
}
