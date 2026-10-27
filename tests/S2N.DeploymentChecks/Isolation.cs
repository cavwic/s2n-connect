using S2N.Models;

namespace S2N.Services
{
    public sealed class SettingsStore
    {
        // Deployment parser checks never write user settings or credentials.
        public Task SaveVpsProfileAsync(VpsProfile profile, string path,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

namespace S2N.Localization
{
    public static class L10n
    {
        public static string T(string chinese, string english) => english;
        public static string TranslateTechnicalMessage(string text) => text;
    }
}
