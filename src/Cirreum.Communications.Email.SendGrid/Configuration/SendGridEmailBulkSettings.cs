namespace Cirreum.Communications.Email.Configuration;

/// <summary>
/// Configuration settings for SendGrid bulk email operations.
/// Controls batching behavior, concurrency limits, and performance optimization
/// for high-volume email sending scenarios.
/// </summary>
/// <remarks>
/// These settings help optimize performance and stay within SendGrid's API limits
/// when sending large volumes of emails. Proper configuration can significantly
/// improve throughput while avoiding rate limiting issues.
/// </remarks>
public class SendGridEmailBulkSettings {

	/// <summary>
	/// Gets or sets the maximum number of emails that can be sent in a single bulk operation.
	/// This helps optimize performance and stay within SendGrid's API limits.
	/// </summary>
	/// <value>The maximum batch size as an integer. Defaults to 500.</value>
	public int MaxBatchSize { get; set; } = 500;

	/// <summary>
	/// Gets or sets the maximum degree of parallelism for bulk operations.
	/// Controls how many email operations can be executed concurrently during bulk sending.
	/// Valid range: 1-50.
	/// </summary>
	/// <value>
	/// An integer representing the maximum number of concurrent operations.
	/// Values are automatically clamped to the range 1-50. Default is 4.
	/// </value>
	/// <remarks>
	/// Higher values can improve throughput but may increase memory usage and API rate limit pressure.
	/// Lower values provide more conservative resource usage but may reduce overall performance.
	/// </remarks>
	private int _maxConcurrency = 4;
	
	/// <summary>
	/// Gets or sets the maximum degree of parallelism for bulk operations.
	/// </summary>
	/// <value>The maximum concurrency level, automatically clamped to range 1-50.</value>
	public int MaxConcurrency {
		get => _maxConcurrency;
		set => _maxConcurrency = Math.Clamp(value, 1, 50);
	}

}