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
using System.Security.Cryptography;
using System.Text;

namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// Service component responsible for client connection handling.
/// </summary>
[DaprService("connect", typeof(IConnectService), lifetime: ServiceLifetime.Transient)]
public sealed class ConnectService : DaprService<ConnectServiceConfiguration>, IConnectService
{
    private static IConnectionMultiplexer? _redisConnection;

    /// <summary>
    /// The very first byte of any request from the client.
    /// Indicates how the message content should be treated.
    /// </summary>
    [Flags]
    public enum MessageFlags : byte
    {
        None = 0,
        FireAndForget = 1 << 0,
        HighPriority = 1 << 1,
        Encrypted = 1 << 2,
        Compressed = 1 << 3,
        // unused = 1 << 4,
        // unused = 1 << 5,
        // unused = 1 << 6,
        // unused = 1 << 7
    };

    /// <summary>
    /// A queued outgoing API request from the client.
    /// Will be consumed by a queue processing task,
    /// then turned into an HTTP request to invoke
    /// on a given service via Dapr.
    /// </summary>
    public struct ServiceRequestItem
    {
        public MessageFlags Flags { get; set; }
        public bool IsBinary { get; set; }
        public uint MessageID { get; set; }
        public uint ServiceCode { get; set; }
        public ByteArrayContent Content { get; set; }
    }

    /// <summary>
    /// A queued incoming event or response to an
    /// earlier request from the client. A messageID
    /// of uint.MaxValue indicates an event with no
    /// originating request. Binary status code
    /// responses (ushort values as data) can occur
    /// even to text-formatted requests, on failures.
    /// </summary>
    public struct ServiceResponseItem
    {
        public bool IsEvent { get; set; }
        public bool IsBinary { get; set; }
        public uint MessageID { get; set; }
        public byte[] Data { get; set; }
    }

    /// <summary>
    /// +1 for MessageFlags
    /// +4 for MessageID
    /// +1-2 for MessageChannel
    /// +4 for ServiceCode
    /// </summary>
    private const uint MAX_REQUEST_HEADER_BYTES = 11;
    private const byte RLE_FLAG = 0x80; // 1000 0000 in binary

    private HttpClient? _httpClient = null;
    /// <summary>
    /// The HttpClient used for all client requests which expect acknowledgement.
    /// 
    /// Defaults to JSON
    /// </summary>
    public HttpClient HttpClient
    {
        get
        {
            if (_httpClient != null)
                return _httpClient;

            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(5);
            _httpClient.BaseAddress = GenerateServiceRequestUrl("bannou");

            ClientDataLock.EnterReadLock();
            try
            {
                _httpClient.DefaultRequestHeaders.Add("REQUEST_IDS", $"USER_ID__{ClientID}");
                _httpClient.DefaultRequestHeaders.Add("roles", ClientRoleClaimsStr);
                _httpClient.DefaultRequestHeaders.Add("scopes", ClientScopeClaimsStr);
            }
            finally
            {
                ClientDataLock.ExitReadLock();
            }

            return _httpClient;
        }

        private set { _httpClient = value; }
    }

    private HttpClient? _fireAndForgetHttpClient = null;
    /// <summary>
    /// This Http Client has an exceptionally small timeout, so it can be awaited
    /// in a typical try/catch, but the catch not logged, in order to handle Fire
    /// And Forget payloads as efficiently as possible.
    /// 
    /// Most other approaches leave a lingering task in the background waiting for
    /// a response, even though one will never come. Better to clean up quickly.
    /// </summary>
    public HttpClient FireAndForgetHttpClient
    {
        get
        {
            if (_fireAndForgetHttpClient != null)
                return _fireAndForgetHttpClient;

            _fireAndForgetHttpClient = new HttpClient();
            _fireAndForgetHttpClient.Timeout = TimeSpan.FromMicroseconds(1);
            _fireAndForgetHttpClient.BaseAddress = GenerateServiceRequestUrl("bannou");

            ClientDataLock.EnterReadLock();
            try
            {
                _fireAndForgetHttpClient.DefaultRequestHeaders.Add("REQUEST_IDS", $"USER_ID__{ClientID}");
                _fireAndForgetHttpClient.DefaultRequestHeaders.Add("roles", ClientRoleClaimsStr);
                _fireAndForgetHttpClient.DefaultRequestHeaders.Add("scopes", ClientScopeClaimsStr);
            }
            finally
            {
                ClientDataLock.ExitReadLock(); 
            }

            return _fireAndForgetHttpClient;
        }

        private set { _fireAndForgetHttpClient = value; }
    }

