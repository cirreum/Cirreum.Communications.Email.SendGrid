namespace Cirreum.Communications.Email.Health;

using Cirreum.Communications.Email.Configuration;
using Cirreum.ServiceProvider.Health;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Diagnostics.HealthChecks;

/// <summary>
/// Health check implementation for SendGrid email services.
/// Validates configuration settings and optionally tests API connectivity to ensure
/// the SendGrid service is operational and properly configured.
/// </summary>
/// <remarks>
/// <para>
/// This health check performs multi-level validation:
/// </para>
/// <list type="bullet">
/// <item><description>Configuration validation (API key, email addresses, settings)</description></item>
/// <item><description>Optional API connectivity testing through actual email service calls</description></item>
/// <item><description>Environment-specific checks (sandbox mode in production, etc.)</description></item>
/// </list>
/// <para>
/// Results are cached to avoid excessive API calls during health check polling.
/// Cache duration is configurable and varies between successful and failed checks.
/// </para>
/// </remarks>
public sealed class SendGridEmailHealthCheck
	: IServiceProviderHealthCheck<SendGridEmailHealthCheckOptions>
	, IDisposable {

	private readonly IEmailService _emailService;
	private readonly bool _isProduction;
	private readonly IMemoryCache _memoryCache;
	private readonly SendGridEmailInstanceSettings _settings;
	private readonly SendGridEmailHealthCheckOptions _options;
	private readonly string _cacheKey;
	private readonly TimeSpan _cacheDuration;
	private readonly TimeSpan _failureCacheDuration;
	private readonly bool _cacheDisabled;
	private readonly SemaphoreSlim _semaphore = new(1, 1);

	/// <summary>
	/// Initializes a new instance of the <see cref="SendGridEmailHealthCheck"/> class.
	/// </summary>
	/// <param name="emailService">The SendGrid email service instance to be health checked.</param>
	/// <param name="settings">Configuration settings for the SendGrid email service instance.</param>
	/// <param name="memoryCache">Memory cache for storing health check results to avoid excessive API calls.</param>
	/// <param name="isProduction">Indicates whether the application is running in a production environment. Defaults to false.</param>
	/// <exception cref="ArgumentException">Thrown when the service name in settings is null or whitespace.</exception>
	/// <remarks>
	/// The health check behavior varies based on the production environment flag:
	/// <list type="bullet">
	/// <item><description>In production: More strict validation (e.g., sandbox mode warnings)</description></item>
	/// <item><description>In development: More permissive settings for testing scenarios</description></item>
	/// </list>
	/// </remarks>
	public SendGridEmailHealthCheck(
		IEmailService emailService,
		SendGridEmailInstanceSettings settings,
		IMemoryCache memoryCache,
		bool isProduction = false) {

		this._emailService = emailService;
		this._isProduction = isProduction;
		this._memoryCache = memoryCache;
		this._settings = settings;
		this._options = settings.HealthOptions ?? new();

		ArgumentException.ThrowIfNullOrWhiteSpace(this._settings.Name);

		this._cacheKey = $"_sendgrid_email_health_{this._settings.Name.ToLowerInvariant()}";
		this._cacheDuration = this._options.CachedResultTimeout ?? TimeSpan.FromSeconds(60);
		this._failureCacheDuration = TimeSpan.FromSeconds(Math.Max(35, (this._options.CachedResultTimeout ?? TimeSpan.FromSeconds(60)).TotalSeconds / 2));
		this._cacheDisabled = (this._options.CachedResultTimeout is null || this._options.CachedResultTimeout.Value.TotalSeconds == 0);

	}

	/// <summary>
	/// Performs the health check operation for the SendGrid email service.
	/// </summary>
	/// <param name="context">The health check context containing registration and other details.</param>
	/// <param name="cancellationToken">A cancellation token to cancel the health check operation.</param>
	/// <returns>
	/// A task that represents the asynchronous health check operation, containing a <see cref="HealthCheckResult"/>
	/// indicating the health status of the SendGrid service.
	/// </returns>
	/// <remarks>
	/// <para>The health check process includes:</para>
	/// <list type="number">
	/// <item><description>Check for cached results to avoid repeated API calls</description></item>
	/// <item><description>Validate configuration settings (API key, email addresses, limits)</description></item>
	/// <item><description>Optionally test API connectivity if enabled in options</description></item>
	/// <item><description>Cache the results with appropriate expiration times</description></item>
	/// </list>
	/// <para>
	/// Results are cached with different durations for healthy vs unhealthy states to balance
	/// responsiveness with API rate limiting considerations.
	/// </para>
	/// </remarks>
	public async Task<HealthCheckResult> CheckHealthAsync(
		HealthCheckContext context,
		CancellationToken cancellationToken = default) {

		if (this._cacheDisabled) {
			// No caching...
			return await this.CheckSendGridEmailHealthAsync(context, cancellationToken)
				.ConfigureAwait(false);
		}

		// Try get from cache first
		if (this._memoryCache.TryGetValue(this._cacheKey, out HealthCheckResult cachedResult)) {
			return cachedResult;
		}

		// If not in cache, ensure only one thread updates it
		try {

			await this._semaphore.WaitAsync(cancellationToken);

			// Double-check after acquiring semaphore
			if (this._memoryCache.TryGetValue(this._cacheKey, out cachedResult)) {
				return cachedResult;
			}

			// Perform actual health check
			var result = await this.CheckSendGridEmailHealthAsync(context, cancellationToken)
				.ConfigureAwait(false);

			// Cache with appropriate duration based on health status
			var jitter = TimeSpan.FromSeconds(Random.Shared.Next(0, 5));
			var duration = result.Status == HealthStatus.Healthy
				? this._cacheDuration
				: this._failureCacheDuration;

			return this._memoryCache.Set(this._cacheKey, result, duration + jitter);

		} finally {
			this._semaphore.Release();
		}
	}

	private async Task<HealthCheckResult> CheckSendGridEmailHealthAsync(
		HealthCheckContext context,
		CancellationToken cancellationToken) {

		// Basic configuration checks
		var configResult = this.CheckConfiguration();
		if (configResult != null) {
			return configResult.Value;
		}

		if (!this._options.TestApiConnectivity) {
			return HealthCheckResult.Healthy("SendGrid Email service configuration validated, API connectivity test disabled");
		}

		try {

			// Create a test email message
			var testMessage = new EmailMessage {
				From = this._settings.DefaultFrom,
				To = [new EmailAddress(this._options.TestEmailAddress, "HealthCheckUser")],
				Subject = "SendGrid Health Check Test",
				TextContent = "This is a health check validation message that tests SendGrid configuration.",
				Priority = EmailPriority.Low
			};

			// Use validation-only mode to test the service without sending
			var result = await this._emailService.SendEmailAsync(testMessage, cancellationToken);
			if (result.Success) {
				if (this._isProduction) {
					return HealthCheckResult.Healthy("SendGrid Email service is operational");
				}
				var healthyMessage = $"SendGrid Email service successfully sent a test email to '{this._options.TestEmailAddress}'";
				return HealthCheckResult.Healthy(healthyMessage);
			}

			var errorMsg = $"SendGrid Email sending test failed - {result.ErrorMessage}";
			return new HealthCheckResult(context.Registration.FailureStatus, errorMsg);

		} catch (HttpRequestException httpEx) {
			// Network connectivity issues
			return new HealthCheckResult(
				HealthStatus.Degraded,
				$"SendGrid connectivity issue: {httpEx.Message}");
		} catch (Exception ex) {
			return new HealthCheckResult(
				context.Registration.FailureStatus,
				$"SendGrid Email health check failed: {ex.Message}",
				ex);
		}

	}

	private HealthCheckResult? CheckConfiguration() {

		var issues = new List<string>();

		// Check API key
		if (string.IsNullOrWhiteSpace(this._settings.ApiKey)) {
			issues.Add("API key is not configured");
		}

		// Check test email address
		if (string.IsNullOrWhiteSpace(this._options.TestEmailAddress)
			|| !EmailAddress.IsValidEmailAddress(this._options.TestEmailAddress)) {
			issues.Add("Invalid or missing health check test email address.");
		}

		// Check sandbox mode in production
		if (this._isProduction && this._settings.SandboxMode) {
			issues.Add("Sandbox mode is enabled in production environment");
		}

		// Check batch size limits
		if (this._settings.BulkOptions.MaxBatchSize <= 0 || this._settings.BulkOptions.MaxBatchSize > 1000) {
			issues.Add($"Invalid batch size: {this._settings.BulkOptions.MaxBatchSize} (should be 1-1000)");
		}

		if (issues.Count > 0) {
			var severity = this._isProduction && this._settings.SandboxMode ? HealthStatus.Degraded : HealthStatus.Unhealthy;
			return new HealthCheckResult(severity, string.Join("; ", issues));
		}

		return null;

	}

	/// <summary>
	/// Releases the unmanaged resources used by the <see cref="SendGridEmailHealthCheck"/> and optionally releases the managed resources.
	/// </summary>
	/// <remarks>
	/// This implementation disposes the internal semaphore used for thread synchronization during health checks.
	/// </remarks>
	public void Dispose() {
		this._semaphore?.Dispose();
	}

}