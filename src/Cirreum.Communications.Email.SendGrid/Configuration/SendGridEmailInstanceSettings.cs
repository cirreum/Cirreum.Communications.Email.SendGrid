namespace Cirreum.Communications.Email.Configuration;

using Cirreum.Communications.Email.Health;
using Cirreum.ServiceProvider.Configuration;
using System.Text.Json;

/// <summary>
/// Configuration settings for a SendGrid email service instance.
/// Provides comprehensive configuration options for SendGrid API integration including
/// authentication, default settings, batching, templating, and health monitoring.
/// </summary>
public sealed class SendGridEmailInstanceSettings
	: ServiceProviderInstanceSettings<SendGridEmailHealthCheckOptions> {

	/// <summary>
	/// Gets or sets the SendGrid API key used for authentication.
	/// This is required for all SendGrid API operations.
	/// </summary>
	/// <value>The API key as a string. Defaults to an empty string.</value>
	public string ApiKey { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the default sender email address used when no explicit sender is specified.
	/// This provides a fallback sender for outgoing emails.
	/// </summary>
	/// <value>An <see cref="EmailAddress"/> object containing the default sender information, or null if not set.</value>
	public EmailAddress DefaultFrom { get; set; }

	/// <summary>
	/// Gets or sets the maximum number of retry attempts for failed requests.
	/// Valid range: 0-10.
	/// </summary>
	private int _maxRetries = 3;
	public int MaxRetries {
		get => _maxRetries;
		set => _maxRetries = Math.Clamp(value, 0, 10);
	}

	/// <summary>
	/// Gets or sets a value indicating whether sandbox mode is enabled.
	/// When enabled, emails are processed but not actually delivered, useful for testing.
	/// </summary>
	/// <value>true if sandbox mode is enabled; otherwise, false. Defaults to false.</value>
	public bool SandboxMode { get; set; } = false;

	/// <summary>
	/// Gets or sets the bulk sending options.
	/// </summary>
	public SendGridEmailBulkSettings BulkOptions { get; set; } = new();

	/// <summary>
	/// Gets or sets a dictionary mapping template names to SendGrid template IDs.
	/// Allows for easy reference to SendGrid dynamic templates by friendly names.
	/// </summary>
	/// <value>A dictionary where keys are template names and values are SendGrid template IDs. Defaults to an empty dictionary.</value>
	public Dictionary<string, string> TemplateMap { get; set; } = [];

	/// <summary>
	/// Gets or sets a dictionary of global headers that will be added to all outgoing emails.
	/// These headers are applied automatically to every email sent through this instance.
	/// </summary>
	/// <value>A dictionary where keys are header names and values are header values. Defaults to an empty dictionary.</value>
	public Dictionary<string, string> GlobalHeaders { get; set; } = [];

	/// <summary>
	/// Gets or sets a list of global categories that will be applied to all outgoing emails.
	/// Categories are used for tracking and analytics in SendGrid.
	/// </summary>
	/// <value>A list of category names as strings. Defaults to an empty list.</value>
	public List<string> GlobalCategories { get; set; } = [];

	/// <summary>
	/// Gets or sets the health check options for monitoring the SendGrid service instance.
	/// Inherits from the base class and provides SendGrid-specific health monitoring configuration.
	/// </summary>
	/// <value>A <see cref="SendGridEmailHealthCheckOptions"/> object containing health check settings. Defaults to a new instance.</value>
	public override SendGridEmailHealthCheckOptions? HealthOptions { get; set; } = new();

	/// <summary>
	/// Parses a JSON connection string to populate the ApiKey and DefaultFrom properties.
	/// Allows KV/Env secret to be provided as JSON with ApiKey + DefaultFrom.
	/// Expected JSON format: { "ApiKey":"...", "DefaultFrom": {"Address":"x@y","Name":"Z"} }
	/// </summary>
	/// <param name="jsonValue">The JSON string containing the configuration data.</param>
	/// <exception cref="InvalidOperationException">
	/// Thrown when the JSON is invalid, missing required fields, or cannot be parsed.
	/// </exception>
	/// <exception cref="JsonException">
	/// Thrown when the JSON format is malformed and cannot be deserialized.
	/// </exception>
	/// <remarks>
	/// This method sets the ConnectionString property and then attempts to parse the JSON.
	/// If DefaultFrom is already set, it will not be overwritten by the parsed value.
	/// The ApiKey field is required and will throw an exception if missing or null.
	/// </remarks>
	public override void ParseConnectionString(string jsonValue) {

		this.ConnectionString = jsonValue;

		try {

			var keyVaultOptions =
				JsonSerializer.Deserialize<SendGridConnectionData>(jsonValue, JsonSerializerOptions.Web)
				?? throw new InvalidOperationException("Invalid SendGrid configuration JSON.");

			this.ApiKey = keyVaultOptions.ApiKey ?? throw new InvalidOperationException("Missing SendGrid ApiKey");

			// local appsettings takes precedence over KeyVault
			if (string.IsNullOrWhiteSpace(this.DefaultFrom.Address)
				&& !string.IsNullOrWhiteSpace(keyVaultOptions.DefaultFrom?.Address)) {
				this.DefaultFrom = keyVaultOptions.DefaultFrom.Value;
			}

		} catch (JsonException ex) {
			throw new InvalidOperationException("Invalid SendGrid configuration format.", ex);
		}

	}

	/// <summary>
	/// Internal record used for deserializing connection string JSON data.
	/// Contains the essential configuration properties that can be provided via connection string.
	/// </summary>
	/// <param name="ApiKey">The SendGrid API key for authentication.</param>
	/// <param name="DefaultFrom">The default sender email address, or null if not specified.</param>
	private sealed record SendGridConnectionData(
		string? ApiKey,
		EmailAddress? DefaultFrom
	);

}