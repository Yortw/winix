// Test fixture child command for hcat pipe mode.
//   (no args)            : faithful binary stdin -> stdout echo (the default; most pipe tests use this).
//   --no-read            : write "ok" to stdout and exit 0 WITHOUT reading stdin. Reproduces the case where
//                          a clean CGI command ignores its request body; with a body larger than the OS pipe
//                          buffer the server's stdin feed then faults with a broken pipe — which must NOT be
//                          surfaced as a 500.
//   --fail-after-output  : write to stdout (committing a 200) then exit non-zero. Exercises the "exit code
//                          can't downgrade an already-committed response" path.
using System;
using System.Text;

if (args.Length > 0 && args[0] == "--no-read")
{
    using var so = Console.OpenStandardOutput();
    byte[] ok = Encoding.ASCII.GetBytes("ok");
    so.Write(ok, 0, ok.Length);
    return 0;
}

if (args.Length > 0 && args[0] == "--fail-after-output")
{
    using var so = Console.OpenStandardOutput();
    byte[] partial = Encoding.ASCII.GetBytes("partial-output");
    so.Write(partial, 0, partial.Length);
    so.Flush();
    return 7;
}

using var stdin = Console.OpenStandardInput();
using var stdout = Console.OpenStandardOutput();
stdin.CopyTo(stdout);
return 0;
