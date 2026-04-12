% LESS(1) Winix | User Commands
% Troy Willmot
% 2026-04-12

# NAME

less - terminal pager with ANSI colour passthrough, search, and follow mode

# SYNOPSIS

**less** [*options*] [*+command*] [*file* ...]

# DESCRIPTION

Displays file content (or stdin) one screen at a time. When no file is given, reads from stdin. Multiple files are paged in sequence.

ANSI escape codes are passed through unchanged by default, so coloured output from tools like **man**, **files**, and **treex** renders correctly.

# OPTIONS

**-N**
:   Show line numbers.

**-S**
:   Chop long lines instead of wrapping.

**-F**
:   Quit immediately if all content fits on one screen.

**-R**
:   Pass raw ANSI colour codes through unchanged (on by default unless **NO_COLOR** is set).

**-X**
:   Do not clear the screen on exit.

**-i**
:   Case-insensitive search, ignored when the pattern contains an uppercase letter.

**-I**
:   Case-insensitive search always, regardless of pattern case.

**+F**
:   Start in follow mode, like **tail -f**. Press **q** to exit follow mode.

**+G**
:   Jump to end of file on open.

**+/**_pattern_
:   Start with an initial forward search for _pattern_.

**--color**
:   Force coloured output, overriding **NO_COLOR**.

**--no-color**
:   Disable ANSI colour passthrough; raw escape sequences are displayed as text.

**--help**
:   Show help and exit.

**--version**
:   Show version and exit.

# KEY BINDINGS

**q**
:   Quit.

**j** / Down arrow
:   Scroll down one line.

**k** / Up arrow
:   Scroll up one line.

Space / PgDn
:   Scroll down one screen.

PgUp
:   Scroll up one screen.

Home / **g**
:   Jump to beginning.

End / **G**
:   Jump to end.

**/**
:   Forward search.

**?**
:   Backward search.

**n**
:   Next search match.

**N**
:   Previous search match.

**F**
:   Enter follow mode (press **q** to exit).

**-N**
:   Toggle line numbers.

**-S**
:   Toggle line chopping.

# EXIT CODES

**0**
:   Normal exit.

**1**
:   Error (file not found, read error).

**2**
:   Usage error (bad arguments).

# ENVIRONMENT

**LESS**
:   Controls default options. When unset or empty, Winix **less** uses modern defaults: **FRX** (quit-if-one-screen, raw colour, no-init). When set to a non-empty value, replaces the defaults entirely. Set **LESS=** explicitly to an empty string to disable all defaults.

**NO_COLOR**
:   If set, disables ANSI colour passthrough (no-color.org).

# EXAMPLES

    less somefile.txt

    man timeit | less

    less -N somefile.txt

    less -S somefile.txt

    less +F logfile.log

    less +G somefile.txt

    less +/error logfile.log

    less -F somefile.txt

# SEE ALSO

**man**(1), **peep**(1), **files**(1), **treex**(1)
