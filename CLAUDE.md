# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a .NET 10.0 library that provides SendGrid email functionality for the Cirreum communications framework. It implements the `IEmailService` interface and provides:

- Single email sending with validation and retry logic
- Bulk email sending (shared template and personalized messages) 
- Health check integration
- Hosting extensions for dependency injection
- Template mapping support
- Attachment handling (inline/attachment disposition)
- Comprehensive error handling with exponential backoff

## Commands

### Build
```bash
dotnet restore Cirreum.Communications.Email.SendGrid.slnx
dotnet build Cirreum.Communications.Email.SendGrid.slnx --configuration Release --no-restore
```

### Pack (for NuGet)
```bash
dotnet pack Cirreum.Communications.Email.SendGrid.slnx --configuration Release --no-build --output ./artifacts
```

### Solution Structure
- Use `Cirreum.Communications.Email.SendGrid.slnx` for all dotnet commands (not traditional .sln)
- Main project: `src/Cirreum.Communications.Email.SendGrid/Cirreum.Communications.Email.SendGrid.csproj`

## Architecture

### Core Components
- **SendGridEmailService**: Main service implementing `IEmailService` with retry logic and bulk operations
- **SendGridEmailRegistrar**: Handles service registration and configuration
- **Configuration classes**: Settings for instances, bulk operations, and health checks
- **HostingExtensions**: Extension methods for `IHostApplicationBuilder` registration

### Key Features
- **Bulk sending**: Two approaches - shared template with multiple recipients, or fully personalized messages
- **Retry mechanism**: Exponential backoff with jitter for 429/5xx responses
- **Validation**: Comprehensive email validation before sending
- **Template support**: Template ID mapping via `TemplateMap` configuration
- **Health checks**: Built-in SendGrid health check support
- **Sandbox mode**: Toggle for testing without actual email delivery

### Dependencies
- SendGrid 9.29.3
- Cirreum.Communications.Email 1.0.107 (provides base interfaces/types)
- Cirreum.ServiceProvider 1.0.2

### Configuration Structure
- Global configuration via `SendGridEmailSettings` 
- Instance-specific via `SendGridEmailInstanceSettings`
- Bulk operation settings in `SendGridEmailBulkSettings`
- Health check options in `SendGridEmailHealthCheckOptions`

The service follows the Cirreum Foundation Framework conventions with layered configuration, dependency injection patterns, and comprehensive error handling.