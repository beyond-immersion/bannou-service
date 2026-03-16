// This file intentionally left as a marker. All broadcast service tests have been
// organized into domain-specific test classes:
//
// - BroadcastServiceConstructorTests.cs  — Constructor validation
// - BroadcastServicePlatformTests.cs     — Platform linking (LinkPlatform, UnlinkPlatform, ListPlatforms, PlatformCallback)
// - BroadcastServiceSessionTests.cs      — Sessions (StartSession, StopSession, AssociateSession, GetSessionStatus, ListSessions)
// - BroadcastServiceOutputTests.cs       — Camera + Output (AnnounceCamera, RetireCamera, StartOutput, StopOutput, UpdateOutput, GetOutputStatus, ListOutputs)
// - BroadcastServiceAdminTests.cs        — Admin + Cleanup (GetLatestPulse, TestSentiment, CleanupByAccount)
//
// See: docs/reference/tenets/TESTING-PATTERNS.md
