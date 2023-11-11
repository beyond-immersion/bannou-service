# WebSocket Message Structure and Handling for Beyond-Immersion System

The Beyond-Immersion system uses a structured format for WebSocket messages to efficiently route and handle requests between clients and various backend services. This document outlines the structure of the WebSocket messages and explains how to use the different flags and fields for effective communication.

## Message Structure

Each message sent over the WebSocket connection comprises several distinct blocks:

1. **Message Flags (1 byte):**
   - The first byte of the message is reserved for various flags that dictate how the message should be handled.
2. **Service Identifier (4 bytes):**
   - The next four bytes represent the CRC32-hashed form of the service name, determining the target service for the request.
3. **Payload (up to 8 KB):**
   - The remainder of the message (up to 8 KB) is a JSON string payload that the target service will receive.

## Endianness Consideration

- The **service identifier** portion of the message must be handled with endianness in mind:
  - The system uses **Big-Endian (Network Order)** for the 4-byte service identifier.
  - Clients on **Little-Endian systems** must reverse the byte order of the service identifier before sending the message.
  - This ensures consistent interpretation of the identifier across different system architectures.

## Message Flags

Each bit in the Message Flags byte represents a different flag:

- `None` (0x00): No special handling.
- `AckRequested` (0x01): The client expects an acknowledgment or response for this message.
- `HighPriority` (0x02): The message should be processed with high priority.
- `Async` (0x04): The message and its response should be handled asynchronously.
- `Encrypted` (0x08): The message payload is encrypted.
- `Compressed` (0x10): The message payload is compressed.

### Flag Descriptions

- **None:**
  - Standard message with no special handling required.
- **AckRequested:**
  - When set, the client expects a response to this message.
  - If combined with `Async`, the response is handled asynchronously.
- **HighPriority:**
  - High-priority messages are processed before others, regardless of their arrival order.
  - This flag applies to all handling patterns (fire-and-forget, synchronous, and asynchronous response).
- **Async:**
  - Indicates that the message should be processed in a separate task/thread.
  - Responses are queued and sent back to the client independently of the message processing order.
- **Encrypted:**
  - The payload is encrypted and needs to be decrypted by the server before processing.
- **Compressed:**
  - The payload is compressed and must be decompressed by the server before processing.

## Service Identifier

- The Service Identifier is a 4-byte unsigned integer representing the CRC32 hash of the service name.
- It is crucial for clients to send the identifier in Big-Endian format:
  - For Little-Endian systems, reverse the byte order of the identifier before attaching it to the message.
- This identifier is used to route the message to the appropriate backend service.

## Payload

- The payload is a JSON string containing the data to be processed by the service.
- The maximum length of the payload is 8 KB.

## Message Handling Patterns

1. **Fire-and-Forget:**
   - Messages are sent without expecting a response.
   - Suitable for operations where the outcome doesn't need to be known immediately.
2. **Wait-for-Response (Synchronous):**
   - The client waits for a response for a configurable timeout (default: 5 seconds).
   - Messages are processed sequentially.
3. **Asynchronous Response:**
   - Each message is handled in its own task/thread.
   - Responses are queued and sent back to the client as they are processed.

## Client Considerations

- Clients must adhere to the message structure for successful communication.
- Clients should choose the appropriate message handling pattern based on their capabilities and requirements.
- For synchronous handling, clients should be prepared to handle response timeouts.

## Example Message (Hex Representation)

- Message Flags: `02`
- Service Identifier: `3F4A85B2` (Big-Endian format)
- Payload: `{"data": "example"}` (UTF-8)
