// Faithful binary stdin -> stdout echo. Used as the pipe-mode child command in tests.
using System;
using var stdin = Console.OpenStandardInput();
using var stdout = Console.OpenStandardOutput();
stdin.CopyTo(stdout);
