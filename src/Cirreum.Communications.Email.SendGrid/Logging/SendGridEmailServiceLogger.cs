namespace Cirreum.Communications.Email.Logging;

using Microsoft.Extensions.Logging;

internal static partial class SendGridEmailServiceLogger {

	// Hot Path - Single Email Send
	[LoggerMessage(
		EventId = 1001,
		Level = LogLevel.Information,
		Message = "{Header}: Sending to {To} from {From} (subject: {Subject})")]
	public static partial void LogSendingEmail(
		this ILogger logger,
		string header,
		string to,
		string from,
		string? subject);

	// Hot Path - Email Send Success
	[LoggerMessage(
		EventId = 1002,
		Level = LogLevel.Information,
		Message = "{Header}: Success. MessageId: {MessageId} → {To}")]
	public static partial void LogEmailSuccess(
		this ILogger logger,
		string header,
		string? messageId,
		string to);

	// Hot Path - Retry Warning (Transient Failures)
	[LoggerMessage(
		EventId = 1003,
		Level = LogLevel.Warning,
		Message = "{Header}: transient {Status} for {Target}. Retrying in {DelayMs} ms (attempt {Attempt}/{Max})")]
	public static partial void LogRetryAttempt(
		this ILogger logger,
		string header,
		int status,
		string target,
		int delayMs,
		int attempt,
		int max);

	// Hot Path - Exception Retry Warning
	[LoggerMessage(
		EventId = 1004,
		Level = LogLevel.Warning,
		Message = "{Header}: exception sending to {Target}. Retrying in {DelayMs} ms (attempt {Attempt}/{Max})")]
	public static partial void LogExceptionRetry(
		this ILogger logger,
		Exception exception,
		string header,
		string target,
		int delayMs,
		int attempt,
		int max);

	// Medium Priority - Email Send Failure
	[LoggerMessage(
		EventId = 1005,
		Level = LogLevel.Error,
		Message = "{Header}: Failed ({Status}). {Error}")]
	public static partial void LogEmailFailure(
		this ILogger logger,
		string header,
		int status,
		string? error);

	// Medium Priority - Parallel Bulk Exception
	[LoggerMessage(
		EventId = 1006,
		Level = LogLevel.Error,
		Message = "{Header}: error sending to {To}")]
	public static partial void LogParallelBulkError(
		this ILogger logger,
		Exception exception,
		string header,
		string to);

}