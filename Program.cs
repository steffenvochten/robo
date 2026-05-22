using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;

while (true)
{
    AnsiConsole.Clear();
    AnsiConsole.Write(new Rule("[bold yellow]Robocopy Wrapper[/]").LeftJustified());
    AnsiConsole.WriteLine();

    var history = HistoryManager.Load();
    HistoryManager.HistoryItem? selectedHistoryItem = null;

    if (history.Count > 0)
    {
        var choices = new List<string> { "Configure a new Robocopy task" };
        foreach (var item in history)
        {
            choices.Add($"Run: {item.Source} -> {item.Target} ({(item.Move ? "Move" : "Copy")}{(item.Recurse ? ", Recurse" : "")})");
        }
        choices.Add("Clear history");
        choices.Add("Exit");

        var selection = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[yellow]Welcome to Robocopy Wrapper! Select an option:[/]")
                .PageSize(10)
                .AddChoices(choices));

        if (selection == "Exit")
        {
            break;
        }
        else if (selection == "Clear history")
        {
            HistoryManager.Save(new List<HistoryManager.HistoryItem>());
            AnsiConsole.MarkupLine("[green]History cleared![/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Press Enter to continue...[/]");
            Console.ReadLine();
            continue;
        }
        else if (selection != "Configure a new Robocopy task")
        {
            int index = choices.IndexOf(selection) - 1;
            if (index >= 0 && index < history.Count)
            {
                selectedHistoryItem = history[index];
            }
        }
    }

    string initialSource = selectedHistoryItem?.Source ?? "";
    string initialTarget = selectedHistoryItem?.Target ?? "";
    bool initialMove = selectedHistoryItem?.Move ?? true;
    bool initialRecurse = selectedHistoryItem?.Recurse ?? true;
    bool initialMt = selectedHistoryItem?.Mt ?? true;
    int initialThreads = selectedHistoryItem?.Threads ?? 128;
    bool initialCustomRetries = selectedHistoryItem?.CustomRetries ?? false;
    int initialRetryCount = selectedHistoryItem?.RetryCount ?? 3;
    int initialRetryWait = selectedHistoryItem?.RetryWait ?? 5;

    bool runDirectly = false;
    if (selectedHistoryItem != null)
    {
        var action = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"[yellow]Past job selected. What would you like to do?[/]")
                .AddChoices("Run directly", "Edit configuration", "Cancel"));

        if (action == "Cancel")
        {
            continue;
        }
        else if (action == "Run directly")
        {
            runDirectly = true;
        }
    }

    string source = initialSource;
    string target = initialTarget;
    bool move = initialMove;
    bool recurse = initialRecurse;
    bool mt = initialMt;
    int threads = initialThreads;
    bool customRetries = initialCustomRetries;
    int retryCount = initialRetryCount;
    int retryWait = initialRetryWait;

    if (!runDirectly)
    {
        // Source
        var sourcePrompt = new TextPrompt<string>("[green]Source folder:[/]")
            .PromptStyle("cyan")
            .Validate(p =>
            {
                string cleaned = p.Trim('"', ' ');
                if (string.IsNullOrWhiteSpace(cleaned)) return ValidationResult.Error("[red]Path cannot be empty.[/]");
                if (!Directory.Exists(cleaned)) return ValidationResult.Error("[red]Directory does not exist.[/]");
                return ValidationResult.Success();
            });
        if (!string.IsNullOrEmpty(initialSource))
        {
            sourcePrompt.DefaultValue(initialSource);
        }
        source = AnsiConsole.Prompt(sourcePrompt).Trim('"', ' ');

        // Target
        var targetPrompt = new TextPrompt<string>("[green]Target folder:[/]")
            .PromptStyle("cyan")
            .Validate(p => string.IsNullOrWhiteSpace(p.Trim('"', ' '))
                ? ValidationResult.Error("[red]Path cannot be empty.[/]")
                : ValidationResult.Success());
        if (!string.IsNullOrEmpty(initialTarget))
        {
            targetPrompt.DefaultValue(initialTarget);
        }
        target = AnsiConsole.Prompt(targetPrompt).Trim('"', ' ');

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
        move      = AnsiConsole.Confirm("Move files (delete from source after copy)?", defaultValue: initialMove);
        recurse   = AnsiConsole.Confirm("Recurse into subdirectories [grey](/E[/])?", defaultValue: initialRecurse);
        mt        = AnsiConsole.Confirm("Use multithreading [grey](/MT[/])?", defaultValue: initialMt);

        if (mt)
        {
            threads = AnsiConsole.Prompt(
                new TextPrompt<int>("  Thread count [grey](1-128)[/]:")
                    .DefaultValue(initialThreads)
                    .Validate(n => n is >= 1 and <= 128
                        ? ValidationResult.Success()
                        : ValidationResult.Error("[red]Must be between 1 and 128.[/]")));
        }

        customRetries = AnsiConsole.Confirm("Customise retries?", defaultValue: initialCustomRetries);
        if (customRetries)
        {
            retryCount = AnsiConsole.Prompt(
                new TextPrompt<int>("  Retry count [grey](/R:n[/]):")
                    .DefaultValue(initialRetryCount)
                    .Validate(n => n >= 0 ? ValidationResult.Success() : ValidationResult.Error("[red]Must be >= 0.[/]")));

            retryWait = AnsiConsole.Prompt(
                new TextPrompt<int>("  Wait seconds between retries [grey](/W:n[/]):")
                    .DefaultValue(initialRetryWait)
                    .Validate(n => n >= 0 ? ValidationResult.Success() : ValidationResult.Error("[red]Must be >= 0.[/]")));
        }
    }

    // Save to history
    HistoryManager.Add(source, target, move, recurse, mt, threads, customRetries, retryCount, retryWait);

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
            var psi = new ProcessStartInfo("robocopy")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var arg in roboArgs) psi.ArgumentList.Add(arg);

            var proc = Process.Start(psi);
            if (proc == null)
            {
                AnsiConsole.MarkupLine("[bold red]Error:[/] Could not start robocopy process.");
                continue;
            }

            var outputLines = new List<string>();
            var sbLine = new System.Text.StringBuilder();

            // Read output character-by-character to preserve real-time updates and carriage returns
            while (true)
            {
                int ch = proc.StandardOutput.Read();
                if (ch == -1) break;

                char c = (char)ch;
                Console.Write(c);

                if (c == '\r' || c == '\n')
                {
                    if (sbLine.Length > 0)
                    {
                        outputLines.Add(sbLine.ToString());
                        sbLine.Clear();
                    }
                }
                else
                {
                    sbLine.Append(c);
                }
            }
            if (sbLine.Length > 0)
            {
                outputLines.Add(sbLine.ToString());
            }

            // Consume and display errors if any
            string err = proc.StandardError.ReadToEnd();
            if (!string.IsNullOrEmpty(err))
            {
                Console.Write(err);
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

            // Parse and render the beautiful summary table
            DisplayParsedSummary(outputLines);
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

static void DisplayParsedSummary(List<string> lines)
{
    List<string>? dirs = null;
    List<string>? files = null;
    List<string>? bytes = null;

    foreach (var line in lines)
    {
        if (line.Contains("Dirs :") || line.Contains("Dirs:"))
        {
            dirs = ParseSummaryRow(line);
        }
        else if (line.Contains("Files :") || line.Contains("Files:"))
        {
            files = ParseSummaryRow(line);
        }
        else if (line.Contains("Bytes :") || line.Contains("Bytes:"))
        {
            bytes = ParseSummaryRow(line);
        }
    }

    if (dirs == null || files == null || bytes == null)
    {
        return;
    }

    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule("[bold yellow]Transfer Summary[/]").LeftJustified());
    AnsiConsole.WriteLine();

    var table = new Table();
    table.Border(TableBorder.Rounded);
    table.AddColumn("[bold]Category[/]");
    table.AddColumn("[bold blue]Total[/]");
    table.AddColumn("[bold green]Copied[/]");
    table.AddColumn("[bold yellow]Skipped[/]");
    table.AddColumn("[bold red]Mismatch[/]");
    table.AddColumn("[bold red]Failed[/]");
    table.AddColumn("[bold yellow]Extras[/]");

    table.AddRow("Directories", dirs[0], dirs[1], dirs[2], dirs[3], dirs[4], dirs[5]);
    table.AddRow("Files", files[0], files[1], files[2], files[3], files[4], files[5]);
    table.AddRow("Bytes", bytes[0], bytes[1], bytes[2], bytes[3], bytes[4], bytes[5]);

    AnsiConsole.Write(table);

    if (long.TryParse(files[4], out long failedFiles) && failedFiles > 0)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel($"[bold red]Warning: {failedFiles} file(s) failed to copy. Check the output logs above for details.[/]")
            .BorderColor(Color.Red));
    }
    else
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel("[bold green]Success: Copy completed with 0 errors.[/]")
            .BorderColor(Color.Green));
    }
    AnsiConsole.WriteLine();
}

