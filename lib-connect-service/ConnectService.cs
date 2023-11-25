using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services;
using JWT.Algorithms;
using JWT.Builder;
using JWT.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using StackExchange.Redis;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

[assembly: InternalsVisibleTo("lib-connect-unit-tests")]
namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// Service component responsible for client connection handling.
/// </summary>
[DaprService("connect", typeof(IConnectService), lifetime: ServiceLifetime.Transient)]
public sealed class ConnectService : DaprService<ConnectServiceConfiguration>, IConnectService
{
    /// <summary>
    /// The very first byte of any request or response message.
    /// Indicates how the message content should be treated, and
    /// how the message itself should be routed through the system.
    /// </summary>
    [Flags]
    public enum MessageFlags : byte
    {
        /// <summary>
        /// The "default message type" is a text/JSON request, being
        /// made to a service endpoint (rather than to another client),
        /// unencrypted, uncompressed, standard priority, and expecting
        /// a response from the service.
        /// 
        /// With these flags, the next "ServiceID" section of the header
        /// would be referring to the Dapr service app to pass the message
        /// along to, after converting it to an HTTP request.
        /// 
        /// The "MessageID" section will also be enterring the system for
        /// the first time- the same ID will need to be handed back to the
        /// client for the response, so they can associate it back to the
        /// request themselves if needed.
        /// </summary>
        None = 0,
        /// <summary>
        /// Message payload is binary data.
        /// Default/off indicates Text/JSON.
        /// </summary>
        Binary = 1 << 0,
        /// <summary>
        /// Message payload is encrypted.
        /// </summary>
        Encrypted = 1 << 1,
        /// <summary>
        /// Message payload is compressed.
        /// </summary>
        Compressed = 1 << 2,
        /// <summary>
        /// Delivery at high priority- skip to the front of queues.
        /// </summary>
        HighPriority = 1 << 3,
        /// <summary>
        /// Message is an event- fire-and-forget.
        /// Default/off indicates an RPC- expects a response.
        /// </summary>
        Event = 1 << 4,
        /// <summary>
        /// Message should be handed off to another WebSocket connection.
        /// Default/off will direct the message to a Dapr service (HTTP).
        /// </summary>
        Client = 1 << 5,
        /// <summary>
        /// Message is the response to an RPC.
        /// Default/off indicates a request (or event).
        /// </summary>
        Response = 1 << 6
    };

    /// <summary>
    /// 
    /// </summary>
    public enum ResponseCodes : byte
    {
        OK = 0,

        RequestError = 10,
        RequestTooLarge,
        TooManyRequests,
        InvalidRequestChannel,

        Unauthorized = 20,

        ServiceNotFound = 30,
        ClientNotFound,
        MessageNotFound,

        Service_BadRequest = 50,
        Service_NotFound,
        Service_Unauthorized,
        Service_InternalServerError = 60
    };

    /// <summary>
    /// A request- either outgoing from the client to a service, or
    /// incoming from a service to the client (as an RPC- cool!).
    /// </summary>
    public struct ServiceRequestItem
    {
        public MessageFlags Flags { get; set; }
        public Guid MessageID { get; set; }
        public ushort MessageChannel { get; set; }
        public Guid ServiceID { get; set; }

        public byte[] Content { get; set; }
    }

    /// <summary>
    /// A response to an RPC, whether made by the client to a service,
    /// or from a service to the client, depending on where the request
    /// originated.
    /// </summary>
    public struct ServiceResponseItem
    {
        public MessageFlags Flags { get; set; }
        public Guid MessageID { get; set; }
        public ResponseCodes ResponseCode { get; set; }

        public byte[]? Content { get; set; }
    }

    private static HttpClient? _httpClient = null;
    /// <summary>
    /// The HttpClient used for all client requests which expect acknowledgement.
    /// 
    /// Defaults to JSON
    /// </summary>
    public static HttpClient HttpClient
    {
        get
        {
            if (_httpClient != null)
                return _httpClient;

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5),
                BaseAddress = GenerateServiceRequestUrl("bannou")
            };

