using Xunit;

namespace Yort.ShellKit.Tests;

/// <summary>
/// Collection definition that serialises test classes which redirect <see cref="System.Console.Out"/>
/// via <c>Console.SetOut</c>. Without this, parallel test execution can race on the shared
/// static console output stream.
/// </summary>
[CollectionDefinition("ConsoleOutput")]
public class ConsoleOutputCollection
{
}
