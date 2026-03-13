# Device Info Standard

> **Scope**: Auth service session creation, event publishing, analytics consumption
> **Source of Truth**: `schemas/auth-api.yaml` → `DeviceInfo` model
> **Last Updated**: 2026-03-13

## Overview

DeviceInfo is a **client-reported** structured model that captures device identification data during authentication. It is populated by Bannou SDKs (not parsed from User-Agent strings) and designed to cover PCs, mobile devices, tablets, game consoles, and handheld gaming devices.

The auth service stores DeviceInfo opaquely in session data and publishes it in authentication events. It does **not** inspect, validate, or enforce any DeviceInfo fields beyond schema validation (type, maxLength). Analytics and other downstream consumers use the event data for their own purposes.

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Collection method | Client-reported via SDK | Native game clients have no User-Agent; SDK has direct access to `SystemInfo`, platform APIs |
| Platform values | Closed enum | Prevents fragmentation from free-form strings; `Other` covers edge cases |
| DeviceType values | Closed enum with `Handheld` | Steam Deck / Switch are distinct from docked consoles and phones |
| Metadata field | `additionalProperties: string` | T29-compliant pass-through; service stores/publishes but never inspects |
| IP address / geolocation | Out of scope | Server-derived, requires proxy-aware extraction and GeoIP — separate security concern |

## Enums

### DeviceType

Describes the physical form factor of the device.

| Value | Description | Examples |
|-------|-------------|----------|
| `Desktop` | Desktop or laptop computer | Windows PC, MacBook, Linux workstation |
| `Mobile` | Mobile phone | iPhone, Android phone |
| `Tablet` | Tablet device | iPad, Android tablet |
| `Console` | Stationary game console | PlayStation 5, Xbox Series X |
| `Handheld` | Handheld gaming device | Nintendo Switch, Steam Deck |
| `Unknown` | Cannot be determined | Fallback when detection fails |

### Platform

Describes the operating system or runtime platform.

| Value | Description |
|-------|-------------|
| `Windows` | Microsoft Windows |
| `MacOS` | Apple macOS |
| `Linux` | Linux distributions |
| `iOS` | Apple iOS (iPhone, iPad) |
| `Android` | Android OS |
| `PlayStation` | Sony PlayStation (any generation) |
| `Xbox` | Microsoft Xbox (any generation) |
| `NintendoSwitch` | Nintendo Switch |
| `WebGL` | Browser-based WebGL runtime |
| `Other` | Platform not listed above |

## DeviceInfo Model

All fields are **nullable** — the entire DeviceInfo object is itself nullable on all requests that accept it. SDKs should populate what they can; omitted fields are stored as null.

| Field | Type | MaxLength | Description |
|-------|------|-----------|-------------|
| `deviceType` | `DeviceType?` | — | Physical form factor enum |
| `platform` | `Platform?` | — | OS/runtime platform enum |
| `osVersion` | `string?` | 100 | OS version string (e.g., `"Windows 11 23H2"`, `"iOS 17.4"`) |
| `deviceManufacturer` | `string?` | 200 | Hardware manufacturer (e.g., `"Apple"`, `"Sony"`, `"Valve"`) |
| `deviceModel` | `string?` | 200 | Hardware model (e.g., `"iPhone 15 Pro"`, `"Steam Deck OLED"`) |
| `appVersion` | `string?` | 100 | Game/application version string |
| `sdkVersion` | `string?` | 100 | Bannou SDK version string |
| `engineName` | `string?` | 100 | Game engine name (e.g., `"Unity"`, `"Unreal"`, `"Godot"`, `"Stride"`) |
| `engineVersion` | `string?` | 100 | Game engine version string |
| `metadata` | `object?` | — | Key-value pairs (string values only) for SDK-specific data not covered by standard fields |

## Where DeviceInfo Appears

### Request Models

| Request | Field | Notes |
|---------|-------|-------|
| `LoginRequest` | `deviceInfo?` | Standard email/password login |
| `RegisterRequest` | `deviceInfo?` | New account registration |
| `CompleteOAuthRequest` | `deviceInfo?` | OAuth provider completion |
| `VerifySteamAuthRequest` | `deviceInfo?` | Steam authentication |
| `RefreshRequest` | `deviceInfo?` | Token refresh — if provided, replaces session DeviceInfo entirely; if null, session gets null DeviceInfo |
| `VerifyMfaRequest` | — | Not accepted; DeviceInfo preserved from login via MFA challenge Redis data |

### Session Storage

DeviceInfo is stored in `SessionDataModel` in Redis alongside session data. It is returned in session listing responses (`SessionInfo.DeviceInfo`).

### Published Events

DeviceInfo is included in all **success** authentication events:

| Event | Field |
|-------|-------|
| `AuthLoginSuccessfulEvent` | `deviceInfo?` |
| `AuthRegistrationSuccessfulEvent` | `deviceInfo?` |
| `AuthOAuthLoginSuccessfulEvent` | `deviceInfo?` |
| `AuthSteamLoginSuccessfulEvent` | `deviceInfo?` |
| `AuthMfaVerifiedEvent` | `deviceInfo?` |

Failed authentication events (`AuthLoginFailedEvent`) do **not** include DeviceInfo — failed logins do not create sessions.

## SDK Implementation Guide

Each Bannou SDK should populate DeviceInfo automatically during authentication. The SDK is responsible for detecting device characteristics using platform-appropriate APIs.

### Detection Sources by Platform

| Platform | DeviceType Source | Model/Manufacturer Source |
|----------|-------------------|---------------------------|
| Unity | `SystemInfo.deviceType` | `SystemInfo.deviceModel` |
| Unreal | `FPlatformProperties` | `FPlatformMisc::GetDeviceMakeAndModel()` |
| .NET (Stride/Godot) | Runtime detection | `System.Runtime.InteropServices.RuntimeInformation` |
| Web/TypeScript | `navigator.userAgent` (limited) | `navigator.platform` |

### Metadata Usage

The `metadata` field accepts arbitrary string key-value pairs for data not covered by standard fields. Examples:

```json
{
  "gpu": "NVIDIA RTX 4090",
  "screenResolution": "3840x2160",
  "totalMemoryMB": "32768",
  "steamDeckModel": "OLED"
}
```

SDKs should document which metadata keys they populate. The auth service stores and publishes metadata unchanged — it is never inspected server-side (per T29).

## MFA Flow

When a login triggers MFA, DeviceInfo from the original login request is stashed in the MFA challenge Redis data (`MfaChallengeData.DeviceInfo`) with the challenge TTL. When the user completes MFA verification, the stashed DeviceInfo is retrieved and used for session creation and event publishing. This ensures DeviceInfo is preserved across the MFA challenge/verify round-trip without requiring the client to resend it.
