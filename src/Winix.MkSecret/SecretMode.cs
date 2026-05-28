namespace Winix.MkSecret;

/// <summary>The three generation modes, selected by the first positional subcommand.</summary>
public enum SecretMode
{
    /// <summary>Random-character password.</summary>
    Password,
    /// <summary>Diceware passphrase from the EFF long wordlist.</summary>
    Phrase,
    /// <summary>Encoded high-entropy random bytes (machine secret / key).</summary>
    Key,
}
