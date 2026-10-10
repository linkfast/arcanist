using System.Security.Cryptography;
using System.Text;
using Arcanist.Core.Hosting;

namespace Arcanist.Core.Setup;

/// <summary>
/// One-time code that proves the person running the setup wizard has access to the server (its logs or data
/// directory). Only required for requests that do not come from the machine itself. Persisted, so it survives
/// restarts until setup completes; NAS packages and Docker images can surface it from the file.
/// </summary>
public sealed class SetupCode
{
    public const string HeaderName = "X-Setup-Code";
    public const string FileName = "setup-code.txt";

    // No 0/O or 1/I, so the code can be read aloud or copied by hand.
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly string _filePath;
    private readonly Lazy<string> _value;

    public SetupCode(ArcanistPaths paths)
    {
        _filePath = Path.Combine(paths.Root, FileName);
        _value = new Lazy<string>(LoadOrCreate);
    }

    public string Value => _value.Value;

    public bool Matches(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        var expected = Encoding.ASCII.GetBytes(Canonical(Value));
        var actual = Encoding.ASCII.GetBytes(Canonical(candidate));
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public void Discard()
    {
        if (File.Exists(_filePath))
            File.Delete(_filePath);
    }

    private string LoadOrCreate()
    {
        if (File.Exists(_filePath) && File.ReadAllText(_filePath).Trim() is { Length: > 0 } existing)
            return existing;

        var chars = RandomNumberGenerator.GetItems<char>(Alphabet, 8);
        var code = $"{new string(chars, 0, 4)}-{new string(chars, 4, 4)}";
        File.WriteAllText(_filePath, code);
        return code;
    }

    private static string Canonical(string code) =>
        new(code.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
