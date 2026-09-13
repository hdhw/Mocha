using Microsoft.AspNetCore.Mvc;
using Mocha2023.Auth;
using Mocha2023.Classes.DBs;
using Mocha2023.Classes.DBs.DBClasses;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Mocha2023.Controllers
{
    [ApiController]
    [Route("recnet/api")]
    public class BotAdminController : ControllerBase
    {
        private sealed record LinkCode(long AccountId, string Username, DateTime ExpiresAt);

        private static readonly ConcurrentDictionary<string, LinkCode> LinkCodes = new();

        private const string DefaultSecret = "M0cha-br1dge-7Qv2mZp8Lr4Tn6Ws1Yb5Hd0Ja";

        private static string Secret =>
            Environment.GetEnvironmentVariable("BOT_ADMIN_SECRET") is { Length: > 0 } s ? s : DefaultSecret;

        private bool BadSecret() =>
            string.IsNullOrEmpty(Secret) ||
            !string.Equals(Request.Headers["X-Bot-Secret"], Secret, StringComparison.Ordinal);

        [HttpGet("link/discord/new")]
        public IActionResult NewLinkCode()
        {
            var account = AuthStuff.GetCurrentPlayer(Request);
            if (account == null)
                return Unauthorized(new { error = "Log in first." });

            var code = GenerateCode();
            LinkCodes[code] = new LinkCode(account.PlayerId, account.Player?.Username ?? account.PlayerId.ToString(),
                DateTime.UtcNow.AddMinutes(10));
            PruneCodes();
            return Ok(new { code, expiresInSeconds = 600 });
        }

        [HttpPost("bot/verify-link")]
        public IActionResult VerifyLink([FromBody] VerifyLinkRequest body)
        {
            if (BadSecret())
                return StatusCode(403, new { error = "bad secret" });
            if (body == null || string.IsNullOrWhiteSpace(body.Code))
                return BadRequest(new { error = "missing code" });

            var code = body.Code.Trim().ToUpperInvariant();
            if (!LinkCodes.TryRemove(code, out var entry) || entry.ExpiresAt < DateTime.UtcNow)
                return BadRequest(new { error = "That code is invalid or expired." });

            return Ok(new { accountId = entry.AccountId, username = entry.Username });
        }

        [HttpPost("bot/edit-profile")]
        public IActionResult EditProfile([FromBody] EditProfileRequest body)
        {
            if (BadSecret())
                return StatusCode(403, new { error = "bad secret" });
            if (body == null)
                return BadRequest(new { error = "missing body" });

            var target = PlayerDB.Players.FindById(body.TargetAccountId);
            if (target == null)
                return NotFound(new { error = $"No account {body.TargetAccountId}." });

            target.Player ??= new PlayerDBClasses.Player();
            if (!string.IsNullOrWhiteSpace(body.DisplayName))
                target.Player.DisplayName = body.DisplayName.Trim();
            if (body.Bio != null)
                target.Player.Bio = body.Bio;

            PlayerDB.Players.Update(target);
            return Ok(new { ok = true, displayName = target.Player.DisplayName, bio = target.Player.Bio });
        }

        private static string GenerateCode()
        {
            const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
            Span<byte> bytes = stackalloc byte[6];
            RandomNumberGenerator.Fill(bytes);
            var chars = new char[6];
            for (var i = 0; i < 6; i++)
                chars[i] = alphabet[bytes[i] % alphabet.Length];
            return new string(chars);
        }

        private static void PruneCodes()
        {
            var now = DateTime.UtcNow;
            foreach (var pair in LinkCodes)
                if (pair.Value.ExpiresAt < now)
                    LinkCodes.TryRemove(pair.Key, out _);
        }

        public sealed class VerifyLinkRequest
        {
            public string Code { get; set; } = "";
            public string DiscordId { get; set; } = "";
        }

        public sealed class EditProfileRequest
        {
            public string ActorDiscordId { get; set; } = "";
            public long ActorMochaId { get; set; }
            public long TargetAccountId { get; set; }
            public string? DisplayName { get; set; }
            public string? Bio { get; set; }
        }
    }
}
