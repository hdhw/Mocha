using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Mocha2023.Classes
{
    // RFC 6238 TOTP (30s step, 6 digits, SHA1) implemented without a third-party
    // package since this checkout has no .csproj to add one to. Compatible with
    // Google Authenticator / Authy / any standard authenticator app.
    public static class TotpService
    {
        private const int StepSeconds = 30;
        private const int Digits = 6;
        private const int SecretBytes = 20;
        private const int RecoveryCodeCount = 8;

        public static string GenerateSecret()
        {
            return Base32Encode(RandomNumberGenerator.GetBytes(SecretBytes));
        }

        public static string GetProvisioningUri(
            string secret,
            string accountLabel,
            string issuer = "Mocha")
        {
            string label = Uri.EscapeDataString($"{issuer}:{accountLabel}");
            string issuerEncoded = Uri.EscapeDataString(issuer);
            return $"otpauth://totp/{label}?secret={secret}&issuer={issuerEncoded}" +
                   $"&algorithm=SHA1&digits={Digits}&period={StepSeconds}";
        }

        public static bool ValidateCode(
            string? secret,
            string? code,
            int windowSteps = 1)
        {
            if (string.IsNullOrWhiteSpace(secret) ||
                string.IsNullOrWhiteSpace(code))
            {
                return false;
            }

            code = code.Trim();
            if (code.Length != Digits || !code.All(char.IsDigit))
            {
                return false;
            }

            byte[] key = Base32Decode(secret);
            long currentStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / StepSeconds;

            for (int offset = -windowSteps; offset <= windowSteps; offset++)
            {
                string candidate = ComputeCode(key, currentStep + offset);
                if (CryptographicOperations.FixedTimeEquals(
                        Encoding.ASCII.GetBytes(candidate),
                        Encoding.ASCII.GetBytes(code)))
                {
                    return true;
                }
            }

            return false;
        }

        public static List<string> GenerateRecoveryCodes()
        {
            var codes = new List<string>(RecoveryCodeCount);
            for (int i = 0; i < RecoveryCodeCount; i++)
            {
                byte[] bytes = RandomNumberGenerator.GetBytes(5);
                string code = Convert.ToHexString(bytes).ToLowerInvariant();
                codes.Add($"{code[..5]}-{code[5..]}");
            }

            return codes;
        }

        private static string ComputeCode(byte[] key, long counter)
        {
            byte[] counterBytes = BitConverter.GetBytes(counter);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(counterBytes);
            }

            byte[] hash = new HMACSHA1(key).ComputeHash(counterBytes);

            int offset = hash[^1] & 0x0F;
            int binary =
                ((hash[offset] & 0x7F) << 24) |
                ((hash[offset + 1] & 0xFF) << 16) |
                ((hash[offset + 2] & 0xFF) << 8) |
                (hash[offset + 3] & 0xFF);

            int truncated = binary % (int)Math.Pow(10, Digits);
            return truncated.ToString().PadLeft(Digits, '0');
        }

        private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        private static string Base32Encode(byte[] data)
        {
            var result = new StringBuilder((data.Length * 8 + 4) / 5);
            int buffer = 0;
            int bitsLeft = 0;

            foreach (byte b in data)
            {
                buffer = (buffer << 8) | b;
                bitsLeft += 8;
                while (bitsLeft >= 5)
                {
                    bitsLeft -= 5;
                    result.Append(Base32Alphabet[(buffer >> bitsLeft) & 0x1F]);
                }
            }

            if (bitsLeft > 0)
            {
                result.Append(Base32Alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);
            }

            return result.ToString();
        }

        private static byte[] Base32Decode(string input)
        {
            input = input.Trim().TrimEnd('=').ToUpperInvariant();
            var bytes = new List<byte>(input.Length * 5 / 8);
            int buffer = 0;
            int bitsLeft = 0;

            foreach (char c in input)
            {
                int value = Base32Alphabet.IndexOf(c);
                if (value < 0)
                {
                    continue;
                }

                buffer = (buffer << 5) | value;
                bitsLeft += 5;
                if (bitsLeft >= 8)
                {
                    bitsLeft -= 8;
                    bytes.Add((byte)((buffer >> bitsLeft) & 0xFF));
                }
            }

            return bytes.ToArray();
        }
    }
}
