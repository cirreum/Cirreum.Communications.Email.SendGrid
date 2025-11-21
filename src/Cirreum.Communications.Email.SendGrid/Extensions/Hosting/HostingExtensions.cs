namespace Cirreum.Communications.Email.Extensions.Hosting;

using Cirreum.Communications.Email.Configuration;
using Cirreum.Communications.Email.Health;
using Microsoft.Extensions.Hosting;

/// <summary>
/// Extension methods for <see cref="IHostApplicationBuilder"/> to configure SendGrid email services.
/// Provides fluent configuration methods for registering SendGrid email clients with dependency injection,
/// supporting multiple service instances, health checks, and various configuration approaches.
/// </summary>
/// <remarks>
/// These extensions integrate SendGrid email services into the .NET hosting model,
/// supporting both direct configuration and JSON-based configuration from sources like Azure Key Vault.
/// All methods support optional health check configuration for monitoring service availability.
/// </remarks>
public static class HostingExtensions {

	/// <summary>
	/// Adds a manually configured keyed <see cref="IEmailService"/> instance for SendGrid.
	/// </summary>
	public static IHostApplicationBuilder AddSendGridEmailClient(
		this IHostApplicationBuilder builder,
		string serviceKey,
		SendGridEmailInstanceSettings settings,
		Action<SendGridEmailHealthCheckOptions>? configureHealth = null) {
		ArgumentNullException.ThrowIfNull(builder);

		settings.HealthOptions ??= new SendGridEmailHealthCheckOptions();
		configureHealth?.Invoke(settings.HealthOptions);

		var registrar = new SendGridEmailRegistrar();
		registrar.RegisterInstance(serviceKey, settings, builder.Services, builder.Configuration);
		return builder;
	}

	/// <summary>
	/// Overload that takes a configure delegate.
	/// </summary>
	public static IHostApplicationBuilder AddSendGridEmailClient(
		this IHostApplicationBuilder builder,
		string serviceKey,
		Action<SendGridEmailInstanceSettings> configure,
		Action<SendGridEmailHealthCheckOptions>? configureHealth = null) {
		var settings = new SendGridEmailInstanceSettings();
		configure?.Invoke(settings);
		if (string.IsNullOrWhiteSpace(settings.Name)) {
			settings.Name = serviceKey;
		}
		return builder.AddSendGridEmailClient(serviceKey, settings, configureHealth);
	}

	/// <summary>
	/// Overload that accepts JSON (Key Vault) connection string.
	/// </summary>
	public static IHostApplicationBuilder AddSendGridEmailClient(
		this IHostApplicationBuilder builder,
		string serviceKey,
		string connectionJson,
		Action<SendGridEmailHealthCheckOptions>? configureHealth = null) {
		var settings = new SendGridEmailInstanceSettings {
			ConnectionString = connectionJson,
			Name = serviceKey
		};
		return builder.AddSendGridEmailClient(serviceKey, settings, configureHealth);
	}

}