            return _httpClient;
        }

        internal set { _httpClient = value; }
    }

    private static HttpClient? _fireAndForgetHttpClient = null;
    /// <summary>
    /// This Http Client has an exceptionally small timeout, so it can be awaited
    /// in a typical try/catch, but the catch not logged, in order to handle Fire
    /// And Forget payloads as efficiently as possible.
    /// 
    /// Most other approaches leave a lingering task in the background waiting for
    /// a response, even though one will never come. Better to clean up quickly.
    /// </summary>
    public static HttpClient FireAndForgetHttpClient
    {
        get
        {
            if (_fireAndForgetHttpClient != null)
                return _fireAndForgetHttpClient;

            _fireAndForgetHttpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMicroseconds(1),
                BaseAddress = GenerateServiceRequestUrl("bannou")
            };

            return _fireAndForgetHttpClient;
        }

        internal set { _fireAndForgetHttpClient = value; }
    }

    private Guid? _clientUUID;
    /// <summary>
    /// The client's unique GUID.
    /// Is created automatically per connection.
    /// </summary>
    public Guid? ClientGUID
    {
        get
        {
            if (_clientUUID != null)
                return _clientUUID;

            _clientUUID = Guid.NewGuid();
            return _clientUUID;
        }

        internal set { _clientUUID = value; }
    }

    private const byte RLE_FLAG = 0x80; // 1000 0000 in binary
    private static IConnectionMultiplexer? RedisConnection { get; set; }

    private static ConcurrentDictionary<string, ConnectService> ActiveSessions { get; set; } = new();
    private static List<string> ServiceNames { get; set; } = new() { "accounts", "authorization", "connect", "leaderboards" };
    private static Dictionary<Guid, string> ServiceLookup { get; } = ServiceNames.ToDictionary(name => Guid.NewGuid(), name => name);

    private ConcurrentQueue<ServiceRequestItem> Inbox { get; } = new();
    private ConcurrentQueue<ServiceRequestItem> Outbox { get; } = new();
    private ConcurrentQueue<ServiceRequestItem> PriorityOutbox { get; } = new();
    private ConcurrentDictionary<ushort, ConcurrentQueue<ServiceRequestItem>> ChannelInbox { get; } = new();
    private ConcurrentDictionary<ushort, ConcurrentQueue<ServiceRequestItem>> ChannelOutbox { get; } = new();
    private ConcurrentDictionary<Guid, Action<ServiceResponseItem>> NotifyResponse { get; } = new();
    private ConcurrentQueue<ServiceResponseItem> ResponseInbox { get; } = new();
    private ConcurrentQueue<ServiceResponseItem> ResponseOutbox { get; } = new();

    private Guid? ClientID;
    private List<string>? ClientRoleClaims;
    private List<string>? ClientServiceClaims;
    private List<string>? ClientScopeClaims;
    private string? ClientRoleClaimsStr;
    private string? ClientScopeClaimsStr;
    private readonly ReaderWriterLockSlim ClientDataLock = new();

    private CancellationTokenSource? ConnectionClosingCancellationToken;
    private bool ConnectionClosed;

    async Task IDaprService.OnStartAsync(CancellationToken cancellationToken)
    {
        if (Configuration.Redis_Connection_String == null)
            throw new NullReferenceException();

        RedisConnection = await ConnectionMultiplexer.ConnectAsync(Configuration.Redis_Connection_String);
        if (!RedisConnection.IsConnected)
            throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Could not connect to Redis for connection service.");
    }

    /// <summary>
    /// The client is attempting to establish a connection.
    /// 
    /// Because the specifics of the connection (websocket, etc) are dependant
    /// on the implementation, we just take the whole requestContext instead of
    /// specifying parameters.
    /// </summary>
    async Task<ServiceResponse> IConnectService.ConnectAsync(HttpContext requestContext)
    {
        try
        {
            Program.Logger.Log(LogLevel.Warning, "Connection request received from client.");

            // only handling websockets right now
            if (!requestContext.WebSockets.IsWebSocketRequest)
                return new(StatusCodes.BadRequest);

            Program.Logger.Log(LogLevel.Warning, "Websocket connection request received from client.");

            if (Configuration.Token_Public_Key == null)
                throw new NullReferenceException("Public token key required configuration is missing.");

            var authorization = requestContext.Request.Headers.Authorization.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(authorization))
                return new(StatusCodes.BadRequest);

            // handle reconnect request
            if (authorization.StartsWith("Reconnect "))
            {
                var reconnectToken = authorization.Remove(0, "Reconnect ".Length);
                if (ActiveSessions.TryGetValue(reconnectToken, out var activeSession))
                    return await activeSession.ReconnectAsync(requestContext.WebSockets);

                return new(StatusCodes.Forbidden);
            }

            // handle first connections
            var accessToken = authorization.Remove(0, "Bearer ".Length);
            if (string.IsNullOrWhiteSpace(accessToken))
                return new(StatusCodes.BadRequest);

            var clientClaims = ValidateAndDecodeToken(accessToken, Configuration.Token_Public_Key);
            if (clientClaims == null)
                return new(StatusCodes.Forbidden);

            // initialize client data from JWT
            ClientDataLock.EnterWriteLock();
            try
            {
                // get client ID
                if (clientClaims.TryGetValue("jti", out var ClientIDStr))
                    if (Guid.TryParse(ClientIDStr?.FirstOrDefault(), out var parsedClientID))
                        ClientID = parsedClientID;

                // the roles and scopes the client has access to
                if (clientClaims.TryGetValue("roles", out var roleClaims) && roleClaims != null && roleClaims.Any())
                {
                    ClientRoleClaims = roleClaims;
                    ClientRoleClaimsStr = JArray.FromObject(roleClaims).ToString(Newtonsoft.Json.Formatting.None);
                }

                if (clientClaims.TryGetValue("scopes", out var scopeClaims) && scopeClaims != null && scopeClaims.Any())
                {
                    ClientScopeClaims = scopeClaims;
                    ClientScopeClaimsStr = JArray.FromObject(scopeClaims).ToString(Newtonsoft.Json.Formatting.None);
                }

                // services the client has specific access to
                _ = clientClaims.TryGetValue("services", out ClientServiceClaims);

                // if admin, the client has access to ALL services
                if (roleClaims?.Contains("administrator") ?? false)
                    ClientServiceClaims = ServiceNames.ToList();
            }
            finally
            {
                ClientDataLock.ExitWriteLock();
            }
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, "An exception occurred setting up client connection.");
            return new(StatusCodes.InternalServerError);
        }

        // accept and create WebSocket connection
        try
        {
            var websocket = await requestContext.WebSockets.AcceptWebSocketAsync();

            // create tasks to handle data to and from the client through the WebSocket
            _ = Task.Run(() => ClientWebSocketReceiveTask(websocket), Program.ShutdownCancellationTokenSource.Token);
            _ = Task.Run(() => ClientWebSocketSendTask(websocket), Program.ShutdownCancellationTokenSource.Token);

            return new(StatusCodes.OK);
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, "An exception occurred starting client connection task.");
            return new(StatusCodes.InternalServerError);
        }
    }

    public async Task<ServiceResponse> ReconnectAsync(WebSocketManager socketManager)
    {
        // accept reconnection only if reconnection is still pending
        // - eliminate all race conditions
        try
        {
            if (ConnectionClosingCancellationToken == null)
                return new(StatusCodes.Forbidden);

            ConnectionClosingCancellationToken.Cancel();
            ConnectionClosingCancellationToken = null;

            if (ConnectionClosed)
                return new(StatusCodes.Forbidden);

            var websocket = await socketManager.AcceptWebSocketAsync();

            _ = Task.Run(() => ClientWebSocketReceiveTask(websocket), Program.ShutdownCancellationTokenSource.Token);
            _ = Task.Run(() => ClientWebSocketSendTask(websocket), Program.ShutdownCancellationTokenSource.Token);

            return new(StatusCodes.OK);
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, "An exception occurred creating client connection task on reconnect.");
            return new(StatusCodes.InternalServerError);
        }
    }

    /// <summary>
    /// Validate JWT / access token, decode and return a lookup of all
    /// claims that were in the token. Even if the original claim was
    /// a single string value, it will be given back here as a single
    /// item string list in the lookup, for uniformity.
    /// 
    /// jti = client ID
    /// scopes = JSON array of client scope claims, if any exist
    /// roles = JSON array of client role claims, if any exist
    /// services = JSON array of client service access claims
    /// </summary>
    internal static Dictionary<string, List<string>?>? ValidateAndDecodeToken(string token, string publicKey)
    {
        try
        {
            var publicKeyByes = Convert.FromBase64String(publicKey);
            var publicKeyDecoded = Encoding.UTF8.GetString(publicKeyByes);

            var publicRSA = RSA.Create();
            publicRSA.ImportFromPem(publicKeyDecoded);

            var json = new JwtBuilder()
                .WithAlgorithm(new RS512Algorithm(publicRSA, RSA.Create()))
                .MustVerifySignature()
                .Decode(token);

            var payload = JObject.Parse(json);
            return payload?.Properties()
                .Where(prop => prop.Value != null && prop.Value.Type != JTokenType.Null)
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

    /// <summary>
    /// Generates the base url for service invocations via Dapr.
    /// 
    /// Points to the Dapr sidecar endpoint, to forward on to other
    /// services in the network.
    /// </summary>
    internal static Uri? GenerateServiceRequestUrl(string appName)
    {
        if (Uri.TryCreate($"http://127.0.0.1:80/v1.0/invoke/{appName}/method/", UriKind.Absolute, out var rootUri))
            return rootUri;

        return null;
    }

    /// <summary>
    /// Converts a byte from the provided byte array into MessageFlags.
    /// </summary>
    internal static MessageFlags GetMessageFlags(byte[] buffer, ref int byteOffset)
    {
        var messageFlags = (MessageFlags)buffer[byteOffset];
        byteOffset += 1;

        return messageFlags;
    }

    /// <summary>
    /// Converts a byte from the provided byte array into a ResponseCode.
    /// </summary>
    internal static ResponseCodes GetMessageResponseCode(byte[] buffer, ref int byteOffset)
    {
        var responseCode = (ResponseCodes)buffer[byteOffset];
        byteOffset += 1;

        return responseCode;
    }

    /// <summary>
    /// Converts the next 16 bytes from the provided byte array into a GUID.
    /// Will reverse the byte order for little-endian systems, for consistency.
    /// </summary>
    internal static Guid GetServiceID(byte[] buffer, ref int byteOffset)
    {
        if (BitConverter.IsLittleEndian)
            Array.Reverse(buffer, byteOffset, 16);

        var guidSpan = new Span<byte>(buffer, byteOffset, 16);
        Guid serviceID = System.Runtime.InteropServices.MemoryMarshal.Read<Guid>(guidSpan);
        byteOffset += 16;

        return serviceID;
    }

    /// <summary>
    /// Converts the next 16 bytes from the provided byte array into a GUID.
    /// Will reverse the byte order for little-endian systems, for consistency.
    /// </summary>
    internal static Guid GetMessageID(byte[] buffer, ref int byteOffset)
    {
        if (BitConverter.IsLittleEndian)
            Array.Reverse(buffer, byteOffset, 16);

        var guidSpan = new Span<byte>(buffer, byteOffset, 16);
        Guid resultGuid = System.Runtime.InteropServices.MemoryMarshal.Read<Guid>(guidSpan);
        byteOffset += 16;

        return resultGuid;
    }

    /// <summary>
    /// Converts the next 1-2 bytes from the provided byte array into a ushort.
    /// Will reverse the byte order for little-endian systems, for consistency.
    /// </summary>
    internal static ushort GetMessageChannel(byte[] buffer, ref int byteOffset)
    {
        if (BitConverter.IsLittleEndian)
            Array.Reverse(buffer, byteOffset, 2);

        var messageChannel = BitConverter.ToUInt16(buffer, byteOffset);
        byteOffset += 2;

        return messageChannel;
    }

    /// <summary>
    /// Returns the remaining bytes as a new byte array.
    /// </summary>
    internal static byte[]? GetMessageContent(byte[] buffer, ref int byteOffset, int contentByteCount)
    {
        if (contentByteCount == 0)
            return null;

        var contentSpan = new Span<byte>(buffer, byteOffset, contentByteCount);
        byteOffset += contentByteCount;

        return contentSpan.ToArray();
    }

    /// <summary>
    /// Long-runner thread/task to handle receiving messages from the client through
    /// the WebSocket connection, and enqueuing them as requests to internal APIs.
    /// 
    /// Tries to process as little as humanly possible, to take in as much data from
    /// the client as possible, as quickly as possible, at all times. Enqueuing the
    /// requests will start a separate task (if one isn't already running) to handle
    /// queued requests until they run out, then exit.
    /// 
    /// It wouldn't be at all difficult to make the queue processing task also
    /// long-running, to start multiple threads/tasks just to handle those requests,
    /// etc, as needs demand.
    /// </summary>
    private async Task ClientWebSocketReceiveTask(WebSocket webSocket)
    {
        var buffer = new byte[Configuration.Client_Request_Max_Size];
        var offset = 0;

        WebSocketReceiveResult? receiveResult = null;
        while (receiveResult == null || receiveResult.CloseStatus == null)
        {
            // try receive message from client / queue to send to services
            try
            {
                receiveResult = await webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer, offset, buffer.Length - offset),
                    Program.ShutdownCancellationTokenSource.Token);

                offset += receiveResult.Count;

                if (!receiveResult.EndOfMessage)
                {
                    if (offset < buffer.Length)
                        continue;

                    // if the buffer is full, generate 'message too large' response before disconnecting
                    // will not allow a reconnection
                    var byteCounter = 0;
                    MessageFlags messageFlags = GetMessageFlags(buffer, ref byteCounter);
                    var messageID = GetMessageID(buffer, ref byteCounter);

                    var messageBytes = CreateResponseMessageBytes(messageFlags, messageID, ResponseCodes.RequestTooLarge);
                    await webSocket.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Binary, true, Program.ShutdownCancellationTokenSource.Token);
                    break;
                }
                else
                {
                    // the complete message is received and in the buffer now
                    var byteCounter = 0;
                    var messageFlags = GetMessageFlags(buffer, ref byteCounter);
                    var messageID = GetMessageID(buffer, ref byteCounter);

                    if (messageFlags.HasFlag(MessageFlags.Response))
                    {
                        // message is an RPC response
                        var responseCode = GetMessageResponseCode(buffer, ref byteCounter);
                        var messageContent = GetMessageContent(buffer, ref byteCounter, offset - byteCounter);
                        EnqueueResponseFromClient(messageFlags, messageID, responseCode, messageContent);
                    }
                    else
                    {
                        // message is an RPC request or event
                        var messageChannel = GetMessageChannel(buffer, ref byteCounter);
                        var serviceID = GetServiceID(buffer, ref byteCounter);
                        var messageContent = GetMessageContent(buffer, ref byteCounter, offset - byteCounter) ?? throw new NullReferenceException();
                        EnqueueRequestFromClient(messageFlags, messageID, messageChannel, serviceID, messageContent);
                    }
                }

                offset = 0;
            }
            catch (WebSocketException exc)
            {
                Program.Logger.Log(LogLevel.Error, exc, $"An exception occurred with client WebSocket connection.");

                // on websocket errors, allow client reconnection for a configurable amount of time
                if (Configuration.Client_Reconnection_Time > 0)
                {
                    ConnectionClosingCancellationToken ??= new CancellationTokenSource();
                    _ = Task.Run(async () =>
                        {
                            await Task.Delay(TimeSpan.FromSeconds(Configuration.Client_Reconnection_Time));
                            if (!ConnectionClosingCancellationToken.IsCancellationRequested)
                                ConnectionClosed = true;
                        }, ConnectionClosingCancellationToken.Token);
                }
            }
            catch (Exception exc)
            {
                Program.Logger.Log(LogLevel.Error, exc, $"An exception occurred handling client request.");

                // on any error that isn't the WebSocket itself, assume a bad actor
                // send back a request error before disconnecting- no reconnection
                if (webSocket.CloseStatus == null)
                {
                    var messageBytes = CreateResponseMessageBytes(MessageFlags.Response, Guid.Empty, ResponseCodes.RequestError);
                    await webSocket.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Binary, true, Program.ShutdownCancellationTokenSource.Token);
                }
            }
        }

        if (receiveResult != null && receiveResult.CloseStatus != null)
        {
            await webSocket.CloseAsync(
                receiveResult.CloseStatus.Value,
                receiveResult.CloseStatusDescription,
                CancellationToken.None);
        }
    }

    /// <summary>
    /// Long-running thread/task to process queued response/event items
    /// and send that data down to the client over the websocket.
    /// 
    /// Only this task should use WebSocket.SendAsync(), and this task
    /// should do absolutely nothing else, loop until the WebSocket is
    /// closed, with no artificial delays.
    /// </summary>
    private async Task ClientWebSocketSendTask(WebSocket webSocket)
    {
        WebSocketReceiveResult? receiveResult = null;
        while (receiveResult?.CloseStatus == null)
        {
            Guid? messageID = null;
            try
            {
                while (ResponseInbox.TryDequeue(out var nextResponse))
                {
                    messageID = nextResponse.MessageID;
                    var messageBytes = CreateResponseMessageBytes(nextResponse.Flags, nextResponse.MessageID, nextResponse.ResponseCode, nextResponse.Content);
                    await webSocket.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Binary, true, Program.ShutdownCancellationTokenSource.Token);

                    if (NotifyResponse.TryRemove(messageID.Value, out Action<ServiceResponseItem>? notifyResponse))
                        _ = Task.Run(() => notifyResponse?.Invoke(nextResponse));
                }
            }
            catch (Exception exc)
            {
                Program.Logger.Log(LogLevel.Error, exc, $"An exception occurred sending RPC response with ID [{messageID}] to client.");
            }

            try
            {
                if (Inbox.TryDequeue(out var nextRPC))
                {
                    messageID = nextRPC.MessageID;
                    var messageBytes = CreateRPCMessageBytes(nextRPC.Flags, nextRPC.MessageID, nextRPC.MessageChannel, nextRPC.ServiceID, nextRPC.Content);
                    await webSocket.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Binary, true, Program.ShutdownCancellationTokenSource.Token);
                }
            }
            catch (Exception exc)
            {
                Program.Logger.Log(LogLevel.Error, exc, $"An exception occurred sending RPC with ID [{messageID}] to client.");
            }
        }

        if (receiveResult != null && receiveResult.CloseStatus != null)
        {
            await webSocket.CloseAsync(
                receiveResult.CloseStatus.Value,
                receiveResult.CloseStatusDescription,
                CancellationToken.None);
        }
    }

    /// <summary>
    /// 
    /// </summary>
    internal static byte[] CreateResponseMessageBytes(MessageFlags flags, Guid messageID, ResponseCodes responseCode, byte[]? responseContent = null)
    {
        var messageIDBytes = messageID.ToByteArray();

        if (BitConverter.IsLittleEndian)
            Array.Reverse(messageIDBytes);

        var messageHeader = new byte[1 + messageIDBytes.Length + 1];
        messageHeader[0] = (byte)flags;
        messageHeader[^1] = (byte)responseCode;
        Buffer.BlockCopy(messageIDBytes, 0, messageHeader, 1, messageIDBytes.Length);

        if (responseContent == null || responseContent.Length == 0)
            return messageHeader;

        var messageBytes = new byte[messageHeader.Length + responseContent.Length];
        Buffer.BlockCopy(messageHeader, 0, messageBytes, 0, messageHeader.Length);
        Buffer.BlockCopy(responseContent, 0, messageBytes, messageHeader.Length, responseContent.Length);

        return messageBytes;
    }

    /// <summary>
    /// 
    /// </summary>
    internal static byte[] CreateRPCMessageBytes(MessageFlags flags, Guid messageID, ushort channel, Guid serviceID, byte[] messageContent)
    {
        var messageIDBytes = messageID.ToByteArray();
        var channelBytes = BitConverter.GetBytes(channel);
        var serviceIDBytes = serviceID.ToByteArray();

        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(messageIDBytes);
            Array.Reverse(channelBytes);
            Array.Reverse(serviceIDBytes);
        }

        var messageHeader = new byte[1 + messageIDBytes.Length + channelBytes.Length + serviceIDBytes.Length];
        messageHeader[0] = (byte)flags;
        Buffer.BlockCopy(messageIDBytes, 0, messageHeader, 1, messageIDBytes.Length);
        Buffer.BlockCopy(channelBytes, 0, messageHeader, 1 + messageIDBytes.Length, channelBytes.Length);
        Buffer.BlockCopy(serviceIDBytes, 0, messageHeader, 1 + messageIDBytes.Length + channelBytes.Length, serviceIDBytes.Length);

        var messageBytes = new byte[messageHeader.Length + messageContent.Length];
        Buffer.BlockCopy(messageHeader, 0, messageBytes, 0, messageHeader.Length);
        Buffer.BlockCopy(messageContent, 0, messageBytes, messageHeader.Length, messageContent.Length);

        return messageBytes;
    }

    /// <summary>
    /// Short-running task that processes all items in the typical message queue-
    /// in other words, those not in a channel queue. Will process high priority
    /// items first.
    /// </summary>
    private async Task OutboxProcessingTask()
    {
        await Task.CompletedTask;

        while (true)
        {
            while (PriorityOutbox.TryDequeue(out ServiceRequestItem priorityRequest))
                HandleNextRequest(priorityRequest);

            if (!Outbox.TryDequeue(out ServiceRequestItem normalRequest))
                break;

            HandleNextRequest(normalRequest);

            void HandleNextRequest(ServiceRequestItem nextRequest)
            {
                if (!ServiceLookup.TryGetValue(nextRequest.ServiceID, out var serviceName))
                {
                    ResponseCodes notFoundResponse = ResponseCodes.ServiceNotFound;
                    if (Configuration.Obfuscate_Not_Found_Response)
                        notFoundResponse = ResponseCodes.Unauthorized;

                    _ = EnqueueResponseToClient(nextRequest.Flags, nextRequest.MessageID, notFoundResponse);
                    return;
                }

                var hasServiceAccess = false;
                ClientDataLock.EnterReadLock();
                try
                {
                    hasServiceAccess = ClientServiceClaims?.Contains(serviceName) ?? false;
                }
                finally
                {
                    ClientDataLock.ExitReadLock();
                }

                if (!hasServiceAccess)
                {
                    _ = EnqueueResponseToClient(nextRequest.Flags, nextRequest.MessageID, ResponseCodes.Unauthorized);
                    return;
                }

                var fireAndForget = nextRequest.Flags.HasFlag(MessageFlags.Event);
                if (fireAndForget)
                {
                    _ = Task.Run(() => FireAndForgetServiceRequestTask(serviceName, nextRequest),
                        Program.ShutdownCancellationTokenSource.Token);

                    return;
                }

                // implement maximum number of outgoing requests/tasks?
                _ = Task.Run(() => ServiceRequestTask(serviceName, nextRequest),
                    Program.ShutdownCancellationTokenSource.Token);
            }
        }
    }

    /// <summary>
    /// Short-running task that processes all queued items in a given ChannelMessageQueue.
    /// These requests/responses need to be handled sequentially, and each responses needs
    /// to be waited on before continuing to the next.
    /// 
    /// Once the queue is empty, the task just exits- the service will create more tasks
    /// as needed if any items are added to an empty queue.
    /// </summary>
    private async Task ChannelOutboxProcessingTask(ushort channelKey)
    {
        while (ChannelOutbox.TryGetValue(channelKey, out ConcurrentQueue<ServiceRequestItem>? channelQueue) && channelQueue.TryDequeue(out var nextRequest))
        {
            if (!ServiceLookup.TryGetValue(nextRequest.ServiceID, out var serviceName))
            {
                ResponseCodes notFoundResponse = ResponseCodes.ServiceNotFound;
                if (Configuration.Obfuscate_Not_Found_Response)
                    notFoundResponse = ResponseCodes.Unauthorized;

                _ = EnqueueResponseToClient(nextRequest.Flags, nextRequest.MessageID, notFoundResponse);
                continue;
            }

            var hasServiceAccess = false;
            ClientDataLock.EnterReadLock();
            try
            {
                hasServiceAccess = ClientServiceClaims?.Contains(serviceName) ?? false;
            }
            finally
            {
                ClientDataLock.ExitReadLock();
            }

            if (!hasServiceAccess)
            {
                _ = EnqueueResponseToClient(nextRequest.Flags, nextRequest.MessageID, ResponseCodes.Unauthorized);
                continue;
            }

            var fireAndForget = nextRequest.Flags.HasFlag(MessageFlags.Event);
            if (fireAndForget)
            {
                await FireAndForgetServiceRequestTask(serviceName, nextRequest);
                continue;
            }

            // channel queues are processed sequentially
            // we wait for each response before continuing
            await ServiceRequestTask(serviceName, nextRequest);
        }
    }

    /// <summary>
    /// Short-lived task to handle an outgoing API request to a service, wait for the response,
    /// and then enqueue that response into the ResponseQueue appropriately, to be handled by the
    /// ClientReceiveConnectionTask when it's timely to send it down to the client.
    /// </summary>
    private async Task ServiceRequestTask(string serviceName, ServiceRequestItem request)
    {
        try
        {
            var serviceAction = new Uri($"/{serviceName}/action", UriKind.Relative);
            var requestMsg = new HttpRequestMessage(HttpMethod.Post, serviceAction);
            if (!request.Flags.HasFlag(MessageFlags.Binary))
                requestMsg.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            ClientDataLock.EnterReadLock();
            try
            {
                requestMsg.Headers.Add("REQUEST_IDS", $"USER_ID__{ClientID}");
                requestMsg.Headers.Add("roles", ClientRoleClaimsStr);
                requestMsg.Headers.Add("scopes", ClientScopeClaimsStr);
            }
            finally
            {
                ClientDataLock.ExitReadLock();
            }

            requestMsg.Content = new ByteArrayContent(request.Content);

            var serviceResponse = await HttpClient.SendAsync(requestMsg, Program.ShutdownCancellationTokenSource.Token);
            if (serviceResponse.IsSuccessStatusCode)
            {
                if (serviceResponse.Content != null)
                {
                    var response = await serviceResponse.Content.ReadAsByteArrayAsync();
                    if (response != null)
                    {
                        _ = EnqueueResponseToClient(request.Flags, request.MessageID, ResponseCodes.OK, response);
                        return;
                    }
                }

                _ = EnqueueResponseToClient(request.Flags, request.MessageID, ResponseCodes.OK);
            }
        }
        catch (HttpRequestException exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"An HTTP failure occurred executing action API method on service [{serviceName}].");
            switch (exc.StatusCode)
            {
                case System.Net.HttpStatusCode.Unauthorized:
                case System.Net.HttpStatusCode.Forbidden:
                    _ = EnqueueResponseToClient(request.Flags, request.MessageID, ResponseCodes.Service_Unauthorized);
                    break;
                case System.Net.HttpStatusCode.NotFound:
                    if (Configuration.Obfuscate_Not_Found_Response)
                        _ = EnqueueResponseToClient(request.Flags, request.MessageID, ResponseCodes.Service_Unauthorized);
                    else
                        _ = EnqueueResponseToClient(request.Flags, request.MessageID, ResponseCodes.Service_NotFound);
                    break;
                case System.Net.HttpStatusCode.BadRequest:
                    _ = EnqueueResponseToClient(request.Flags, request.MessageID, ResponseCodes.Service_BadRequest);
                    break;
                case System.Net.HttpStatusCode.InternalServerError:
                default:
                    _ = EnqueueResponseToClient(request.Flags, request.MessageID, ResponseCodes.Service_InternalServerError);
                    break;
            }
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"An exception occurred executing action API method on service [{serviceName}].");
            _ = EnqueueResponseToClient(request.Flags, request.MessageID, ResponseCodes.RequestError);
        }
    }

    /// <summary>
    /// Short-lived task to handle an outgoing fire-and-forget RPC call to a service.
    /// Will not publish a response to the client, even on failure to send at this point.
    /// 
    /// Uses a special HttpClient that times out in a microsecond, so that we can just await
    /// the task normally here, catch all exceptions, and avoid wasting any resources on the
    /// task being in the background and eventually timing out anyways.
    /// </summary>
    private async Task FireAndForgetServiceRequestTask(string serviceName, ServiceRequestItem request)
    {
        try
        {
            var serviceAction = new Uri($"/{serviceName}/action", UriKind.Relative);
            var requestMsg = new HttpRequestMessage(HttpMethod.Post, serviceAction);
            if (!request.Flags.HasFlag(MessageFlags.Binary))
                requestMsg.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            ClientDataLock.EnterReadLock();
            try
            {
                requestMsg.Headers.Add("REQUEST_IDS", $"USER_ID__{ClientID}");
                requestMsg.Headers.Add("roles", ClientRoleClaimsStr);
                requestMsg.Headers.Add("scopes", ClientScopeClaimsStr);
            }
            finally
            {
                ClientDataLock.ExitReadLock();
            }

            requestMsg.Content = new ByteArrayContent(request.Content);
            _ = await FireAndForgetHttpClient.SendAsync(requestMsg, Program.ShutdownCancellationTokenSource.Token);
        }
        catch { }
    }

    /// <summary>
    /// Enqueues an outgoing service request from the client.
    /// 
    /// Depending on the arguments, this could be a fire-and-forget
    /// RPC call, a synchronous channel queue message, or a typical
    /// asynchronous API request. It may also be text or binary, and
    /// in either case, both the request AND eventual response needs
    /// to be handled appropriately.
    /// 
    /// All requests can receive binary status code responses, even
    /// if the original request was text.
    /// </summary>
    private void EnqueueRequestFromClient(MessageFlags messageFlags, Guid messageID, ushort messageChannel, Guid serviceID, byte[] messageContent)
    {
        var queueWasEmpty = false;
        var newRequestQueueItem = new ServiceRequestItem()
        {
            Flags = messageFlags,
            ServiceID = serviceID,
            MessageID = messageID,
            Content = messageContent
        };

        if (messageChannel != 0)
        {
            // enqueue to appropriate channel queue
            _ = ChannelOutbox.AddOrUpdate(messageChannel,
                (channel) =>
                {
                    var newRequestQueue = new ConcurrentQueue<ServiceRequestItem>();
                    newRequestQueue.Enqueue(newRequestQueueItem);

                    // start processing task
                    _ = Task.Run(() => ChannelOutboxProcessingTask(messageChannel));

                    return newRequestQueue;
                },
                (channel, channelQueue) => // update
                {
                    queueWasEmpty = channelQueue.IsEmpty;
                    channelQueue.Enqueue(newRequestQueueItem);

                    // start new processing task, if needed
                    if (queueWasEmpty)
                        _ = Task.Run(() => ChannelOutboxProcessingTask(messageChannel));

                    return channelQueue;
                });

            return;
        }

        // enqueue as high-priority request
        if (messageFlags.HasFlag(MessageFlags.HighPriority))
        {
            queueWasEmpty = PriorityOutbox.IsEmpty;
            PriorityOutbox.Enqueue(newRequestQueueItem);
            if (queueWasEmpty)
                _ = Task.Run(OutboxProcessingTask);
        }

        // enqueue as typical request
        queueWasEmpty = Outbox.IsEmpty;
        Outbox.Enqueue(newRequestQueueItem);
        if (queueWasEmpty)
            _ = Task.Run(OutboxProcessingTask);
    }

    /// <summary>
    /// Enqueue a response given by the client for a given RPC.
    /// </summary>
    private bool EnqueueResponseFromClient(MessageFlags flags, Guid messageID, ResponseCodes responseCode, byte[]? content = null)
    {
        try
        {
            ResponseOutbox.Enqueue(new()
            {
                Flags = flags,
                MessageID = messageID,
                ResponseCode = responseCode,
                Content = content
            });

            return true;
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"An exception occurred enqueuing an RPC response from the client.");
        }

        return false;
    }

    /// <summary>
    /// Enqueue a response that should be fed back to the client for a given RPC.
    /// </summary>
    private bool EnqueueResponseToClient(MessageFlags flags, Guid messageID, ResponseCodes responseCode, byte[]? content = null)
    {
        try
        {
            ResponseInbox.Enqueue(new()
            {
                Flags = flags,
                MessageID = messageID,
                ResponseCode = responseCode,
                Content = content
            });

            return true;
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"An exception occurred enqueuing an RPC response to the client.");
        }

        return false;
    }
}
