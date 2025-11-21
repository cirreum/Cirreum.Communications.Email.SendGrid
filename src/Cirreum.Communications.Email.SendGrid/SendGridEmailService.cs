namespace Cirreum.Communications.Email;

using Cirreum.Communications.Email.Configuration;
using Cirreum.Communications.Email.Logging;
using Microsoft.Extensions.Logging;
using SendGrid.Helpers.Mail;
using System.Collections.Concurrent;
using System.IO;

internal sealed class SendGridEmailService(
	SendGridClient sendGridClient,
	SendGridEmailInstanceSettings settings,
	ILogger<SendGridEmailService> logger
) : IEmailService {

	private const string LogHeader = "SendGrid Email";
	private const int MaxRetryBackoffExponent = 6;
	private const int JitterMinMs = 250;
	private const int JitterMaxMs = 1000;

	// --------------------------- Single Send ---------------------------
	public async Task<EmailResult> SendEmailAsync(
		EmailMessage message,
		CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(message);

		// If From is empty, use instance DefaultFrom
		var msg = this.EnsureFrom(message);

		var errors = Validate(msg);
		if (errors.Count > 0) {
			if (logger.IsEnabled(LogLevel.Warning)) {
				logger.LogWarning("{Header}: validation failed for primary To {To}: {Errors}", LogHeader, msg.To.FirstOrDefault().Address, string.Join("; ", errors));
			}
			return new EmailResult {
				EmailAddress = msg.To.FirstOrDefault().Address,
				Success = false,
				ErrorMessage = "Validation failed",
				ValidationErrors = errors
			};
		}

		cancellationToken.ThrowIfCancellationRequested();

		var to = msg.To.First();
		var toStr = to.ToString();
		var from = msg.From.ToString();
		try {
			logger.LogSendingEmail(
				LogHeader,
				toStr,
				from,
				msg.Subject ?? "<none>");
			var sg = await this.BuildSendGridMessageAsync(msg, cancellationToken: cancellationToken);
			var result = await this.SendWithRetryAsync(() =>
				sendGridClient.SendEmailAsync(sg, cancellationToken),
					to.Address,
					cancellationToken);
			return await this.MapResponseAsync(result, to.Address, cancellationToken);
		} catch (Exception ex) {
			if (logger.IsEnabled(LogLevel.Error)) {
				logger.LogError(ex, "{Header}: Error sending to {To}", LogHeader, to.Address);
			}
			return new EmailResult {
				EmailAddress = to.Address,
				Success = false,
				ErrorMessage = ex.Message,
				Provider = "SendGrid"
			};
		}

	}

	// -------------- Bulk – shared template/message --------------
	public async Task<EmailResponse> SendBulkEmailAsync(
		EmailMessage template,
		IEnumerable<EmailAddress> recipients,
		bool validateOnly = false,
		CancellationToken cancellationToken = default) {

		var list = recipients?.ToList() ?? [];
		if (list.Count == 0) {
			throw new ArgumentException("Recipient list cannot be empty", nameof(recipients));
		}

		// Ensure From default
		var frame = this.EnsureFrom(template);

		var frameErrors = Validate(frame, validateTo: false);
		if (frameErrors.Count > 0) {
			if (logger.IsEnabled(LogLevel.Warning)) {
				logger.LogWarning("{Header}: bulk frame validation failed: {Errors}", LogHeader, string.Join("; ", frameErrors));
			}
			var failed = list.Select(r => new EmailResult {
				EmailAddress = r.Address,
				Success = false,
				ErrorMessage = "Validation failed",
				ValidationErrors = frameErrors
			}).ToList();
			return new EmailResponse(0, failed.Count, failed);
		}

		if (validateOnly) {
			var ok = list.Select(r => new EmailResult {
				EmailAddress = r.Address,
				Success = true,
				Provider = "SendGrid"
			}).ToList();
			return new EmailResponse(ok.Count, 0, ok);
		}

		var results = new List<EmailResult>(list.Count);

		foreach (var chunk in list.Chunk(Math.Max(1, settings.BulkOptions.MaxBatchSize))) {
			cancellationToken.ThrowIfCancellationRequested();

			try {
				// Build a single message with multiple personalization for efficiency
				var baseMsg = this.EnsureFrom(frame);
				baseMsg = baseMsg with { To = [] }; // we'll add To via personalization
				var sg = await this.BuildSendGridMessageAsync(baseMsg, includeTo: false, cancellationToken: cancellationToken);

				foreach (var addr in chunk) {
					sg.Personalizations.Add(new() {
						Tos = [new SendGrid.Helpers.Mail.EmailAddress(addr.Address, addr.Name)]
					});
				}

				var response = await this.SendWithRetryAsync(() =>
					sendGridClient.SendEmailAsync(sg, cancellationToken),
					target: $"{chunk.Length} recipients",
					cancellationToken);
				var (status, retryAfter) = GetStatusAndRetryAfter(response);
				var success = status is >= 200 and < 300;
				var msgId = GetMessageId(response);
				var error = success ? null : await response.Body.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

				results.AddRange(chunk.Select(r => new EmailResult {
					EmailAddress = r.Address,
					Success = success,
					MessageId = msgId,
					ErrorMessage = error,
					Provider = "SendGrid",
					StatusCode = status,
					RetryAfter = retryAfter
				}));

			} catch (Exception ex) {
				if (logger.IsEnabled(LogLevel.Error)) {
					logger.LogError(ex, "{Header}: error sending bulk chunk of size {Count}", LogHeader, chunk.Length);
				}
				results.AddRange(chunk.Select(r => new EmailResult {
					EmailAddress = r.Address,
					Success = false,
					ErrorMessage = ex.Message,
					Provider = "SendGrid"
				}));
			}
		}

		var sent = results.Count(r => r.Success);
		var failedCount = results.Count - sent;
		return new EmailResponse(sent, failedCount, results);
	}

	// -------------- Bulk – fully personalized messages --------------
	public async Task<EmailResponse> SendBulkEmailAsync(
		IEnumerable<EmailMessage> messages,
		bool validateOnly = false,
		CancellationToken cancellationToken = default) {

		var list = messages?.ToList() ?? [];
		if (list.Count == 0) {
			throw new ArgumentException("Messages cannot be empty", nameof(messages));
		}

		var results = new ConcurrentBag<EmailResult>();
		int sent = 0, failed = 0;

		await Parallel.ForEachAsync(
			list,
			new ParallelOptions {
				MaxDegreeOfParallelism = settings.BulkOptions.MaxConcurrency,
				CancellationToken = cancellationToken
			},
			async (msg, token) => {
				try {
					var withFrom = this.EnsureFrom(msg);
					var errors = Validate(withFrom);
					if (errors.Count > 0) {
						results.Add(new EmailResult {
							EmailAddress = withFrom.To.FirstOrDefault().Address,
							Success = false,
							ErrorMessage = "Validation failed",
							ValidationErrors = errors
						});
						Interlocked.Increment(ref failed);
						return;
					}

					if (validateOnly) {
						results.Add(new EmailResult {
							EmailAddress = withFrom.To.FirstOrDefault().Address,
							Success = true,
							Provider = "SendGrid"
						});
						Interlocked.Increment(ref sent);
						return;
					}

					var sg = await this.BuildSendGridMessageAsync(withFrom, cancellationToken: token);

					var response = await this.SendWithRetryAsync(() =>
						sendGridClient.SendEmailAsync(sg, token),
						withFrom.To.First().Address,
						token);

					var mapped = await this.MapResponseAsync(
						response,
						withFrom.To.First().Address,
						token);

					results.Add(mapped);
					if (mapped.Success) {
						Interlocked.Increment(ref sent);
					} else {
						Interlocked.Increment(ref failed);
					}

				} catch (Exception ex) {
					var target = msg.To.FirstOrDefault().Address ?? "<unknown>";
					logger.LogParallelBulkError(ex, LogHeader, target);
					results.Add(new EmailResult { EmailAddress = target, Success = false, ErrorMessage = ex.Message, Provider = "SendGrid" });
					Interlocked.Increment(ref failed);
				}
			});

		return new EmailResponse(sent, failed, [.. results]);
	}

	// ============================== Helpers ==============================

	private EmailMessage EnsureFrom(EmailMessage message) {
		if (string.IsNullOrWhiteSpace(message.From.Address)) {
			return message with { From = settings.DefaultFrom };
		}
		return message;
	}

	private static List<string> Validate(EmailMessage message, bool validateTo = true) {
		var errors = new List<string>();

		if (validateTo && (message.To is null || message.To.Count == 0)) {
			errors.Add("At least one To recipient is required.");
		}

		var hasContent = !string.IsNullOrWhiteSpace(message.TextContent) ||
						!string.IsNullOrWhiteSpace(message.HtmlContent);
		var hasTemplate = !string.IsNullOrWhiteSpace(message.TemplateId) ||
						 !string.IsNullOrWhiteSpace(message.TemplateKey);

		if (!hasContent && !hasTemplate) {
			errors.Add("Either Text/Html content or TemplateKey/TemplateId must be provided.");
		}

		// Validate all recipients addresses
		var recipients = (message.To ?? [])
			.Concat(message.Cc ?? [])
			.Concat(message.Bcc ?? []);
		foreach (var recipient in recipients) {
			if (string.IsNullOrWhiteSpace(recipient.Address) || !EmailAddress.IsValidEmailAddress(recipient.Address)) {
				errors.Add($"Invalid recipient address: '{recipient.Address}'.");
			}
		}

		// Validate ReplyTo if provided
		if (message.ReplyTo?.Address is { } replyTo && !EmailAddress.IsValidEmailAddress(replyTo)) {
			errors.Add("Invalid ReplyTo address.");
		}

		// Validate attachments
		foreach (var attachment in message.Attachments ?? []) {
			if (attachment.Content is null && attachment.ContentStream is null) {
				errors.Add($"Attachment '{attachment.FileName}' must provide Content or ContentStream.");
			}

			if (string.IsNullOrWhiteSpace(attachment.ContentType)) {
				errors.Add($"Attachment '{attachment.FileName}' must provide ContentType.");
			}
		}

		return errors;

	}

	private string? ResolveTemplateId(EmailMessage message) {
		if (!string.IsNullOrWhiteSpace(message.TemplateId)) {
			return message.TemplateId;
		}

		if (!string.IsNullOrWhiteSpace(message.TemplateKey) &&
			settings.TemplateMap.TryGetValue(message.TemplateKey, out var mappedId)) {
			return mappedId;
		}

		return null;
	}

	private async Task<SendGridMessage> BuildSendGridMessageAsync(
	   EmailMessage message,
	   bool includeTo = true,
	   CancellationToken cancellationToken = default) {

		var m = new SendGridMessage {
			From = new SendGrid.Helpers.Mail.EmailAddress(message.From.Address, message.From.Name)
		};

		// ReplyTo
		if (message.ReplyTo is { } rt) {
			m.ReplyTo = new SendGrid.Helpers.Mail.EmailAddress(rt.Address, rt.Name);
		}

		// Subject & content
		if (!string.IsNullOrWhiteSpace(message.Subject)) {
			m.Subject = message.Subject;
		}

		if (!string.IsNullOrWhiteSpace(message.TextContent)) {
			m.AddContent("text/plain", message.TextContent);
		}

		if (!string.IsNullOrWhiteSpace(message.HtmlContent)) {
			m.AddContent("text/html", message.HtmlContent);
		}

		// Headers (incl. priority map)
		var headers = new Dictionary<string, string>(settings.GlobalHeaders, StringComparer.OrdinalIgnoreCase);
		foreach (var kv in message.Headers) {
			headers[kv.Key] = kv.Value;
		}

		foreach (var kv in MapPriorityHeaders(message.Priority)) {
			headers[kv.Key] = kv.Value;
		}

		foreach (var (k, v) in headers) {
			m.AddHeader(k, v);
		}

		// Categories
		foreach (var cat in settings.GlobalCategories) {
			m.AddCategory(cat);
		}

		foreach (var cat in message.Categories) {
			m.AddCategory(cat);
		}

		// Custom args
		foreach (var kv in message.CustomArgs) {
			m.AddCustomArg(kv.Key, kv.Value);
		}

		// Idempotency
		if (!string.IsNullOrWhiteSpace(message.IdempotencyKey)) {
			m.AddHeader("Idempotency-Key", message.IdempotencyKey);
		}

		// Template
		var templateId = this.ResolveTemplateId(message);
		if (!string.IsNullOrWhiteSpace(templateId)) {
			m.SetTemplateId(templateId);
			if (message.TemplateData.Count > 0) {
				m.SetTemplateData(message.TemplateData);
			}
		}

		// Attachments
		foreach (var attachment in message.Attachments ?? []) {
			if (attachment.ContentStream is not null) {
				using var ms = new MemoryStream();
				// Safety check: reset position if possible
				var stream = attachment.ContentStream;
				if (stream.CanSeek && stream.Position != 0) {
					try {
						stream.Position = 0;
					} catch (IOException ex) {
						if (logger.IsEnabled(LogLevel.Warning)) {
							logger.LogWarning("{Header}: Unable to reset stream position for attachment '{FileName}': {Error}",
								LogHeader, attachment.FileName, ex.Message);
						}
					} catch (NotSupportedException ex) {
						if (logger.IsEnabled(LogLevel.Warning)) {
							logger.LogWarning("{Header}: Stream seeking not supported for attachment '{FileName}': {Error}",
								LogHeader, attachment.FileName, ex.Message);
						}
					}
				}
				await stream.CopyToAsync(ms, cancellationToken);
				m.AddAttachment(
					attachment.FileName,
					Convert.ToBase64String(ms.ToArray()),
					attachment.ContentType,
					attachment.Disposition == EmailAttachmentDisposition.Inline ? "inline" : "attachment",
					attachment.ContentId);
			} else if (attachment.Content is not null) {
				m.AddAttachment(
					attachment.FileName,
					Convert.ToBase64String(attachment.Content),
					attachment.ContentType,
					attachment.Disposition == EmailAttachmentDisposition.Inline ? "inline" : "attachment",
					attachment.ContentId);
			}
		}

		// Sandbox
		if (settings.SandboxMode) {
			m.MailSettings ??= new MailSettings();
			m.MailSettings.SandboxMode = new SandboxMode { Enable = true };
		}

		// Recipients
		if (includeTo) {
			var p = new Personalization {
				Tos = [],
				Ccs = [],
				Bccs = []
			};

			foreach (var addr in message.To ?? []) {
				p.Tos.Add(new SendGrid.Helpers.Mail.EmailAddress(addr.Address, addr.Name));
			}

			foreach (var addr in message.Cc ?? []) {
				p.Ccs.Add(new SendGrid.Helpers.Mail.EmailAddress(addr.Address, addr.Name));
			}

			foreach (var addr in message.Bcc ?? []) {
				p.Bccs.Add(new SendGrid.Helpers.Mail.EmailAddress(addr.Address, addr.Name));
			}

			m.Personalizations.Add(p);
		}

		// Scheduled send
		if (message.SendAt is DateTimeOffset dto) {
			m.SendAt = dto.ToUnixTimeSeconds();
		}

		return m;
	}

	private static Dictionary<string, string> MapPriorityHeaders(EmailPriority p)
		=> p switch {
			EmailPriority.High => new() { ["X-Priority"] = "1", ["X-MSMail-Priority"] = "High", ["Importance"] = "High" },
			EmailPriority.Low => new() { ["X-Priority"] = "5", ["X-MSMail-Priority"] = "Low", ["Importance"] = "Low" },
			_ => []
		};

	private async Task<EmailResult> MapResponseAsync(
		Response response,
		string primaryTo,
		CancellationToken cancellationToken) {

		var (status, retryAfter) = GetStatusAndRetryAfter(response);
		var success = status is >= 200 and < 300;
		var msgId = GetMessageId(response);
		var error = success ? null : await response.Body.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

		if (success) {
			logger.LogEmailSuccess(LogHeader, msgId, primaryTo);
		} else {
			logger.LogEmailFailure(LogHeader, status, error ?? "<no body>");
		}

		return new EmailResult {
			EmailAddress = primaryTo,
			Success = success,
			MessageId = msgId,
			ErrorMessage = error,
			Provider = "SendGrid",
			StatusCode = status,
			RetryAfter = retryAfter
		};
	}

	private static (int status, TimeSpan? retryAfter) GetStatusAndRetryAfter(Response response) {
		var status = (int)response.StatusCode;
		TimeSpan? retryAfter = null;
		if (response.Headers.TryGetValues("Retry-After", out var values)) {
			var v = values.FirstOrDefault();
			if (!string.IsNullOrWhiteSpace(v)) {
				if (int.TryParse(v, out var seconds)) {
					retryAfter = TimeSpan.FromSeconds(seconds);
				} else if (DateTimeOffset.TryParse(v, out var when)) {
					retryAfter = when - DateTimeOffset.UtcNow;
				}
			}
		}
		return (status, retryAfter);
	}

	private static string? GetMessageId(Response response)
		=> response.Headers.TryGetValues("X-Message-Id", out var values) ? values.FirstOrDefault() : null;

	// Simple retry with exponential backoff + jitter for 429/5xx
	private async Task<Response> SendWithRetryAsync(
		Func<Task<Response>> send,
		string target,
		CancellationToken cancellationToken) {
		var maxRetries = settings.MaxRetries;
		for (var attempt = 0; attempt <= maxRetries; attempt++) {
			try {
				var resp = await send();
				if ((int)resp.StatusCode == 429 || (int)resp.StatusCode >= 500) {
					if (attempt < maxRetries) {
						var (_, retryAfter) = GetStatusAndRetryAfter(resp);
						var delay = retryAfter ?? TimeSpan.FromSeconds(Math.Pow(2, Math.Min(attempt, MaxRetryBackoffExponent))) + TimeSpan.FromMilliseconds(Random.Shared.Next(JitterMinMs, JitterMaxMs));
						logger.LogRetryAttempt(
							LogHeader, (int)resp.StatusCode, target, (int)delay.TotalMilliseconds, attempt + 1, maxRetries);
						await Task.Delay(delay, cancellationToken);
						continue;
					}
				}
				return resp;
			} catch (Exception ex) when (attempt < maxRetries) {
				var delay = TimeSpan.FromSeconds(Math.Pow(2, Math.Min(attempt, MaxRetryBackoffExponent))) + TimeSpan.FromMilliseconds(Random.Shared.Next(JitterMinMs, JitterMaxMs));
				logger.LogExceptionRetry(ex,
					LogHeader, target, (int)delay.TotalMilliseconds, attempt + 1, maxRetries);
				await Task.Delay(delay, cancellationToken);
			}
		}

		// Final attempt – no retries left
		return await send();
	}

}