    private static byte[]? _okMsgBytes = null;
    /// <summary>
    /// The binary payload (2 byte ushort) for a 200 OK.
    /// 
    /// Can be returned to the client via EnqueueResponse,
    /// even if the original request wasn't binary.
    /// </summary>
    public static byte[] OKMsgBytes
    {
        get
        {
            if (_okMsgBytes != null)
                return _okMsgBytes;

            var tmp = BitConverter.GetBytes((ushort)200);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(tmp);

            _okMsgBytes = tmp;
            return _okMsgBytes;
        }
    }

    private static byte[]? _badRequestMsgBytes = null;
    /// <summary>
    /// The binary payload (2 byte ushort) for a 400 failure:
    /// Bad Request.
    /// 
    /// Can be returned to the client via EnqueueResponse,
    /// even if the original request wasn't binary.
    /// </summary>
    public static byte[] BadRequestMsgBytes
    {
        get
        {
            if (_badRequestMsgBytes != null)
                return _badRequestMsgBytes;

            var tmp = BitConverter.GetBytes((ushort)400);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(tmp);

            _badRequestMsgBytes = tmp;
            return _badRequestMsgBytes;
        }
    }

    private static byte[]? _forbiddenMsgBytes = null;
    /// <summary>
    /// The binary payload (2 byte ushort) for a 404 failure:
    /// Forbidden.
    /// 
    /// Can be returned to the client via EnqueueResponse,
    /// even if the original request wasn't binary.
    /// </summary>
    public static byte[] ForbiddenMsgBytes
    {
        get
        {
            if (_forbiddenMsgBytes != null)
                return _forbiddenMsgBytes;

            var tmp = BitConverter.GetBytes((ushort)403);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(tmp);

            _forbiddenMsgBytes = tmp;
            return _forbiddenMsgBytes;
        }
    }

    private static byte[]? _notFoundMsgBytes = null;
    /// <summary>
    /// The binary payload (2 byte ushort) for a 404 failure:
    /// Not Found.
    /// 
    /// Can be returned to the client via EnqueueResponse,
    /// even if the original request wasn't binary.
    /// </summary>
    public static byte[] NotFoundMsgBytes
    {
        get
        {
            if (_notFoundMsgBytes != null)
                return _notFoundMsgBytes;

            var tmp = BitConverter.GetBytes((ushort)404);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(tmp);

            _notFoundMsgBytes = tmp;
            return _notFoundMsgBytes;
        }
    }

    private static byte[]? _tooLargeMsgBytes = null;
    /// <summary>
    /// The binary payload (2 byte ushort) for a 413 failure:
    /// Message Too Large.
    /// 
    /// Can be returned to the client via EnqueueResponse,
    /// even if the original request wasn't binary.
    /// </summary>
    public static byte[] TooLargeMsgBytes
    {
        get
        {
            if (_tooLargeMsgBytes != null)
                return _tooLargeMsgBytes;

            var tmp = BitConverter.GetBytes((ushort)413);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(tmp);

            _tooLargeMsgBytes = tmp;
            return _tooLargeMsgBytes;
        }
    }

    private static byte[]? _serverErrorMsgBytes = null;
    /// <summary>
    /// The binary payload (2 byte ushort) for a 500 failure:
    /// Internal Server Error.
    /// 
    /// Can be returned to the client via EnqueueResponse,
    /// even if the original request wasn't binary.
    /// </summary>
    public static byte[] ServerErrorMsgBytes
    {
        get
        {
            if (_serverErrorMsgBytes != null)
                return _serverErrorMsgBytes;

            var tmp = BitConverter.GetBytes((ushort)500);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(tmp);

            _serverErrorMsgBytes = tmp;
            return _serverErrorMsgBytes;
        }
    }

