# Cirreum.Communications.Email.SendGrid

[![NuGet Version](https://img.shields.io/nuget/v/Cirreum.Communications.Email.SendGrid.svg?style=flat-square&labelColor=1F1F1F&color=003D8F)](https://www.nuget.org/packages/Cirreum.Communications.Email.SendGrid/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Cirreum.Communications.Email.SendGrid.svg?style=flat-square&labelColor=1F1F1F&color=003D8F)](https://www.nuget.org/packages/Cirreum.Communications.Email.SendGrid/)
[![GitHub Release](https://img.shields.io/github/v/release/cirreum/Cirreum.Communications.Email.SendGrid?style=flat-square&labelColor=1F1F1F&color=FF3B2E)](https://github.com/cirreum/Cirreum.Communications.Email.SendGrid/releases)
[![License](https://img.shields.io/github/license/cirreum/Cirreum.Communications.Email.SendGrid?style=flat-square&labelColor=1F1F1F&color=F2F2F2)](https://github.com/cirreum/Cirreum.Communications.Email.SendGrid/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-003D8F?style=flat-square&labelColor=1F1F1F)](https://dotnet.microsoft.com/)

**SendGrid email service implementation for the Cirreum communications framework**

## Overview

**Cirreum.Communications.Email.SendGrid** provides a robust SendGrid email service implementation for the Cirreum communications framework. It implements the `IEmailService` interface with comprehensive features for reliable email delivery.

## Features

- **Single & Bulk Email Sending** - Send individual emails or bulk operations with shared templates or fully personalized messages
- **Retry Logic** - Exponential backoff with jitter for handling rate limits and transient failures
- **Template Support** - Dynamic template integration with friendly name mapping
- **Attachment Handling** - Support for inline and attachment dispositions with streams or byte arrays
- **Health Check Integration** - Built-in health monitoring for SendGrid API connectivity
- **Sandbox Mode** - Testing mode that processes emails without actual delivery
- **Comprehensive Validation** - Email address validation and content verification before sending
- **Dependency Injection** - Seamless integration with .NET hosting and service provider

## Getting Started

### Installation

```bash
dotnet add package Cirreum.Communications.Email.SendGrid
```

### Basic Usage

```csharp
// Register in Program.cs
builder.AddSendGridEmailClient("sendgrid", settings => {
    settings.ApiKey = "your-api-key";
    settings.DefaultFrom = new EmailAddress("noreply@yourcompany.com", "Your Company");
});

// Use in your service
public class NotificationService {
    private readonly IEmailService _emailService;
    
    public NotificationService([FromKeyedServices("sendgrid")] IEmailService emailService) {
        _emailService = emailService;
    }
    
    public async Task SendWelcomeEmailAsync(string to, string name) {
        var message = new EmailMessage {
            To = [new EmailAddress(to, name)],
            Subject = "Welcome!",
            HtmlContent = "<h1>Welcome to our service!</h1>"
        };
        
        var result = await _emailService.SendEmailAsync(message);
    }
}
```

### Bulk Sending

```csharp
// Shared template approach
var template = new EmailMessage {
    Subject = "Newsletter",
    HtmlContent = "<h1>Latest News</h1>"
};

var recipients = [
    new EmailAddress("user1@example.com", "User One"),
    new EmailAddress("user2@example.com", "User Two")
];

var response = await _emailService.SendBulkEmailAsync(template, recipients);
```

### Configuration Options

```csharp
builder.AddSendGridEmailClient("sendgrid", settings => {
    settings.ApiKey = "your-api-key";
    settings.DefaultFrom = new EmailAddress("noreply@yourcompany.com");
    settings.MaxRetries = 3;
    settings.SandboxMode = false;
    settings.BulkOptions.MaxBatchSize = 100;
    settings.BulkOptions.MaxConcurrency = 5;
    settings.TemplateMap = new Dictionary<string, string> {
        ["welcome"] = "d-abc123",
        ["reset-password"] = "d-def456"
    };
    settings.GlobalHeaders = new Dictionary<string, string> {
        ["X-Company"] = "YourCompany"
    };
    settings.GlobalCategories = ["transactional"];
});

## Advanced Features

### JSON Connection String Support

For secure configuration management (e.g., Azure Key Vault):

```csharp
var connectionJson = """
{
    "ApiKey": "SG.your-api-key",
    "DefaultFrom": {
        "Address": "noreply@yourcompany.com",
        "Name": "Your Company"
    }
}
""";

builder.AddSendGridEmailClient("sendgrid", connectionJson);
```

### Health Checks

The library includes built-in health checks that verify SendGrid API connectivity:

```csharp
builder.AddSendGridEmailClient("sendgrid", settings => {
    // ... other settings
}, healthOptions => {
    healthOptions.Enabled = true;
    healthOptions.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddHealthChecks()
    .AddCheck<SendGridEmailHealthCheck>("sendgrid-email");
```

### Template Usage

Use SendGrid dynamic templates with friendly names:

```csharp
var message = new EmailMessage {
    To = [new EmailAddress("user@example.com")],
    TemplateKey = "welcome", // Maps to template ID via TemplateMap
    TemplateData = new Dictionary<string, object> {
        ["firstName"] = "John",
        ["companyName"] = "Acme Corp"
    }
};
```

## Contribution Guidelines

1. **Be conservative with new abstractions**  
   The API surface must remain stable and meaningful.

2. **Limit dependency expansion**  
   Only add foundational, version-stable dependencies.

3. **Favor additive, non-breaking changes**  
   Breaking changes ripple through the entire ecosystem.

4. **Include thorough unit tests**  
   All primitives and patterns should be independently testable.

5. **Document architectural decisions**  
   Context and reasoning should be clear for future maintainers.

6. **Follow .NET conventions**  
   Use established patterns from Microsoft.Extensions.* libraries.

## Versioning

Cirreum.Communications.Email.SendGrid follows [Semantic Versioning](https://semver.org/):

- **Major** - Breaking API changes
- **Minor** - New features, backward compatible
- **Patch** - Bug fixes, backward compatible

Given its foundational role, major version bumps are rare and carefully considered.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

**Cirreum Foundation Framework**  
*Layered simplicity for modern .NET*