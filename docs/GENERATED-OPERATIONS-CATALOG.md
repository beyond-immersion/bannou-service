# Generated Operations Catalog

> **Source**: `docs/operations/*.md`
> **Do not edit manually** - regenerate with `make generate-docs`

Operations, deployment, testing, and CI/CD documentation.

## Deployment Guide {#deployment}

[Full Document](operations/DEPLOYMENT.md)

This guide covers deploying Bannou from local development to production environments.

## GitHub Actions CI/CD {#github-actions}

[Full Document](operations/GITHUB-ACTIONS.md)

This document describes Bannou's CI/CD pipeline implemented with GitHub Actions.

## EditorConfig and Linting Guide {#linting}

[Full Document](operations/LINTING.md)

- **Problem**: CI uses strict EditorConfig validation beyond `dotnet format`
- **Solution**: New `make format-strict` and `make lint-editorconfig` commands
- **Workflow**: Use `make validate` before pushing
- **Diagnosis**: Use `make lint-editorconfig-fast` to find issues quickly

## NuGet Package Setup for Bannou SDKs {#nuget-setup}

[Full Document](operations/NUGET-SETUP.md)

Bannou publishes **multiple SDK packages** to NuGet for different use cases. For the complete package registry and naming conventions, see [SDK Conventions](../../sdks/CONVENTIONS.md).

## Release Process Guide {#releasing}

[Full Document](operations/RELEASING.md)

This guide documents the versioning and release process for Bannou.

## Bannou Testing Documentation {#testing}

[Full Document](operations/TESTING.md)

Comprehensive testing documentation for Bannou's schema-driven microservices architecture with WebSocket-first edge gateway and CI/CD integration.

## Summary

- **Documents in catalog**: 6

---

*This file is auto-generated. See [TENETS.md](reference/TENETS.md) for architectural context.*
