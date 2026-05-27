using System;
using System.Security.Cryptography;
using System.Text;

namespace ConditioningControlPanel.Services.Moderation
{
    /// <summary>
    /// Holds a per-launch random GUID used only to produce a short, opaque session id
    /// hash for <see cref="ModerationLog"/>. The hash is non-reversible — the only way
    /// to associate two log lines is to have a copy of this in-memory GUID. The GUID
    /// itself is never persisted.
    /// </summary>
    public sealed class ModerationSession
    {
        private readonly Guid _sessionGuid;
        private readonly string _sessionIdHash;

        public ModerationSession()
        {
            _sessionGuid = Guid.NewGuid();
            _sessionIdHash = ComputeShortHash(_sessionGuid.ToString("N"));
        }

        /// <summary>
        /// Short (8-hex-char) opaque identifier safe to write to disk. Cannot be
        /// reversed to the GUID.
        /// </summary>
        public string GetSessionIdHash() => _sessionIdHash;

        private static string ComputeShortHash(string input)
        {
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = SHA256.HashData(bytes);
            var sb = new StringBuilder(8);
            for (int i = 0; i < 4 && i < hash.Length; i++)
                sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }
    }
}
