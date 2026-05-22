using System.Diagnostics;
using Spectre.Console;

while (true)
{
    AnsiConsole.Clear();
    AnsiConsole.Write(new Rule("[bold yellow]Robocopy Wrapper[/]").LeftJustified());
    AnsiConsole.WriteLine();

    // Source
    string source = AnsiConsole.Prompt(
        new TextPrompt<string>("[green]Source folder:[/]")
            .PromptStyle("cyan")
            .Validate(p =>
            {
                string cleaned = p.Trim('"', ' ');
                if (string.IsNullOrWhiteSpace(cleaned)) return ValidationResult.Error("[red]Path cannot be empty.[/]");
                if (!Directory.Exists(cleaned)) return ValidationResult.Error("[red]Directory does not exist.[/]");
                return ValidationResult.Success();
            })).Trim('"', ' ');

    // Target
    string target = AnsiConsole.Prompt(
        new TextPrompt<string>("[green]Target folder:[/]")
            .PromptStyle("cyan")
            .Validate(p => string.IsNullOrWhiteSpace(p.Trim('"', ' '))
                ? ValidationResult.Error("[red]Path cannot be empty.[/]")
                : ValidationResult.Success())).Trim('"', ' ');

    // Offer resolved target path (source name appended) as editable pre-filled input
    string sourceName = Path.GetFileName(source.TrimEnd('\\', '/'));
    if (!string.IsNullOrEmpty(sourceName))
    {
        string resolved = Path.Combine(target, sourceName);
        string edited;
        do
        {
            edited = ReadLineEditable("[cyan]Final target folder:[/] ", resolved).Trim('"', ' ');
            if (string.IsNullOrWhiteSpace(edited))
                AnsiConsole.MarkupLine("[red]Path cannot be empty.[/]");
        } while (string.IsNullOrWhiteSpace(edited));
        target = edited;
    }

    // Options
    bool move      = AnsiConsole.Confirm("Move files (delete from source after copy)?", defaultValue: true);
    bool recurse   = AnsiConsole.Confirm("Recurse into subdirectories [grey](/E[/])?", defaultValue: true);

    bool mt = AnsiConsole.Confirm("Use multithreading [grey](/MT[/])?", defaultValue: true);
    int threads = 128;
    if (mt)
    {
        threads = AnsiConsole.Prompt(
            new TextPrompt<int>("  Thread count [grey](1-128)[/]:")
                .DefaultValue(128)
                .Validate(n => n is >= 1 and <= 128
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Must be between 1 and 128.[/]")));
    }

    bool customRetries = AnsiConsole.Confirm("Customise retries?", defaultValue: false);
    int retryCount = 0, retryWait = 0;
    if (customRetries)
    {
        retryCount = AnsiConsole.Prompt(
            new TextPrompt<int>("  Retry count [grey](/R:n[/]):")
                .DefaultValue(3)
                .Validate(n => n >= 0 ? ValidationResult.Success() : ValidationResult.Error("[red]Must be >= 0.[/]")));

        retryWait = AnsiConsole.Prompt(
            new TextPrompt<int>("  Wait seconds between retries [grey](/W:n[/]):")
                .DefaultValue(5)
                .Validate(n => n >= 0 ? ValidationResult.Success() : ValidationResult.Error("[red]Must be >= 0.[/]")));
    }

    // Build argument list (runtime handles quoting via ArgumentList)
    var roboArgs = new List<string> { source, target };
    if (move)    roboArgs.Add("/MOVE");
    if (recurse) roboArgs.Add("/E");
    if (mt)      roboArgs.Add($"/MT:{threads}");
    if (customRetries)
    {
        roboArgs.Add($"/R:{retryCount}");
        roboArgs.Add($"/W:{retryWait}");
    }

    // Build display string (manually quote paths/args with space or special chars according to Windows CLI rules)
    static string Quote(string arg)
    {
        if (string.IsNullOrEmpty(arg)) return "\"\"";
        if (!arg.Contains(' ') && !arg.Contains('\"') && !arg.Contains('\t')) return arg;

        var sb = new System.Text.StringBuilder();
        sb.Append('"');
        for (int i = 0; i < arg.Length; i++)
        {
            int backslashCount = 0;
            while (i < arg.Length && arg[i] == '\\')
            {
                backslashCount++;
                i++;
            }

            if (i == arg.Length)
            {
                sb.Append('\\', backslashCount * 2);
                break;
            }
            else if (arg[i] == '"')
            {
                sb.Append('\\', backslashCount * 2 + 1);
                sb.Append('"');
            }
            else
            {
                sb.Append('\\', backslashCount);
                sb.Append(arg[i]);
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
    string display = "robocopy " + string.Join(" ", roboArgs.Select(Quote));

    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Panel($"[bold cyan]{Markup.Escape(display)}[/]")
        .Header("[grey]Command to run[/]")
        .BorderColor(Color.Grey));
    AnsiConsole.WriteLine();

    if (!AnsiConsole.Confirm("Execute?", defaultValue: true))
    {
        AnsiConsole.MarkupLine("[grey]Skipped.[/]");
    }
    else
    {
        try
        {
            var psi = new ProcessStartInfo("robocopy") { UseShellExecute = false };
            foreach (var arg in roboArgs) psi.ArgumentList.Add(arg);

            var proc = Process.Start(psi);
            if (proc == null)
            {
                AnsiConsole.MarkupLine("[bold red]Error:[/] Could not start robocopy process.");
                continue;
            }
            proc.WaitForExit();
            int code = proc.ExitCode;

            AnsiConsole.WriteLine();
            (string label, Color color) Interpret(int c) => c switch
            {
                0 => ("No files were copied. Source and destination are in sync.", Color.Green),
                1 => ("Files copied successfully.", Color.Green),
                2 => ("Extra files or directories detected in destination.", Color.Yellow),
                3 => ("Files copied; extra files detected.", Color.Yellow),
                4 => ("Some files or directories could not be copied (mismatched).", Color.Yellow),
                5 => ("Files copied; some mismatches detected.", Color.Yellow),
                6 => ("Extra files and mismatches; no copy was done.", Color.Yellow),
                7 => ("Files copied with some mismatches and extra files.", Color.Yellow),
                8 => ("Several files did not copy (check output/errors).", Color.Red),
                16 => ("Serious error. Robocopy did not copy any files.", Color.Red),
                _ => ($"Exit code {c}. Check output above.", c > 8 ? Color.Red : Color.Yellow)
            };

            var (msg, color) = Interpret(code);
            AnsiConsole.MarkupLine($"[bold {color}]Exit code {code}:[/] {Markup.Escape(msg)}");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[bold red]Failed to execute robocopy:[/] {Markup.Escape(ex.Message)}");
        }
    }

    AnsiConsole.WriteLine();
    if (!AnsiConsole.Confirm("Run another?", defaultValue: true))
        break;
}

AnsiConsole.MarkupLine("[grey]Bye.[/]");

static string ReadLineEditable(string label, string prefill)
{
    AnsiConsole.Markup(label);
    int startLeft = Console.CursorLeft;
    var buf       = new System.Text.StringBuilder(prefill);
    int cur       = prefill.Length;
    Console.Write(prefill);

    void MoveTo(int targetCur)
    {
        int width = Console.BufferWidth;
        int startTop = Console.CursorTop - (startLeft + cur) / width;
        int abs = startLeft + targetCur;
        Console.CursorTop  = startTop + abs / width;
        Console.CursorLeft = abs % width;
        cur = targetCur;
    }

    void Redraw(int oldLen, int targetCur)
    {
        int width = Console.BufferWidth;
        int startTop = Console.CursorTop - (startLeft + cur) / width;
        Console.CursorTop  = startTop;
        Console.CursorLeft = startLeft;
        Console.Write(buf.ToString());
        if (oldLen > buf.Length) 
            Console.Write(new string(' ', oldLen - buf.Length));
        
        int writeLen = Math.Max(buf.Length, oldLen);
        cur = writeLen;
        MoveTo(targetCur);
    }

    static int FindWordBoundaryLeft(string s, int start)
    {
        if (start <= 0) return 0;
        int idx = start - 1;
        while (idx > 0 && char.IsWhiteSpace(s[idx])) idx--;
        while (idx > 0 && !char.IsWhiteSpace(s[idx - 1])) idx--;
        return idx;
    }

    static int FindWordBoundaryRight(string s, int start)
    {
        if (start >= s.Length) return s.Length;
        int idx = start;
        while (idx < s.Length && !char.IsWhiteSpace(s[idx])) idx++;
        while (idx < s.Length && char.IsWhiteSpace(s[idx])) idx++;
        return idx;
    }

    while (true)
    {
        var k = Console.ReadKey(intercept: true);
        switch (k.Key)
        {
            case ConsoleKey.Enter:
                Console.WriteLine();
                return buf.ToString();
            case ConsoleKey.LeftArrow:
                if (k.Modifiers.HasFlag(ConsoleModifiers.Control))
                {
                    MoveTo(FindWordBoundaryLeft(buf.ToString(), cur));
                }
                else if (cur > 0)
                {
                    MoveTo(cur - 1);
                }
                break;
            case ConsoleKey.RightArrow:
                if (k.Modifiers.HasFlag(ConsoleModifiers.Control))
                {
                    MoveTo(FindWordBoundaryRight(buf.ToString(), cur));
                }
                else if (cur < buf.Length)
                {
                    MoveTo(cur + 1);
                }
                break;
            case ConsoleKey.Home:
                MoveTo(0);
                break;
            case ConsoleKey.End:
                MoveTo(buf.Length);
                break;
            case ConsoleKey.Backspace:
                if (cur > 0)
                {
                    int old = buf.Length;
                    int target = k.Modifiers.HasFlag(ConsoleModifiers.Control)
                        ? FindWordBoundaryLeft(buf.ToString(), cur)
                        : cur - 1;
                    buf.Remove(target, cur - target);
                    Redraw(old, target);
                }
                break;
            case ConsoleKey.Delete:
                if (cur < buf.Length)
                {
                    int old = buf.Length;
                    int target = k.Modifiers.HasFlag(ConsoleModifiers.Control)
                        ? FindWordBoundaryRight(buf.ToString(), cur)
                        : cur + 1;
                    buf.Remove(cur, target - cur);
                    Redraw(old, cur);
                }
                break;
            default:
                if (!char.IsControl(k.KeyChar))
                {
                    int old = buf.Length;
                    int target = cur + 1;
                    buf.Insert(cur, k.KeyChar);
                    Redraw(old, target);
                }
                break;
        }
    }
}
