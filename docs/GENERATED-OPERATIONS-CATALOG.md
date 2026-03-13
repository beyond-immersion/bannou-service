# Generated Operations Catalog

> **Source**: `docs/operations/*.md`
> **Do not edit manually** - regenerate with `make generate-docs`

Operations, deployment, testing, and CI/CD documentation.

## Deployment Guide {#deployment}

**Last Updated**: 2026-03-08 | **Scope**: Deploying Bannou from local development through external testing to production, including Docker Compose, Orchestrator presets, OpenResty edge proxy, and environment configuration. | [Full Document](operations/DEPLOYMENT.md)

Deployment procedures for Bannou covering local development with Docker Compose, external client testing with OpenResty SSL termination, Orchestrator-managed distributed topologies, and production deployment patterns. Includes environment variable configuration, port allocation, health monitoring, and troubleshooting procedures. Reference when setting up new environments, configuring external access, or debugging deployment issues.

## GitHub Actions CI/CD {#github-actions}

**Last Updated**: 2026-03-08 | **Scope**: GitHub Actions CI/CD workflows, integration pipeline stages, SDK publishing, and reusable actions | [Full Document](operations/GITHUB-ACTIONS.md)

GitHub Actions CI/CD pipeline configuration covering the integration testing pipeline, unit test and lint workflows, SDK preview and stable release publishing, and reusable composite actions. Reference when investigating CI failures, adding new test stages, configuring SDK releases, or understanding workflow triggers and sequencing.

## EditorConfig and Linting Guide {#linting}

**Last Updated**: 2026-03-08 | **Scope**: EditorConfig validation, linting commands, and CI compliance for all project files | [Full Document](operations/LINTING.md)

EditorConfig validation and linting procedures for ensuring CI compliance across all project files. Covers the relationship between dotnet format (C# code style) and editorconfig-checker (text formatting: indentation, line endings, final newlines), available Makefile commands for local validation and fixing, and troubleshooting CI failures. Required reading when CI lint checks fail or before pushing changes that touch formatting.

## NuGet Package Setup for Bannou SDKs {#nuget-setup}

**Last Updated**: 2026-03-08 | **Scope**: NuGet package publishing configuration, SDK architecture, GitHub Actions CI workflows, and version management for all Bannou SDK packages | [Full Document](operations/NUGET-SETUP.md)

NuGet publishing setup for the Bannou SDK ecosystem covering package architecture, GitHub environment secrets, version management via SDK_VERSION file and PR labels, and CI/CD workflows for preview and stable releases. Required reading when configuring NuGet API keys, adding new SDK packages to the publish pipeline, or understanding how SDK versioning works across the three GitHub Actions workflows (auto-preview, manual preview, stable release).

## Release Process Guide {#releasing}

**Last Updated**: 2026-03-08 | **Scope**: Versioning, changelog management, and release automation for platform and SDK releases | [Full Document](operations/RELEASING.md)

Release procedures for the Bannou platform and SDKs covering two independent version tracks: platform releases (triggered by VERSION file changes on master) and SDK releases (triggered by PR labels and manual workflow dispatch). Covers semantic versioning guidelines, changelog maintenance, the prepare-release and release-commit Makefile workflow, TypeScript and Unreal SDK generation, and troubleshooting for common CI release failures.

## Bannou Testing Documentation {#testing}

**Last Updated**: 2026-03-13 | **Scope**: Test commands, CI/CD pipeline integration, Docker Compose test configurations, and testing workflows for all tiers (unit, HTTP integration, WebSocket edge, infrastructure). | [Full Document](operations/TESTING.md)

User-facing testing operations document covering test commands, CI/CD pipeline integration, Docker Compose configurations, and development workflows. Covers how to run core framework tests (bannou-service.tests), per-plugin unit tests (lib-*.tests), HTTP integration tests (http-tester), WebSocket edge tests (edge-tester), and infrastructure tests (lib-testing).

## Summary

- **Documents in catalog**: 6

---

*This file is auto-generated. See [TENETS.md](reference/TENETS.md) for architectural context.*
