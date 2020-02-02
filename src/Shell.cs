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

        public static Shell Instance { get; set; }

        public string HistoryFile { get; set; }

        public int HistoryLimit { get; set; }

        public int ExitCode { get; set; }

        public string Prompt { get; set; }

        public char[] Separators { get; set; }

        public const string DefaultPrompt = ">";

        private string[] commands;
        public IEnumerable<string> Commands
        {
            get => this.commands;
            set
            {
                this.commands = value.ToArray();
                ReadLine.AutoCompletionHandler = this;
            }
        }

        public string[] GetSuggestions(string text, int index)
        {
            return RankSuggestions(this.commands, text, index)
                .Select(QuoteIfNeeded)
                .ToArray();
        }

        public static IEnumerable<string> RankSuggestions(IEnumerable<string> commands, string text, int index)
        {
            var hint = text.Substring(index);
            if (String.IsNullOrWhiteSpace(hint))
                return commands;

            //get suggestions even if spelt wrongly           

            var suggestions = commands
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .Select(str => (distance: Rank(str, hint), str))
                .OrderBy(pair => pair.distance)
                .Where(pair => pair.distance < pair.str.Length)
                .Select(pair => pair.str);

            return suggestions;
        }

        public static int Rank(string str, string hint)
        {
            var split = str.Split('-');
            if (split.Length > 1 && split.Zip(hint, (s, h) => s.StartsWith(h.ToString())).All(x => x))
                return 0;

            if (split.Any(s => s.StartsWith(hint)))
                return 0;

            if (str.EndsWith(hint))
                return 1;

            var distance = LevenshteinDistance(hint.ToUpperInvariant(), str.ToUpperInvariant());
            return distance;
        }

        public static string QuoteIfNeeded(string arg) => 
            arg.Contains(' ') && arg.IndexOf('"') != 0 ? $"\"{arg}\"" : arg;

        public static string Humanize(string value)
        {
            return Regex.Replace(value, "(?!^)([A-Z])", " $1");
        }

        public static string Hyphenize(string value)
        {
            return Regex.Replace(value, "(?!^)([A-Z])", "-$1");
        }

        public Shell()
        {
            Instance = this;
            IsRunning = true;
            HistoryFile = $"{Assembly.GetEntryAssembly().Location}.rc";
            HistoryLimit = 100;
            Prompt = DefaultPrompt;
            Separators = new[] { ' ' };
        }

        public void HandleCancelKey()
        {
            Console.CancelKeyPress += OnCancelKeyPress;
        }

        public event Func<string, bool> Command;

        public bool IsRunning { get; private set; }

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

        public string CurrentPath { get; set; }

        public void SetCurrentPath(IEnumerable<string> enumerable)
        {
            CurrentPath = string.Join(" ", enumerable);
        }

        public int Run()
        {
            return Run(Environment.GetCommandLineArgs().Skip(1).ToArray());
        }

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
            else
            {
                if (args.Length > 0)
                    OnCommand(string.Join(" ", args)); //reparse args
            }

            while (IsRunning)
            {
                var prompt = String.IsNullOrEmpty(CurrentPath) ? $"{Prompt}>" : $"{Prompt}/{CurrentPath}>";
                var input = ReadLine.Read(prompt);

                if (!String.IsNullOrWhiteSpace(input))
                {
                    OnCommand(input);
                    ReadLine.AddHistory(input);
                }
            }

            var history = ReadLine.GetHistory();
            var lines = history.AsEnumerable().Take(HistoryLimit);
            File.WriteAllLines(HistoryFile, lines, Encoding.UTF8);

            return ExitCode;
        }

        private void LoadHistory()
        {
            Task.Factory.StartNew(() =>
            {
                ReadLine.AddHistory(File.ReadLines(HistoryFile).Reverse().Distinct().Reverse().ToArray());
            });
        }

        private bool? OnCommand(string input)
        {
            return Command?.Invoke(input);
        }

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsRunning = false;
            IsDisposed = true;
            Console.CancelKeyPress -= OnCancelKeyPress;
        }

        public void Exit(int exitCode = 0)
        {
            this.ExitCode = exitCode;
            IsRunning = false;
        }

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
