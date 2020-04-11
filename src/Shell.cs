using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace NetShell
{
    public class Shell : IDisposable, IAutoCompleteHandler
    {

        /// <summary>
        /// Gets the default instance of the shell.
        /// </summary>
        public static Shell Instance { get; protected set; }

        /// <summary>
        /// Gets or sets the command history file.
        /// </summary>
        public string HistoryFile { get; set; }

        /// <summary>
        /// Gets or sets the command history limit.
        /// </summary>
        public int HistoryLimit { get; set; }

        /// <summary>
        /// The shell exits with this exit code.
        /// </summary>
        public int ExitCode { get; set; }

        /// <summary>
        /// Display this prompt before every command input
        /// </summary>
        public string Prompt { get; set; }

        /// <summary>
        /// Tokenize the input command with these separators.
        /// Defaults are space-separated parameters.
        /// </summary>
        /// <value>
        /// The separators.
        /// </value>
        public char[] Separators { get; set; }

        /// <summary>
        /// The default shell prompt
        /// </summary>
        public const string DefaultPrompt = ">";

        private string[] commands;

        /// <summary>
        /// Gets or sets the available list of commands.
        /// </summary>
        public IEnumerable<string> Commands
        {
            get => this.commands;
            set => this.commands = value.ToArray();
        }

        /// <summary>
        /// Gets the auto-complete suggestions for given text and cursor index.
        /// </summary>
        /// <param name="text">The command text.</param>
        /// <param name="index">Cursor index.</param>
        /// <returns></returns>
        public string[] GetSuggestions(string text, int index)
        {
            return RankSuggestions(this.commands, text, index)
                .Select(QuoteIfNeeded)
                .ToArray();
        }

        /// <summary>
        /// Ranks suggestions by proximity and edit distance.
        /// </summary>
        /// <param name="commands">The commands to tank.</param>
        /// <param name="text">The text.</param>
        /// <param name="index">Cursor index.</param>
        /// <returns></returns>
        public static IEnumerable<string> RankSuggestions(IEnumerable<string> commands, string text, int index)
        {
            var hint = text.Substring(index).Trim('-');
            if (String.IsNullOrWhiteSpace(hint))
                return commands;

            //get suggestions even if spelt wrongly           

            var suggestions = commands
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .Select(str => (distance: Rank(str.ToUpperInvariant(), hint.ToUpperInvariant()), str))
                .OrderBy(pair => pair.distance)
                .ThenBy(pair => pair.str.Length)        //debug order .Select(pair => { Trace.WriteLine(pair); return pair; })
                .Where(pair => pair.distance < pair.str.Length)
                .Select(pair => pair.str);

            return suggestions;
        }

        /// <summary>
        /// Ranks the specified string with another.
        /// </summary>
        /// <param name="str">The string.</param>
        /// <param name="hint">The hint.</param>
        /// <returns>0 to N, where 0 is the closest</returns>
        public static int Rank(string str, string hint)
        {
            var split = str.Trim('-').Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);

            if (split.Length > 1 && split.Zip(hint, (s, h) => s.StartsWith(h.ToString())).All(x => x))
                return 0;

            if (split.Any(s => s.StartsWith(hint)))
                return 0;

            if (str.EndsWith(hint))
                return 1;

            var distance = LevenshteinDistance(hint, str);
            return distance;
        }

        /// <summary>
        /// Quotes arguments if needed.
        /// </summary>
        public static string QuoteIfNeeded(string arg) =>
            arg.Contains(' ') && arg.IndexOf('"') != 0 ? $"\"{arg}\"" : arg;

        /// <summary>
        /// Humanizes the specified value.
        /// Converts 'InvalidOperation' to 'Invalid Operation'
        /// </summary>
        public static string Humanize(string value)
        {
            return Regex.Replace(value, "(?!^)([A-Z])", " $1");
        }

        /// <summary>
        /// Converts 'GetFile' to 'Get-File'
        /// </summary>
        public static string Hyphenize(string value)
        {
            return Regex.Replace(value, "(?!^)([A-Z])", "-$1");
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Shell"/> class.
        /// </summary>
        public Shell()
        {
            Instance = this;
            IsRunning = true;
            HistoryFile = $"{Assembly.GetEntryAssembly().Location}.rc";
            HistoryLimit = 100;
            Prompt = DefaultPrompt;
            Separators = new[] { ' ' };
        }

        /// <summary>
        /// Handles the cancel (Ctrl+C) key.
        /// </summary>
        public void HandleCancelKey()
        {
            Console.CancelKeyPress += OnCancelKeyPress;
        }

        public IEnumerable<string> GetHistory()
        {
            return ReadLine.GetHistory().AsEnumerable().Reverse();
        }

        /// <summary>
        /// Raised when when a line has been entered into the shell.
        /// </summary>
        public event Func<string, bool> Command;

        /// <summary>
        /// Gets a value indicating whether the shell instance is running.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is running; otherwise, <c>false</c>.
        /// </value>
        public bool IsRunning { get; private set; }

        /// <summary>
        /// Handles exit on [cancel key press].
        /// </summary>
        private void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            ConsoleUtil.Log(ConsoleColor.Green, "Press <return> to exit");
            e.Cancel = IsRunning;

            //Ctrl-C pressed again
            if (!IsRunning)
            {
                Environment.Exit(0);
            }

            IsRunning = false;
        }

        /// <summary>
        /// Gets or sets the current 'path' of the shell.
        /// </summary>
        public string CurrentPath { get; set; }

        /// <summary>
        /// Sets the current path from pieces.
        /// </summary>
        public void SetCurrentPath(IEnumerable<string> enumerable)
        {
            CurrentPath = string.Join(Separators.First().ToString(), enumerable);
        }

        /// <summary>
        /// Runs this instance with env command line args.
        /// </summary>
        public int Run()
        {
            return Run(Environment.GetCommandLineArgs().Skip(1).ToArray());
        }

        /// <summary>
        /// Runs with the specified arguments.
        /// </summary>
        public int Run(params string[] args)
        {
            if (File.Exists(HistoryFile))
            {
                LoadHistory();
            }

            if (Console.IsInputRedirected)
            {
                string line;
                while ((line = Console.In.ReadLine()) != null)
                {
                    ConsoleUtil.Log(ConsoleColor.Gray, $"> {line}");
                    OnCommand(line);
                }

                return ExitCode;
            }
            else if (args.Length > 0)
            {
                OnCommand(string.Join(" ", args)); //reparse args
                return ExitCode;
            }

            while (IsRunning)
            {
                try
                {
                    var prompt = String.IsNullOrEmpty(CurrentPath) ? $"{Prompt}>" : $"{Prompt}/{CurrentPath}>";
                    var input = ReadLine.Read(prompt);

                    if (!String.IsNullOrWhiteSpace(input))
                    {
                        OnCommand(input);
                        ReadLine.AddHistory(input);
                    }
                }
                catch (Exception ex)
                {
                    Trace.TraceError(ex.ToString());
                }
            }

            var history = ReadLine.GetHistory();
            var lines = history.AsEnumerable().Take(HistoryLimit);
            File.WriteAllLines(HistoryFile, lines, Encoding.UTF8);

            return ExitCode;
        }

        /// <summary>
        /// Loads the history async.
        /// </summary>
        private void LoadHistory()
        {
            Task.Factory.StartNew(() =>
            {
                ReadLine.AddHistory(File.ReadLines(HistoryFile).Reverse().Distinct().Reverse().ToArray());
            });
        }

        /// <summary>
        /// Raise the command event.
        /// </summary>
        protected bool? OnCommand(string input)
        {
            return Command?.Invoke(input);
        }

        /// <summary>
        /// Gets a value indicating whether this instance is disposed.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is disposed; otherwise, <c>false</c>.
        /// </value>
        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            IsRunning = false;
            IsDisposed = true;
            Console.CancelKeyPress -= OnCancelKeyPress;
        }

        /// <summary>
        /// Exits with the specified exit code.
        /// </summary>
        public void Exit(int exitCode = 0)
        {
            this.ExitCode = exitCode;
            IsRunning = false;
        }

        /// <summary>
        /// Calculate Levenshteins distance.
        /// </summary>
        static int LevenshteinDistance(string s, string t)
        {
            int n = s.Length;
            int m = t.Length;
            int[,] d = new int[n + 1, m + 1];

            // Step 1
            if (n == 0)
            {
                return m;
            }

            if (m == 0)
            {
                return n;
            }

            // Step 2
            for (int i = 0; i <= n; d[i, 0] = i++)
            {
            }

            for (int j = 0; j <= m; d[0, j] = j++)
            {
            }

            // Step 3
            for (int i = 1; i <= n; i++)
            {
                //Step 4
                for (int j = 1; j <= m; j++)
                {
                    // Step 5
                    int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;

                    // Step 6
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }
            // Step 7
            return d[n, m];
        }


        /// <summary>
        /// Returns a poller which cancels the token source when any key is pressed
        /// </summary>
        public IDisposable CancelOnKey(System.Threading.CancellationTokenSource cancellation, int waitAfter)
        {
            var timer = new Timer(_ =>
            {
                if (Console.KeyAvailable)
                {
                    Console.ReadKey();
                    cancellation.Cancel();
                }
            }, null, waitAfter, 100);

            cancellation.Token.Register(timer.Dispose);
            return timer;
        }


    }
}
