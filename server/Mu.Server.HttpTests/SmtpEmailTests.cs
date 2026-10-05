using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Mu.Server;
using Mu.Server.Services;

internal static class SmtpEmailTests
{
    public static async Task<int> RunAsync()
    {
        var count = 0;
        void Check(bool value, string message) { count++; if (!value) throw new Exception(message); }
        foreach (var accept in new[] { true, false })
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var body = new StringBuilder();
            var server = Task.Run(async () =>
            {
                using var socket = await listener.AcceptTcpClientAsync(deadline.Token);
                await using var stream = socket.GetStream();
                using var reader = new StreamReader(stream);
                await using var writer = new StreamWriter(stream) { AutoFlush = true, NewLine = "\r\n" };
                await writer.WriteLineAsync("220 localhost ESMTP");
                while (await reader.ReadLineAsync(deadline.Token) is { } line)
                {
                    if (line.StartsWith("EHLO")) await writer.WriteLineAsync("250 localhost");
                    else if (line.StartsWith("MAIL FROM:")) await writer.WriteLineAsync("250 OK");
                    else if (line.StartsWith("RCPT TO:"))
                    {
                        await writer.WriteLineAsync(accept ? "250 OK" : "550 recipient rejected secret-response");
                        if (!accept) break;
                    }
                    else if (line == "DATA")
                    {
                        await writer.WriteLineAsync("354 End with dot");
                        while (await reader.ReadLineAsync(deadline.Token) is { } content && content != ".") body.AppendLine(content);
                        await writer.WriteLineAsync("250 queued");
                    }
                    else if (line == "QUIT") { await writer.WriteLineAsync("221 bye"); break; }
                    else await writer.WriteLineAsync("250 OK");
                }
            });
            var settings = new Dictionary<string, string?>
            {
                ["Smtp:Host"] = "127.0.0.1", ["Smtp:Port"] = port.ToString(),
                ["Smtp:From"] = "MU <noreply@example.test>", ["Smtp:StartTls"] = "false",
                ["Smtp:AllowPrivatePlaintext"] = "true"
            };
            var sender = new SmtpVerificationEmailSender(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
                NullLogger<SmtpVerificationEmailSender>.Instance);
            try
            {
                await sender.SendAsync("recipient@example.test", "123456", "register", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", deadline.Token);
                Check(accept, "Rejected SMTP recipient must fail");
            }
            catch (ApiException error)
            {
                Check(!accept && error.Error.Code == "email_unavailable", "SMTP rejection uses public stable error");
                Check(!error.Message.Contains("secret-response"), "Provider details stay private");
            }
            await server;
            if (accept)
            {
                Check(body.ToString().Contains("recipient@example.test"), "SMTP envelope and message submitted");
                Check(body.ToString().Contains("<mu.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa@example.test>"), "Message ID correlates submission and delivery without the code");
                var mime = body.ToString();
                var content = mime[(mime.IndexOf("\n\n", StringComparison.Ordinal) + 2)..];
                if (mime.Contains("Content-Transfer-Encoding: base64", StringComparison.OrdinalIgnoreCase))
                    content = Encoding.UTF8.GetString(Convert.FromBase64String(content));
                Check(content.Contains("123456"), "Verification code reaches MIME content");
            }
        }
        var unavailable = new SmtpVerificationEmailSender(new ConfigurationBuilder().Build(), NullLogger<SmtpVerificationEmailSender>.Instance);
        try { await unavailable.SendAsync("a@example.test", "123456", "register", "test"); throw new Exception("Missing SMTP accepted"); }
        catch (ApiException error) { Check(error.Error.Code == "email_unavailable", "Unconfigured SMTP fails closed"); }
        Console.WriteLine($"SMTP provider passed: {count} assertions.");
        return count;
    }
}
