using System.Collections.Concurrent;
using System.Net;
using System.Text;
using AUBDigitalAcademy.Models;
using AUBDigitalAcademy.Services.Implementations;
using AUBDigitalAcademy.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AUBDigitalAcademy.Data;

public static class TelegramLogRedactionTestRunner
{
    private const string KnownTestBotToken = "999999999:TEST_SYNTHETIC_BOT_TOKEN_NEVER_LOG_XYZ123";
    private const string KnownTestOtp = "482913";
    private const string KnownLinkSecret = "deadbeef0123456789abcdef98765432";

    private sealed record CapturedLogEntry(
        string Category,
        LogLevel Level,
        string Message,
        string? ExceptionText,
        string Scopes);

    private sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();
        public ConcurrentQueue<CapturedLogEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, this);

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopeProvider = scopeProvider;

        public void Clear()
        {
            while (Entries.TryDequeue(out _)) { }
        }

        public string DumpAllText()
        {
            var sb = new StringBuilder();
            foreach (var e in Entries)
            {
                sb.AppendLine($"[{e.Level}] {e.Category}: {e.Message}");
                if (!string.IsNullOrEmpty(e.Scopes))
                {
                    sb.AppendLine($"  Scopes: {e.Scopes}");
                }
                if (!string.IsNullOrEmpty(e.ExceptionText))
                {
                    sb.AppendLine($"  Ex: {e.ExceptionText}");
                }
            }
            return sb.ToString();
        }

        public void Dispose() { }

        private sealed class CapturingLogger : ILogger
        {
            private readonly string _category;
            private readonly CapturingLoggerProvider _owner;

            public CapturingLogger(string category, CapturingLoggerProvider owner)
            {
                _category = category;
                _owner = owner;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
                => _owner._scopeProvider.Push(state);

            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var scopeSb = new StringBuilder();
                _owner._scopeProvider.ForEachScope((scope, builder) =>
                {
                    if (builder.Length > 0) builder.Append(" | ");
                    builder.Append(scope);
                }, scopeSb);

                _owner.Entries.Enqueue(new CapturedLogEntry(
                    _category,
                    logLevel,
                    formatter(state, exception),
                    exception?.ToString(),
                    scopeSb.ToString()));
            }
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Handler { get; set; }
            = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true,\"result\":[]}")
            });

        public List<string> ObservedUrls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri != null)
            {
                ObservedUrls.Add(request.RequestUri.OriginalString);
            }
            return Handler(request, cancellationToken);
        }
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public StubHostEnvironment(string environmentName) => EnvironmentName = environmentName;
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "AUBDigitalAcademy";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class ThrowingTelegramScopeFactory : IServiceScopeFactory
    {
        private readonly string _token;
        public ThrowingTelegramScopeFactory(string token) => _token = token;

        public IServiceScope CreateScope() => new ThrowingScope(_token);

        private sealed class ThrowingScope : IServiceScope, IServiceProvider
        {
            private readonly ITelegramService _telegram;
            public ThrowingScope(string token) => _telegram = new ThrowingTelegramService(token);
            public IServiceProvider ServiceProvider => this;
            public object? GetService(Type serviceType) => serviceType == typeof(ITelegramService) ? _telegram : null;
            public void Dispose() { }
        }

        private sealed class ThrowingTelegramService : ITelegramService
        {
            private readonly string _token;
            public ThrowingTelegramService(string token) => _token = token;

            public Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long? offset = null, int timeoutSeconds = 20, CancellationToken cancellationToken = default)
                => throw new HttpRequestException($"Socket failure for https://api.telegram.org/bot{_token}/getUpdates", null, HttpStatusCode.BadGateway);

            public Task<bool> SendVerificationCodeAsync(string universityId, string studentName, string verificationCode, string? customChatId = null) => Task.FromResult(true);
            public Task<(bool Success, string? BotUsername, string? ErrorMessage)> TestBotConnectivityAsync() => Task.FromResult((false, (string?)null, (string?)"n/a"));
            public Task<bool> SendMessageAsync(long chatId, string text, string parseMode = "HTML", CancellationToken cancellationToken = default) => Task.FromResult(true);
            public Task<(string LinkUrl, string PlainToken, DateTime ExpiresAt)> GenerateLinkTokenAsync(string userId, string? clientIp = null) => throw new NotSupportedException();
            public Task<(bool Success, string Message, User? User)> ProcessLinkTokenAsync(string plainToken, long chatId, string? telegramUsername) => throw new NotSupportedException();
            public Task<bool> IsAccountLinkedAsync(string userId) => Task.FromResult(false);
        }
    }

    public static async Task RunTestsAsync(IServiceProvider rootServiceProvider)
    {
        Console.WriteLine("=================================================");
        Console.WriteLine("  TELEGRAM LOG REDACTION REGRESSION TEST SUITE   ");
        Console.WriteLine("=================================================");

        int passed = 0;
        int failed = 0;

        void Assert(string name, bool condition, string detail = "")
        {
            if (condition)
            {
                Console.WriteLine($"[PASS] {name}");
                passed++;
            }
            else
            {
                Console.WriteLine($"[FAIL] {name}{(string.IsNullOrEmpty(detail) ? "" : $" - {detail}")}");
                failed++;
            }
        }

        var dbOptions = rootServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>();

        ServiceProvider BuildTestContainer(
            CapturingLoggerProvider loggerProvider,
            StubHttpMessageHandler httpHandler,
            bool configureBotToken = true,
            string? defaultChatId = null,
            string environmentName = "Development")
        {
            var dict = new Dictionary<string, string?>
            {
                ["Telegram:BotToken"] = configureBotToken ? KnownTestBotToken : "",
                ["Telegram:ChatId"] = defaultChatId ?? "",
                ["Telegram:DisablePolling"] = "false"
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(config);
            services.AddSingleton<IHostEnvironment>(new StubHostEnvironment(environmentName));
            services.AddSingleton(dbOptions);
            services.AddScoped<ApplicationDbContext>();
            services.AddLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Trace);
                logging.AddProvider(loggerProvider);
            });
            services.AddTransient<TelegramHttpClientLogger>();
            services.AddHttpClient<ITelegramService, TelegramService>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .RemoveAllLoggers()
            .AddLogger<TelegramHttpClientLogger>(wrapHandlersPipeline: true)
            .ConfigurePrimaryHttpMessageHandler(() => httpHandler);

            return services.BuildServiceProvider();
        }

        // 1. SendVerificationCodeAsync (200 OK) at LogLevel.Trace: token and OTP never logged, diagnostics preserved
        {
            var logs = new CapturingLoggerProvider();
            var handler = new StubHttpMessageHandler
            {
                Handler = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"ok\":true,\"result\":{\"message_id\":1}}")
                })
            };
            using var sp = BuildTestContainer(logs, handler);
            var telegram = sp.GetRequiredService<ITelegramService>();
            var sent = await telegram.SendVerificationCodeAsync("STU900", "Test Student", KnownTestOtp, "12345678");
            var dump = logs.DumpAllText();

            Assert(
                "1. SendVerificationCodeAsync (200 OK) at Trace level redacts bot token and OTP while keeping diagnostics",
                sent &&
                !dump.Contains(KnownTestBotToken, StringComparison.Ordinal) &&
                !dump.Contains(KnownTestOtp, StringComparison.Ordinal) &&
                dump.Contains("bot[REDACTED]/sendMessage", StringComparison.Ordinal) &&
                dump.Contains("200", StringComparison.Ordinal) &&
                dump.Contains("Operation=sendMessage", StringComparison.Ordinal),
                $"dump={dump}");
        }

        // 2. SendVerificationCodeAsync HTTP error response echoing token in body -> redacted, status code preserved
        {
            var logs = new CapturingLoggerProvider();
            var handler = new StubHttpMessageHandler
            {
                Handler = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent($"{{\"ok\":false,\"description\":\"Unauthorized for https://api.telegram.org/bot{KnownTestBotToken}/sendMessage\"}}")
                })
            };
            using var sp = BuildTestContainer(logs, handler);
            var telegram = sp.GetRequiredService<ITelegramService>();
            var sent = await telegram.SendVerificationCodeAsync("STU901", "Test Student", KnownTestOtp, "12345678");
            var dump = logs.DumpAllText();

            Assert(
                "2. SendVerificationCodeAsync non-success HTTP status redacts echoed token and preserves 401 diagnostic",
                !sent &&
                !dump.Contains(KnownTestBotToken, StringComparison.Ordinal) &&
                !dump.Contains(KnownTestOtp, StringComparison.Ordinal) &&
                dump.Contains("401", StringComparison.Ordinal) &&
                dump.Contains("bot[REDACTED]/sendMessage", StringComparison.Ordinal),
                $"dump={dump}");
        }

        // 3. SendVerificationCodeAsync exception containing full Bot API URL & inner exception -> logged & redacted
        {
            var logs = new CapturingLoggerProvider();
            var handler = new StubHttpMessageHandler
            {
                Handler = (_, _) => throw new HttpRequestException(
                    $"Connection failed for https://api.telegram.org/bot{KnownTestBotToken}/sendMessage",
                    new InvalidOperationException($"Inner socket error on bot{KnownTestBotToken}"),
                    HttpStatusCode.BadGateway)
            };
            using var sp = BuildTestContainer(logs, handler);
            var telegram = sp.GetRequiredService<ITelegramService>();
            var sent = await telegram.SendVerificationCodeAsync("STU902", "Test Student", KnownTestOtp, "12345678");
            var dump = logs.DumpAllText();

            Assert(
                "3. SendVerificationCodeAsync exception redacts outer and inner token URLs without swallowing error log",
                !sent &&
                !dump.Contains(KnownTestBotToken, StringComparison.Ordinal) &&
                !dump.Contains(KnownTestOtp, StringComparison.Ordinal) &&
                logs.Entries.Any(e => e.Level == LogLevel.Error) &&
                dump.Contains("502", StringComparison.Ordinal) &&
                dump.Contains("HttpRequestException", StringComparison.Ordinal),
                $"dump={dump}");
        }

        // 4. SendMessageAsync (200 OK & exception) -> token never logged, diagnostics preserved
        {
            var logs = new CapturingLoggerProvider();
            var callCount = 0;
            var handler = new StubHttpMessageHandler
            {
                Handler = (_, _) =>
                {
                    callCount++;
                    if (callCount == 1)
                    {
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent("{\"ok\":true,\"result\":{\"message_id\":2}}")
                        });
                    }
                    throw new HttpRequestException($"Timeout calling https://api.telegram.org/bot{KnownTestBotToken}/sendMessage");
                }
            };
            using var sp = BuildTestContainer(logs, handler);
            var telegram = sp.GetRequiredService<ITelegramService>();
            var okSend = await telegram.SendMessageAsync(12345678, "Hello");
            var failSend = await telegram.SendMessageAsync(12345678, "Hello again");
            var dump = logs.DumpAllText();

            Assert(
                "4. SendMessageAsync success and exception paths never leak bot token and log errors",
                okSend &&
                !failSend &&
                !dump.Contains(KnownTestBotToken, StringComparison.Ordinal) &&
                dump.Contains("bot[REDACTED]/sendMessage", StringComparison.Ordinal) &&
                logs.Entries.Any(e => e.Level == LogLevel.Error),
                $"dump={dump}");
        }

        // 5. GetUpdatesAsync polling (200 OK, HTTP error, and exception) at Trace level -> token never logged
        {
            var logs = new CapturingLoggerProvider();
            var step = 0;
            var handler = new StubHttpMessageHandler
            {
                Handler = (_, _) =>
                {
                    step++;
                    return step switch
                    {
                        1 => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent("{\"ok\":true,\"result\":[]}")
                        }),
                        2 => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
                        {
                            Content = new StringContent($"Upstream error at https://api.telegram.org/bot{KnownTestBotToken}/getUpdates")
                        }),
                        _ => throw new HttpRequestException($"Network drop on https://api.telegram.org/bot{KnownTestBotToken}/getUpdates")
                    };
                }
            };
            using var sp = BuildTestContainer(logs, handler);
            var telegram = sp.GetRequiredService<ITelegramService>();
            var r1 = await telegram.GetUpdatesAsync(offset: 10, timeoutSeconds: 5);
            var r2 = await telegram.GetUpdatesAsync(offset: 11, timeoutSeconds: 5);
            var r3 = await telegram.GetUpdatesAsync(offset: 12, timeoutSeconds: 5);
            var dump = logs.DumpAllText();

            Assert(
                "5. GetUpdatesAsync success, HTTP 502, and exception paths redact bot token while preserving getUpdates diagnostics",
                r1.Count == 0 &&
                r2.Count == 0 &&
                r3.Count == 0 &&
                !dump.Contains(KnownTestBotToken, StringComparison.Ordinal) &&
                dump.Contains("bot[REDACTED]/getUpdates", StringComparison.Ordinal) &&
                dump.Contains("502", StringComparison.Ordinal) &&
                logs.Entries.Any(e => e.Level == LogLevel.Error),
                $"dump={dump}");
        }

        // 6. TestBotConnectivityAsync error & exception -> neither logs nor returned error string expose token
        {
            var logs = new CapturingLoggerProvider();
            var handler = new StubHttpMessageHandler
            {
                Handler = (_, _) => throw new HttpRequestException(
                    $"Failed GET https://api.telegram.org/bot{KnownTestBotToken}/getMe",
                    null,
                    HttpStatusCode.ServiceUnavailable)
            };
            using var sp = BuildTestContainer(logs, handler);
            var telegram = sp.GetRequiredService<ITelegramService>();
            var (success, _, error) = await telegram.TestBotConnectivityAsync();
            var dump = logs.DumpAllText();

            Assert(
                "6. TestBotConnectivityAsync exception redacts bot token from both logs and returned error message",
                !success &&
                !string.IsNullOrEmpty(error) &&
                !error.Contains(KnownTestBotToken, StringComparison.Ordinal) &&
                !dump.Contains(KnownTestBotToken, StringComparison.Ordinal) &&
                error.Contains("bot[REDACTED]/getMe", StringComparison.Ordinal) &&
                logs.Entries.Any(e => e.Level == LogLevel.Error),
                $"error={error}; dump={dump}");
        }

        // 7. TelegramPollingService polling loop + HandleUpdateAsync (/start link_ & /link) -> redacts bot token & link secret
        {
            var logs = new CapturingLoggerProvider();
            var pollStep = 0;
            var handler = new StubHttpMessageHandler
            {
                Handler = (req, _) =>
                {
                    var uri = req.RequestUri?.OriginalString ?? string.Empty;
                    if (uri.Contains("/sendMessage", StringComparison.OrdinalIgnoreCase))
                    {
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent("{\"ok\":true,\"result\":{\"message_id\":99}}")
                        });
                    }

                    pollStep++;
                    if (pollStep == 1)
                    {
                        var payload = $"{{\"ok\":true,\"result\":[{{\"update_id\":101,\"message\":{{\"message_id\":1,\"chat\":{{\"id\":555001,\"type\":\"private\"}},\"from\":{{\"id\":555001,\"first_name\":\"Dara\",\"username\":\"dara_test\"}},\"text\":\"/start link_{KnownLinkSecret}\"}}}},{{\"update_id\":102,\"message\":{{\"message_id\":2,\"chat\":{{\"id\":555001,\"type\":\"private\"}},\"from\":{{\"id\":555001,\"first_name\":\"Dara\",\"username\":\"dara_test\"}},\"text\":\"/link {KnownLinkSecret}\"}}}}]}}";
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(payload)
                        });
                    }

                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"ok\":true,\"result\":[]}")
                    });
                }
            };

            var dict = new Dictionary<string, string?>
            {
                ["Telegram:BotToken"] = KnownTestBotToken,
                ["Telegram:DisablePolling"] = "false"
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(config);
            services.AddSingleton<IHostEnvironment>(new StubHostEnvironment("Development"));
            services.AddSingleton(dbOptions);
            services.AddScoped<ApplicationDbContext>();
            services.AddLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Trace);
                logging.AddProvider(logs);
            });
            services.AddTransient<TelegramHttpClientLogger>();
            services.AddHttpClient<ITelegramService, TelegramService>()
                .RemoveAllLoggers()
                .AddLogger<TelegramHttpClientLogger>(wrapHandlersPipeline: true)
                .ConfigurePrimaryHttpMessageHandler(() => handler);

            using var sp = services.BuildServiceProvider();
            var pollingService = new TelegramPollingService(
                sp.GetRequiredService<IServiceScopeFactory>(),
                config,
                sp.GetRequiredService<ILogger<TelegramPollingService>>());

            using var cts = new CancellationTokenSource();
            await pollingService.StartAsync(cts.Token);

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (pollStep < 2 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            cts.Cancel();
            await pollingService.StopAsync(CancellationToken.None);

            var dump = logs.DumpAllText();
            Assert(
                "7. TelegramPollingService polling & /start link_ / /link handling redacts bot token and link secret",
                pollStep >= 1 &&
                handler.ObservedUrls.Any(u => u.Contains("/sendMessage", StringComparison.OrdinalIgnoreCase)) &&
                !dump.Contains(KnownTestBotToken, StringComparison.Ordinal) &&
                !dump.Contains(KnownLinkSecret, StringComparison.Ordinal) &&
                dump.Contains("bot[REDACTED]/getUpdates", StringComparison.Ordinal) &&
                dump.Contains("bot[REDACTED]/sendMessage", StringComparison.Ordinal) &&
                dump.Contains("/start link_[REDACTED]", StringComparison.Ordinal) &&
                dump.Contains("/link [REDACTED]", StringComparison.Ordinal),
                $"pollStep={pollStep}; dump={dump}");
        }

        // 8. Simulation mode in Development and Production -> never logs plaintext OTP
        {
            var prevToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
            var prevChatId = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID");
            try
            {
                Environment.SetEnvironmentVariable("TELEGRAM_BOT_TOKEN", null);
                Environment.SetEnvironmentVariable("TELEGRAM_CHAT_ID", null);

                var devLogs = new CapturingLoggerProvider();
                using var devSp = BuildTestContainer(devLogs, new StubHttpMessageHandler(), configureBotToken: false, environmentName: "Development");
                var devSent = await devSp.GetRequiredService<ITelegramService>().SendVerificationCodeAsync("STU903", "Dev User", KnownTestOtp);
                var devDump = devLogs.DumpAllText();

                var prodLogs = new CapturingLoggerProvider();
                using var prodSp = BuildTestContainer(prodLogs, new StubHttpMessageHandler(), configureBotToken: false, environmentName: "Production");
                var prodSent = await prodSp.GetRequiredService<ITelegramService>().SendVerificationCodeAsync("STU904", "Prod User", KnownTestOtp);
                var prodDump = prodLogs.DumpAllText();

                Assert(
                    "8. Simulation mode suppresses plaintext OTP in both Development (Information) and Production (Warning)",
                    devSent &&
                    prodSent &&
                    !devDump.Contains(KnownTestOtp, StringComparison.Ordinal) &&
                    !prodDump.Contains(KnownTestOtp, StringComparison.Ordinal) &&
                    devLogs.Entries.Any(e => e.Level == LogLevel.Information) &&
                    prodLogs.Entries.Any(e => e.Level == LogLevel.Warning),
                    $"devDump={devDump}; prodDump={prodDump}");
            }
            finally
            {
                Environment.SetEnvironmentVariable("TELEGRAM_BOT_TOKEN", prevToken);
                Environment.SetEnvironmentVariable("TELEGRAM_CHAT_ID", prevChatId);
            }
        }

        // 9. Sanitizer redacts Bearer JWTs, raw JWTs, and sensitive query parameters
        {
            const string sampleJwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJ1c2VyMTIzIn0.signaturePart12345";
            var raw = $"Authorization: Bearer {sampleJwt} | token={KnownLinkSecret}&password=MyPassword123!&otp={KnownTestOtp}";
            var sanitized = TelegramLogSanitizer.Sanitize(raw, KnownTestBotToken);

            Assert(
                "9. TelegramLogSanitizer redacts Bearer JWTs, raw JWTs, and sensitive query parameters",
                !sanitized.Contains(sampleJwt, StringComparison.Ordinal) &&
                !sanitized.Contains(KnownLinkSecret, StringComparison.Ordinal) &&
                !sanitized.Contains("MyPassword123!", StringComparison.Ordinal) &&
                !sanitized.Contains(KnownTestOtp, StringComparison.Ordinal),
                $"sanitized={sanitized}");
        }

        // 10. TelegramPollingService exception in HandleUpdateAsync / loop -> logged and redacted
        {
            var logs = new CapturingLoggerProvider();
            var throwingScopeFactory = new ThrowingTelegramScopeFactory(KnownTestBotToken);
            var dict = new Dictionary<string, string?>
            {
                ["Telegram:BotToken"] = KnownTestBotToken,
                ["Telegram:DisablePolling"] = "false"
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
            var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));

            var pollingService = new TelegramPollingService(
                throwingScopeFactory,
                config,
                loggerFactory.CreateLogger<TelegramPollingService>());

            using var cts = new CancellationTokenSource();
            await pollingService.StartAsync(cts.Token);

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!logs.Entries.Any(e => e.Level == LogLevel.Warning && e.Category.Contains("TelegramPollingService")) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            cts.Cancel();
            await pollingService.StopAsync(CancellationToken.None);

            var dump = logs.DumpAllText();
            Assert(
                "10. TelegramPollingService polling loop HttpRequestException redacts bot token while preserving StatusCode and diagnostics",
                !dump.Contains(KnownTestBotToken, StringComparison.Ordinal) &&
                logs.Entries.Any(e => e.Category.Contains("TelegramPollingService") && e.Level == LogLevel.Warning) &&
                dump.Contains("bot[REDACTED]/getUpdates", StringComparison.Ordinal) &&
                dump.Contains("502", StringComparison.Ordinal),
                $"dump={dump}");
        }

        Console.WriteLine("=================================================");
        Console.WriteLine($"  SUMMARY: {passed} passed, {failed} failed");
        Console.WriteLine("=================================================");

        if (failed > 0)
        {
            Environment.ExitCode = 1;
        }
    }
}
