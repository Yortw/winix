using System.Text;

namespace Winix.Wargs;

/// <summary>
/// Builds <see cref="CommandInvocation"/>s from a command template and input items.
/// If any template argument contains <c>{}</c>, substitution mode is used (each <c>{}</c>
/// is replaced with the item). Otherwise, items are appended as additional arguments.
/// </summary>
public sealed class CommandBuilder
{
    private const string Placeholder = "{}";

    private readonly string[] _template;
    private readonly int _batchSize;

    /// <summary>
    /// Creates a new command builder.
    /// </summary>
    /// <param name="template">
    /// The command template from trailing CLI args. If empty, defaults to <c>echo</c>.
    /// First element is the command, remainder are argument templates.
    /// </param>
    /// <param name="batchSize">Number of items per invocation (default 1).</param>
    public CommandBuilder(string[] template, int batchSize = 1)
    {
        if (template.Length == 0)
        {
            template = new[] { "echo" };
        }

        _template = template;
        _batchSize = batchSize;
        IsSubstitutionMode = template.Skip(1).Any(arg => arg.Contains(Placeholder, StringComparison.Ordinal));
    }

    /// <summary>True if the template contains <c>{}</c> placeholders.</summary>
    public bool IsSubstitutionMode { get; }

    /// <summary>
    /// Builds command invocations from the input items.
    /// </summary>
    public IEnumerable<CommandInvocation> Build(IEnumerable<string> items)
    {
        var batch = new List<string>(_batchSize);

        foreach (string item in items)
        {
            batch.Add(item);
            if (batch.Count >= _batchSize)
            {
                yield return BuildOne(batch.ToArray());
                batch.Clear();
            }
        }

        // Emit remainder
        if (batch.Count > 0)
        {
            yield return BuildOne(batch.ToArray());
        }
    }

    private CommandInvocation BuildOne(string[] sourceItems)
    {
        string command = _template[0];
        string[] templateArgs = _template.AsSpan(1).ToArray();
        string[] arguments;

        if (IsSubstitutionMode)
        {
            string replacement = string.Join(" ", sourceItems);
            arguments = new string[templateArgs.Length];
            for (int i = 0; i < templateArgs.Length; i++)
            {
                arguments[i] = templateArgs[i].Replace(Placeholder, replacement, StringComparison.Ordinal);
            }
        }
        else
        {
            arguments = new string[templateArgs.Length + sourceItems.Length];
            templateArgs.CopyTo(arguments, 0);
            sourceItems.CopyTo(arguments, templateArgs.Length);
        }

        string displayString = FormatDisplayString(command, arguments);
        return new CommandInvocation(command, arguments, displayString, sourceItems);
    }

    /// <summary>
    /// Formats a command and arguments into a human-readable, shell-quoted string.
    /// </summary>
    internal static string FormatDisplayString(string command, string[] arguments)
    {
        var sb = new StringBuilder();
        sb.Append(ShellQuote(command));

        foreach (string arg in arguments)
        {
            sb.Append(' ');
            sb.Append(ShellQuote(arg));
        }

        return sb.ToString();
    }

    private static string ShellQuote(string value)
    {
        if (value.Length == 0)
        {
            return "''";
        }

        // If the value contains no special characters, no quoting needed
        bool needsQuoting = false;
        foreach (char c in value)
        {
            if (c == ' ' || c == '\t' || c == '"' || c == '\'' || c == '\\' || c == '|'
                || c == '&' || c == ';' || c == '(' || c == ')' || c == '<' || c == '>'
                || c == '$' || c == '`' || c == '!' || c == '{' || c == '}')
            {
                needsQuoting = true;
                break;
            }
        }

        if (!needsQuoting)
        {
            return value;
        }

        // Use single quotes — safest for display. Escape embedded single quotes.
        return "'" + value.Replace("'", "'\\''") + "'";
    }
}
