namespace Cirreum.Communications.Email.Health;

using Cirreum.Health;

/// <summary>
/// Configuration options for SendGrid email service health checks.
/// </summary>
public sealed class SendGridEmailHealthCheckOptions
	: ServiceProviderHealthCheckOptions {

	/// <summary>
	/// Gets or sets whether to test actual API connectivity.
	/// When false, only validates configuration without making API calls.
	/// </summary>
	public bool TestApiConnectivity { get; set; } = false;

	/// <summary>
	/// Gets or sets the test email address to use for health check validation.
	/// </summary>
	public string TestEmailAddress { get; set; } = "";

}