namespace Microsoft.Extensions.Hosting;

using Cirreum.Communications.Email;
using Cirreum.Communications.Email.Configuration;
using Cirreum.Communications.Email.Health;
using Cirreum.ServiceProvider.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

internal static class SendGridRegistrationExtensions {

	public static void AddSendGridEmailService(
		this IServiceCollection services,
		string serviceKey,
		SendGridEmailInstanceSettings settings) {

		// Keyed IEmailService factory → constructs a client bound to this instance settings
		services.AddKeyedSingleton<IEmailService>(
			serviceKey,
			(sp, key) => {
				var logger = sp.GetRequiredService<ILogger<SendGridEmailService>>();
				var client = new SendGridClient(settings.ApiKey);
				return new SendGridEmailService(
					client,
					settings,
					logger);
			});

		// Register Default (non-Keyed) Service Factory (wraps the keyed registration)
		if (serviceKey.Equals(ServiceProviderSettings.DefaultKey, StringComparison.OrdinalIgnoreCase)) {
			services.TryAddSingleton(sp => sp.GetRequiredKeyedService<IEmailService>(serviceKey));
		}

	}

	public static SendGridEmailHealthCheck CreateSendGridEmailHealthCheck(
		this IServiceProvider sp,
		string serviceKey,
		SendGridEmailInstanceSettings settings) {
		var env = sp.GetRequiredService<IHostEnvironment>();
		var cache = sp.GetRequiredService<IMemoryCache>();
		var service = sp.GetRequiredKeyedService<IEmailService>(serviceKey);
		return new SendGridEmailHealthCheck(service, settings, cache, env.IsProduction());
	}

}