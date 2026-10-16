using System.Security.Cryptography;
using System.Text;

namespace TRobot.Application.Events;

internal static class RegistrationFingerprint
{
    // Fixed prefix separates the boolean from the exact, trimmed original name.
    public static string Compute(string displayName, bool enableSimulation) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes((enableSimulation ? "1:" : "0:") + displayName.Trim())));
}