    private static List<string> ServiceNames { get; set; } = new List<string> { "accounts", "authorization", "connect", "leaderboards" };
    private static Dictionary<uint, string> ServiceLookup { get; } = ServiceNames.ToDictionary(name => Misc.Crc32.ComputeCRC32(name), name => name);
    private ConcurrentQueue<ServiceRequestItem> PriorityMessageQueue { get; } = new();
    private ConcurrentQueue<ServiceRequestItem> MessageQueue { get; } = new();
    private ConcurrentQueue<ServiceResponseItem> ResponseQueue { get; } = new();
    private ConcurrentDictionary<ushort, ConcurrentQueue<ServiceRequestItem>> ChannelMessageQueue { get; } = new();

    private Task? ClientSendTask;
    private Task? ClientReceiveTask;

    private int? ClientID;
    private List<string>? ClientRoleClaims;
    private List<string>? ClientServiceClaims;
    private List<string>? ClientScopeClaims;
    private string? ClientRoleClaimsStr;
    private string? ClientScopeClaimsStr;
    private ReaderWriterLockSlim ClientDataLock { get; } = new ReaderWriterLockSlim();

    async Task IDaprService.OnStartAsync(CancellationToken cancellationToken)
    {
        if (Configuration.Redis_Connection_String == null)
            throw new NullReferenceException();

        _redisConnection = await ConnectionMultiplexer.ConnectAsync(Configuration.Redis_Connection_String);
        if (!_redisConnection.IsConnected)
            throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Could not connect to Redis for connection service.");
    }

