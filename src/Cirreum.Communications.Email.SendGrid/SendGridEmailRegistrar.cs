namespace Cirreum.Communications.Email;

using Cirreum.Communications.Email.Configuration;
using Cirreum.Communications.Email.Health;
using Cirreum.Providers;
using Cirreum.ServiceProvider;
using Cirreum.ServiceProvider.Health;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

/// <summary>
/// Registers and configures SendGrid email services within the application's dependency injection container.
/// </summary>
/// <remarks>This class is responsible for integrating SendGrid email services into the application, including
/// validating configuration settings, adding service instances, and creating health checks. It supports multiple
/// service instances, each identified by a unique service key.</remarks>
public sealed class SendGridEmailRegistrar() :
	ServiceProviderRegistrar<
		SendGridEmailSettings,
		SendGridEmailInstanceSettings,
		SendGridEmailHealthCheckOptions> {

	/// <inheritdoc/>
	public override ProviderType ProviderType => ProviderType.Communications;

	/// <inheritdoc/>
	public override string ProviderName => "SendGrid";

	/// <inheritdoc/>
	public override string[] ActivitySourceNames { get; } = ["SendGrid.*"];

	/// <inheritdoc/>
	public override void ValidateSettings(SendGridEmailInstanceSettings settings) {
		if (string.IsNullOrWhiteSpace(settings.ApiKey) && string.IsNullOrWhiteSpace(settings.ConnectionString)) {
			throw new InvalidOperationException("SendGrid ApiKey or ConnectionString (JSON) is required");
		}
		if (string.IsNullOrWhiteSpace(settings.DefaultFrom.Address)) {
			throw new InvalidOperationException("DefaultFrom Address is required");
		}
	}

	/// <inheritdoc/>
	protected override void AddServiceProviderInstance(
		IServiceCollection services,
		string serviceKey,
		SendGridEmailInstanceSettings settings) {
		services.AddSendGridEmailService(serviceKey, settings);
	}

	/// <inheritdoc/>
	protected override IServiceProviderHealthCheck<SendGridEmailHealthCheckOptions> CreateHealthCheck(
		IServiceProvider serviceProvider,
		string serviceKey,
		SendGridEmailInstanceSettings settings) {
		return serviceProvider.CreateSendGridEmailHealthCheck(serviceKey, settings);
	}

}