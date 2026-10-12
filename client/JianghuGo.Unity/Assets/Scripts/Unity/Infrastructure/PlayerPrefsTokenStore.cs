using JianghuGo.Client.Infrastructure;
using UnityEngine;

namespace JianghuGo.Unity.Infrastructure
{
    /// <summary>
    /// DEVELOPMENT ONLY: PlayerPrefs is unencrypted. Before release replace with a keystore-backed store
    /// (Android Keystore / iOS Keychain) — tracked in docs/security/threat-model.md (T-ACC-4).
    /// </summary>
    public sealed class PlayerPrefsTokenStore : ITokenStore
    {
        private const string Key = "jianghu.refresh_token";

        public string LoadRefreshToken()
        {
            var value = PlayerPrefs.GetString(Key, string.Empty);
            return value.Length == 0 ? null : value;
        }

        public void SaveRefreshToken(string token)
        {
            PlayerPrefs.SetString(Key, token);
            PlayerPrefs.Save();
        }

        public void Clear()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }
    }
}
