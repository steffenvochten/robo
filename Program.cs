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
                if (string.IsNullOrWhiteSpace(p)) return ValidationResult.Error("[red]Path cannot be empty.[/]");
                if (!Directory.Exists(p)) return ValidationResult.Error("[red]Directory does not exist.[/]");
                return ValidationResult.Success();
            }));

    // Target
    string target = AnsiConsole.Prompt(
        new TextPrompt<string>("[green]Target folder:[/]")
            .PromptStyle("cyan")
            .Validate(p => string.IsNullOrWhiteSpace(p)
                ? ValidationResult.Error("[red]Path cannot be empty.[/]")
                : ValidationResult.Success()));

    // Offer resolved target path (source name appended) as editable pre-filled input
    string sourceName = Path.GetFileName(source.TrimEnd('\\', '/'));
    if (!string.IsNullOrEmpty(sourceName))
    {
        string resolved = Path.Combine(target, sourceName);
        string edited;
        do
        {
            edited = ReadLineEditable("[cyan]Final target folder:[/] ", resolved);
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

    // Build display string (manually quote paths/args with spaces)
    static string Quote(string s) => s.Contains(' ') ? $"\"{s}\"" : s;
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
        var psi = new ProcessStartInfo("robocopy") { UseShellExecute = false };
        foreach (var arg in roboArgs) psi.ArgumentList.Add(arg);

        var proc = Process.Start(psi)!;
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
            _ => ($"Error — exit code {c}. Check output above.", Color.Red)
        };

        var (msg, color) = Interpret(code);
        AnsiConsole.MarkupLine($"[bold {color}]Exit code {code}:[/] {Markup.Escape(msg)}");
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
    int startTop  = Console.CursorTop;
    int width     = Console.BufferWidth;
    var buf       = new System.Text.StringBuilder(prefill);
    int cur       = prefill.Length;
    Console.Write(prefill);

    void MoveTo(int offset)
    {
        int abs = startLeft + offset;
        Console.CursorTop  = startTop + abs / width;
        Console.CursorLeft = abs % width;
    }

    void Redraw(int oldLen)
    {
        Console.CursorTop  = startTop;
        Console.CursorLeft = startLeft;
        Console.Write(buf.ToString());
        if (oldLen > buf.Length) Console.Write(new string(' ', oldLen - buf.Length));
        MoveTo(cur);
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
                if (cur > 0) MoveTo(--cur);
                break;
            case ConsoleKey.RightArrow:
                if (cur < buf.Length) MoveTo(++cur);
                break;
            case ConsoleKey.Home:
                cur = 0; MoveTo(0);
                break;
            case ConsoleKey.End:
                cur = buf.Length; MoveTo(cur);
                break;
            case ConsoleKey.Backspace:
                if (cur > 0)
                {
                    int old = buf.Length;
                    buf.Remove(--cur, 1);
                    Redraw(old);
                }
                break;
            case ConsoleKey.Delete:
                if (cur < buf.Length)
                {
                    int old = buf.Length;
                    buf.Remove(cur, 1);
                    Redraw(old);
                }
                break;
            default:
                if (!char.IsControl(k.KeyChar))
                {
                    int old = buf.Length;
                    buf.Insert(cur++, k.KeyChar);
                    Redraw(old);
                }
                break;
        }
    }
}