static List<string> ParseSummaryRow(string line)
{
    var idx = line.IndexOf(':');
    var dataPart = idx != -1 ? line.Substring(idx + 1) : line;

    var rawTokens = dataPart.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToList();
    var result = new List<string>();

    for (int i = 0; i < rawTokens.Count; i++)
    {
        string current = rawTokens[i];
        if (i + 1 < rawTokens.Count)
        {
            string next = rawTokens[i + 1].ToLowerInvariant();
            if (next is "k" or "m" or "g" or "t")
            {
                current = $"{current} {rawTokens[i + 1]}";
                i++;
            }
        }
        result.Add(current);
    }

    while (result.Count < 6)
    {
        result.Add("0");
    }
    return result.Take(6).ToList();
}

[JsonSerializable(typeof(List<HistoryManager.HistoryItem>))]
internal partial class HistoryItemContext : JsonSerializerContext
{
}

public static class HistoryManager
{
    private static readonly string FolderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RoboWrapper"
    );
    private static readonly string FilePath = Path.Combine(FolderPath, "history.json");

    public class HistoryItem
    {
        public string Source { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public bool Move { get; set; }
        public bool Recurse { get; set; }
        public bool Mt { get; set; }
        public int Threads { get; set; }
        public bool CustomRetries { get; set; }
        public int RetryCount { get; set; }
        public int RetryWait { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public static List<HistoryItem> Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                string json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize(json, HistoryItemContext.Default.ListHistoryItem) ?? new List<HistoryItem>();
            }
        }
        catch
        {
            // Ignore loading errors
        }
        return new List<HistoryItem>();
    }

    public static void Save(List<HistoryItem> items)
    {
        try
        {
            Directory.CreateDirectory(FolderPath);
            var options = new JsonSerializerOptions { WriteIndented = true };
            var context = new HistoryItemContext(options);
            string json = JsonSerializer.Serialize(items, context.ListHistoryItem);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Ignore saving errors
        }
    }

    public static void Add(string source, string target, bool move, bool recurse, bool mt, int threads, bool customRetries, int retryCount, int retryWait)
    {
        var items = Load();
        items.RemoveAll(x => string.Equals(x.Source, source, StringComparison.OrdinalIgnoreCase) && 
                             string.Equals(x.Target, target, StringComparison.OrdinalIgnoreCase) &&
                             x.Move == move &&
                             x.Recurse == recurse &&
                             x.Mt == mt &&
                             x.Threads == threads &&
                             x.CustomRetries == customRetries &&
                             x.RetryCount == retryCount &&
                             x.RetryWait == retryWait);

        items.Insert(0, new HistoryItem
        {
            Source = source,
            Target = target,
            Move = move,
            Recurse = recurse,
            Mt = mt,
            Threads = threads,
            CustomRetries = customRetries,
            RetryCount = retryCount,
            RetryWait = retryWait,
            Timestamp = DateTime.Now
        });

        if (items.Count > 10)
        {
            items = items.Take(10).ToList();
        }

        Save(items);
    }
}