    /// <summary>
    /// The client is attempting to establish a connection.
    /// 
    /// Because the specifics of the connection (websocket, etc) are dependant
    /// on the implementation, we just take the whole requestContext instead of
    /// specifying parameters.
    /// </summary>
    public async Task<ServiceResponse> ConnectAsync(HttpContext requestContext)
    {
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

            var clientClaims = ValidateAndDecodeToken(accessToken);
            if (clientClaims == null)
                return new(StatusCodes.Forbidden);

            // initialize client data
            ClientDataLock.EnterWriteLock();
            try
            {
                // set client ID
                if (clientClaims.TryGetValue("jti", out var ClientIDStr))
                    if (int.TryParse(ClientIDStr?.FirstOrDefault(), out var parsedClientID))
                        ClientID = parsedClientID;

                // the roles and scopes the client has access to are forwarded
                // to every action endpoint, so the service handler has them too
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
            ClientSendTask = Task.Run(() => ClientSendConnectionTask(websocket));
            ClientReceiveTask = Task.Run(() => ClientReceiveConnectionTask(websocket));

            return new(StatusCodes.OK);
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, "An exception occurred starting client connection task.");
            return new(StatusCodes.InternalServerError);
        }
    }

    /// <summary>
    /// Generates the base url for service invocations via Dapr.
    /// 
    /// Points to the Dapr sidecar endpoint, to forward on to other
    /// services in the network.
    /// </summary>
    private static Uri? GenerateServiceRequestUrl(string applicationName)
    {
        if (Uri.TryCreate($"http://127.0.0.1:80/v1.0/invoke/{applicationName}/method/", UriKind.Absolute, out var rootUri))
            return rootUri;

        return null;
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

    /// <summary>
    /// Converts a byte from the provided byte array into MessageFlags.
    /// </summary>
    private static MessageFlags GetMessageFlags(byte[] bytes, ref int byteCounter)
    {
        var messageFlags = (MessageFlags)bytes[0];
        byteCounter += 1;

        return messageFlags;
    }

    /// <summary>
    /// Converts the next 4 bytes from the provided byte array into a uint.
    /// Will reverse the byte order for little-endian systems, for consistency.
    /// </summary>
    private static uint GetServiceCode(byte[] bytes, ref int byteCounter)
    {
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes, byteCounter, 4);

        var serviceCode = BitConverter.ToUInt32(bytes, byteCounter);
        byteCounter += 4;

        return serviceCode;
    }

    /// <summary>
    /// Converts the next 4 bytes from the provided byte array into a uint.
    /// Will reverse the byte order for little-endian systems, for consistency.
    /// </summary>
    private static uint GetMessageID(byte[] bytes, ref int byteCounter)
    {
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes, byteCounter, 4);

        var messageID = BitConverter.ToUInt32(bytes, byteCounter);
        byteCounter += 4;

        return messageID;
    }

    /// <summary>
    /// Converts the next 1-2 bytes from the provided byte array into a ushort.
    /// Will reverse the byte order for little-endian systems, for consistency.
    /// </summary>
    private static ushort GetMessageChannel(byte[] bytes, ref int byteCounter)
    {
        if ((bytes[byteCounter] & RLE_FLAG) == RLE_FLAG)
        {
            // If RLE flag is set, combine both bytes to get the channel
            var messageChannel = (ushort)(((bytes[byteCounter] & 0x7F) << 8) | bytes[byteCounter + 1]);
            byteCounter += 2;
            return messageChannel;
        }
        else
        {
            // Only the first byte is used for the channel
            var messageChannel = (ushort)bytes[byteCounter];
            byteCounter += 1;
            return messageChannel;
        }
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
    private async Task ClientSendConnectionTask(WebSocket webSocket)
    {
        // configurable max message size
        byte[] buffer = new byte[MAX_REQUEST_HEADER_BYTES + Configuration.Max_Client_Request_Bytes];
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
                    // check if the buffer is full- if not, then loop/read more
                    if (offset < buffer.Length)
                        continue;

                    Program.Logger.Log(LogLevel.Error, $"A message was received that was too large to process.");

                    var byteCounter = 0;
                    MessageFlags messageFlags = GetMessageFlags(buffer, ref byteCounter);
                    var messageID = GetMessageID(buffer, ref byteCounter);
                    await WaitUntilEndOfMessage(receiveResult);
                    EnqueueResponse(messageID, TooLargeMsgBytes);
                }
                else
                {
                    // the complete message is received and in the buffer now
                    var byteCounter = 0;
                    var messageFlags = GetMessageFlags(buffer, ref byteCounter);
                    var messageID = GetMessageID(buffer, ref byteCounter);
                    var messageChannel = GetMessageChannel(buffer, ref byteCounter);
                    var serviceCode = GetServiceCode(buffer, ref byteCounter);
                    var messageContent = new ByteArrayContent(buffer, byteCounter, offset - byteCounter);
                    EnqueueServiceRequest(messageFlags, messageID, serviceCode, messageChannel, messageContent, receiveResult.MessageType == WebSocketMessageType.Binary);
                }

                offset = 0;
            }
            catch (Exception exc)
            {
                Program.Logger.Log(LogLevel.Error, exc, $"An exception occurred handling client request. Attempting to send BadRequest response back.");

                try
                {
                    var byteCounter = 0;
                    var messageFlags = GetMessageFlags(buffer, ref byteCounter);
                    var messageID = GetMessageID(buffer, ref byteCounter);
                    await WaitUntilEndOfMessage(receiveResult);
                    EnqueueResponse(messageID, BadRequestMsgBytes);
                }
                catch (Exception innerExc)
                {
                    Program.Logger.Log(LogLevel.Error, innerExc, $"A critical exception occurred returning a BadRequest response to the client.");
                }

                offset = 0;
            }
        }

        if (receiveResult != null && receiveResult.CloseStatus != null)
        {
            await webSocket.CloseAsync(
                receiveResult.CloseStatus.Value,
                receiveResult.CloseStatusDescription,
                CancellationToken.None);
        }

        async Task WaitUntilEndOfMessage(WebSocketReceiveResult? received)
        {
            while (!received?.EndOfMessage ?? false)
            {
                received = await webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer, 0, buffer.Length),
                    Program.ShutdownCancellationTokenSource.Token);

                if (received.CloseStatus.HasValue)
                    return;
            }
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
    private async Task ClientReceiveConnectionTask(WebSocket webSocket)
    {
        WebSocketReceiveResult? receiveResult = null;
        while (receiveResult == null || receiveResult.CloseStatus == null)
        {
            // try send event(s)/response(s) to client from services
            uint messageID = 0;
            try
            {
                if (ResponseQueue.TryDequeue(out var nextResponse))
                {
                    messageID = nextResponse.MessageID;
                    await SendDataAsync(webSocket, nextResponse.IsBinary, nextResponse.MessageID, nextResponse.Data);
                }
            }
            catch (Exception exc)
            {
                Program.Logger.Log(LogLevel.Error, exc, $"An exception occurred passing down event/response with ID [{messageID}] to client.");
            }
        }

        if (receiveResult != null && receiveResult.CloseStatus != null)
        {
            await webSocket.CloseAsync(
                receiveResult.CloseStatus.Value,
                receiveResult.CloseStatusDescription,
                CancellationToken.None);
        }

        /// <summary>
        /// Local methods to prevent anything being able to get
        /// around the response/event queue system.
        /// 
        /// Send a payload over the websocket back to the client.
        /// Events not originating with the client should use the
        /// service ID as the message ID, by convention.
        /// </summary>
        async Task SendDataAsync(WebSocket webSocket, bool isBinary, uint messageID, byte[] responseBody)
        {
            if (responseBody.Length > Configuration.Max_Client_Response_Bytes)
            {
                await SendFragmentedDataAsync(webSocket, isBinary, messageID, responseBody);
                return;
            }

            var messageIDBytes = BitConverter.GetBytes(messageID);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(messageIDBytes);

            // we effectively skip 2 bytes in the header, because
            // the message wasn't large enough to need fragmenting
            var responseHeader = new byte[2 + messageIDBytes.Length];
            Buffer.BlockCopy(messageIDBytes, 0, responseHeader, 2, messageIDBytes.Length);

            var responseBytes = new byte[responseHeader.Length + responseBody.Length];
            Buffer.BlockCopy(responseHeader, 0, responseBytes, 0, responseHeader.Length);
            Buffer.BlockCopy(responseBody, 0, responseBytes, responseHeader.Length, responseBody.Length);

            await webSocket.SendAsync(new ArraySegment<byte>(responseBytes), isBinary ? WebSocketMessageType.Binary : WebSocketMessageType.Text, true, Program.ShutdownCancellationTokenSource.Token);
        }

        /// <summary>
        /// Send many messages worth of data over the websocket
        /// back to the client. Includes a byte for the current
        /// fragment ID, and another for the total fragments.
        /// </summary>
        async Task SendFragmentedDataAsync(WebSocket webSocket, bool isBinary, uint messageID, byte[] responseBody)
        {
            if (responseBody.Length >= Configuration.Max_Client_Response_Bytes * (int)byte.MaxValue)
            {
                Program.Logger.Log(LogLevel.Error, $"The event or response for message/service ID [{messageID}] was too large for fragmenting.");
                await SendDataAsync(webSocket, true, messageID, ServerErrorMsgBytes);

                return;
            }

            var totalMessages = (int)MathF.Ceiling((float)responseBody.Length / (float)Configuration.Max_Client_Response_Bytes);

            var messageIDBytes = BitConverter.GetBytes(messageID);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(messageIDBytes);

            // the header byte array to include in each message
            var responseHeader = new byte[2 + messageIDBytes.Length];

            // copy the fragment total into the second array index
            responseHeader[1] = (byte)totalMessages;

            // copy the message ID in first
            Buffer.BlockCopy(messageIDBytes, 0, responseHeader, 2, messageIDBytes.Length);

            for (var i = 0; i < totalMessages; i++)
            {
                // on each message, replace the current fragment number byte in the response header
                responseHeader[0] = (byte)i;

                // create the full message from the header + body
                var responseBytes = new byte[responseHeader.Length + responseBody.Length];
                Buffer.BlockCopy(responseHeader, 0, responseBytes, 0, responseHeader.Length);
                Buffer.BlockCopy(responseBody, 0, responseBytes, responseHeader.Length, responseBody.Length);

                // forward message fragment on to client
                await webSocket.SendAsync(new ArraySegment<byte>(responseBytes), isBinary ? WebSocketMessageType.Binary : WebSocketMessageType.Text, true, Program.ShutdownCancellationTokenSource.Token);
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
    private async Task ChannelQueueProcessingTask(ushort channelKey)
    {
        while (ChannelMessageQueue.TryGetValue(channelKey, out ConcurrentQueue<ServiceRequestItem>? channelQueue) && channelQueue.TryDequeue(out var nextRequest))
        {
            if (!ServiceLookup.TryGetValue(nextRequest.ServiceCode, out var serviceName))
            {
                EnqueueResponse(nextRequest, ForbiddenMsgBytes);
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
                EnqueueResponse(nextRequest, ForbiddenMsgBytes);
                continue;
            }

            var fireAndForget = nextRequest.Flags.HasFlag(MessageFlags.FireAndForget);
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
    /// Short-running task that processes all items in the typical message queue-
    /// in other words, those not in a channel queue. Will process high priority
    /// items first.
    /// </summary>
    private async Task RequestQueueProcessingTask()
    {
        await Task.CompletedTask;

        while (true)
        {
            while (PriorityMessageQueue.TryDequeue(out ServiceRequestItem priorityRequest))
                HandleNextRequest(priorityRequest);

            if (!MessageQueue.TryDequeue(out ServiceRequestItem normalRequest))
                break;

            HandleNextRequest(normalRequest);

            void HandleNextRequest(ServiceRequestItem nextRequest)
            {
                if (!ServiceLookup.TryGetValue(nextRequest.ServiceCode, out var serviceName))
                {
                    EnqueueResponse(nextRequest, ForbiddenMsgBytes);
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
                    EnqueueResponse(nextRequest, ForbiddenMsgBytes);
                    return;
                }

                var fireAndForget = nextRequest.Flags.HasFlag(MessageFlags.FireAndForget);
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
            if (!request.IsBinary)
                requestMsg.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            requestMsg.Content = request.Content;

            var serviceResponse = await HttpClient.SendAsync(requestMsg, Program.ShutdownCancellationTokenSource.Token);
            if (serviceResponse.IsSuccessStatusCode)
            {
                if (serviceResponse.Content != null)
                {
                    var response = await serviceResponse.Content.ReadAsByteArrayAsync();
                    if (response != null)
                    {
                        EnqueueResponse(request, response);
                        return;
                    }
                }

                EnqueueResponse(request, OKMsgBytes, true);
            }
        }
        catch (HttpRequestException exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"An HTTP failure occurred executing action API method on service [{serviceName}].");
            switch (exc.StatusCode)
            {
                case System.Net.HttpStatusCode.Unauthorized:
                case System.Net.HttpStatusCode.Forbidden:
                    EnqueueResponse(request.MessageID, ForbiddenMsgBytes);
                    break;
                case System.Net.HttpStatusCode.NotFound:
                    if (Configuration.Obfuscate_Not_Found_Response)
                        EnqueueResponse(request.MessageID, ForbiddenMsgBytes);
                    else
                        EnqueueResponse(request.MessageID, NotFoundMsgBytes);
                    break;
                case System.Net.HttpStatusCode.BadRequest:
                    EnqueueResponse(request.MessageID, BadRequestMsgBytes);
                    break;
                case System.Net.HttpStatusCode.InternalServerError:
                default:
                    EnqueueResponse(request.MessageID, ServerErrorMsgBytes);
                    break;
            }
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"An exception occurred executing action API method on service [{serviceName}].");
            EnqueueResponse(request.MessageID, ServerErrorMsgBytes);
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
            if (!request.IsBinary)
                requestMsg.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            requestMsg.Content = request.Content;

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
    private void EnqueueServiceRequest(MessageFlags messageFlags, uint messageID, uint serviceCode, ushort messageChannel, ByteArrayContent messageContent, bool isBinary)
    {
        var queueWasEmpty = false;
        var newRequestQueueItem = new ServiceRequestItem()
        {
            IsBinary = isBinary,
            Flags = messageFlags,
            ServiceCode = serviceCode,
            MessageID = messageID,
            Content = messageContent
        };

        if (messageChannel != 0)
        {
            // enqueue to appropriate channel queue
            _ = ChannelMessageQueue.AddOrUpdate(messageChannel,
                (channel) =>
                {
                    var newRequestQueue = new ConcurrentQueue<ServiceRequestItem>();
                    newRequestQueue.Enqueue(newRequestQueueItem);

                    // start processing task
                    _ = Task.Run(() => ChannelQueueProcessingTask(messageChannel));

                    return newRequestQueue;
                },
                (channel, channelQueue) => // update
                {
                    queueWasEmpty = channelQueue.IsEmpty;
                    channelQueue.Enqueue(newRequestQueueItem);

                    // start new processing task, if needed
                    if (queueWasEmpty)
                        _ = Task.Run(() => ChannelQueueProcessingTask(messageChannel));

                    return channelQueue;
                });

            return;
        }

        // enqueue as high-priority request
        if (messageFlags.HasFlag(MessageFlags.HighPriority))
        {
            queueWasEmpty = PriorityMessageQueue.IsEmpty;
            PriorityMessageQueue.Enqueue(newRequestQueueItem);
            if (queueWasEmpty)
                _ = Task.Run(RequestQueueProcessingTask);
        }

        // enqueue as typical request
        queueWasEmpty = MessageQueue.IsEmpty;
        MessageQueue.Enqueue(newRequestQueueItem);
        if (queueWasEmpty)
            _ = Task.Run(RequestQueueProcessingTask);
    }

    /// <summary>
    /// Enqueues an event to be fired down to the client.
    /// 
    /// This is effectively the same as a response to a client-originating request,
    /// but uses a messageID of the uint/4-byte max value.
    /// 
    /// This indicates to the client that this is an event, and not an API response.
    /// Any more information will require deserializing the event on the client side,
    /// which will depend on if it's binary or text, etc.
    /// </summary>
    private bool EnqueueEvent(byte[] content, bool isBinary = true)
    {
        try
        {
            ResponseQueue.Enqueue(new()
            {
                IsEvent = true,
                IsBinary = isBinary,
                MessageID = uint.MaxValue,
                Data = content,
            });

            return true;
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"An exception occurred enqueuing an event to be sent to the client.");
        }

        return false;
    }

    /// <summary>
    /// Enqueue a response that should be fed back to the client for a given message.
    /// Uses the messageID, and requires specifying the binary/text content type.
    /// 
    /// This method allows for responses where the original request is lost, or before
    /// the request item is create, otherwise, use the version that takes the request
    /// as a parameter.
    /// </summary>
    private bool EnqueueResponse(uint messageID, byte[] content, bool isBinary = true)
    {
        try
        {
            ResponseQueue.Enqueue(new()
            {
                IsBinary = isBinary,
                MessageID = messageID,
                Data = content,
            });

            return true;
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"An exception occurred enqueuing a response to be sent back to the client.");
        }

        return false;
    }

    /// <summary>
    /// Enqueue a response that should be fed back to the client for a given message.
    /// Uses the message object itself to simplify things.
    /// 
    /// Can override the IsBinary of the request, if sending back a binary error code.
    /// </summary>
    private bool EnqueueResponse(ServiceRequestItem requestItem, byte[] content, bool? isBinary = null)
    {
        try
        {
            ResponseQueue.Enqueue(new()
            {
                IsBinary = isBinary != null ? isBinary.Value : requestItem.IsBinary,
                MessageID = requestItem.MessageID,
                Data = content,
            });

            return true;
        }
        catch (Exception exc)
        {
            Program.Logger.Log(LogLevel.Error, exc, $"An exception occurred enqueuing a response to be sent back to the client.");
        }

        return false;
    }
}
