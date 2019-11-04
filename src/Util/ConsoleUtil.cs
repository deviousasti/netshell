using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NetShell
{
    public static class ConsoleUtil
    {
        public static bool IsInteractive { get; set; } = Environment.UserInteractive;

        public static void SetInteractive()
        {
            IsInteractive = true;
        }

        public static string ReplaceWithEnvVariables(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            return Regex.Replace(text, @"%(\w*)%", m => Environment.GetEnvironmentVariable(m.Groups[1].Value));
        }

        public static void LogAtCursor(ConsoleColor forecolor, string text)
        {
            Console.ForegroundColor = forecolor;
            Console.Write(text);
            Console.ResetColor();
        }

        public static void LogAtCursor(ConsoleColor backcolor, ConsoleColor forecolor, string text)
        {
            Console.BackgroundColor = backcolor;
            LogAtCursor(forecolor, text);
        }

        public static void Log(ConsoleColor backcolor, ConsoleColor forecolor, string text)
        {
            Console.BackgroundColor = backcolor;
            Log(forecolor, text);
        }

        public static void Log(ConsoleColor forecolor, string text)
        {
            Console.ForegroundColor = forecolor;
            Console.WriteLine(text);
            Console.ResetColor();
        }

        public static void Log()
        {
            Console.WriteLine();
        }

        public static void Warn(string text)
        {
            Log(ConsoleColor.DarkYellow, text: text);
        }

        public static void Error(string text)
        {
            Log(ConsoleColor.Red, text);
        }

        public static void Error(System.Exception ex)
        {
            Error(ex.Message);
            Error(ex.StackTrace);

            if (ex.InnerException != null)
                Error(ex.InnerException);
        }

        public static void Log(string text)
        {
            Log(ConsoleColor.Gray, text);
        }

        public static bool Confirm(string text = "Proceed?")
        {
            Console.WriteLine("{0} (y/n)", text);
            return Console.ReadKey().Key == ConsoleKey.Y;
        }

        public static void WaitForAnyKey()
        {
            if (Console.KeyAvailable) Console.ReadKey();
            Log("Press any key to exit...");
            Console.ReadKey();
        }

        public static void ShowPercentProgress(string message, int present, int total)
        {
            if (present < 0 || present > total)
            {
                throw new InvalidOperationException("Out of range");
            }

            int percent = (100 * (present + 1)) / total;

            ShowPercentProgress(message, percent);
        }

        public static void ShowPercentProgress(string message, int percent)
        {
            Console.Write("\r{0} {1}%", message, percent);

            if (percent == 100)
                Console.WriteLine();
        }

        public static void ShowInlineProgressBar(string message, int percent, ConsoleColor color = ConsoleColor.Gray)
        {
            if (!IsInteractive)
                return;

            const string format = "{0} [{1}{2}]\r";
            int max = Console.WindowWidth - message.Length - format.Length;

            int scaled = percent * max / 100;

            if (scaled > max)
                scaled = max;

            Log(color, string.Format(format, message, new string(Characters.FullBlock, scaled), new string('.', max - scaled)));
        }

        public static void ShowProgressBar(string message, int percent, ConsoleColor color = ConsoleColor.Gray)
        {
            const string format = "[{1}{2}]";
            int max = Console.WindowWidth - 2;

            int scaled = percent * max / 100;

            if (scaled > max)
                scaled = max;

            Log(message);
            Log(color, string.Format(format, message, new string(Characters.FullBlock, scaled), new string('.', max - scaled)));
        }

        static int cursorleft;
        static int cursortop;
        public static void SavePosition()
        {
            cursorleft = Console.CursorLeft;
            cursortop = Console.CursorTop;
        }

        public static void LoadPosition()
        {
            Console.SetCursorPosition(cursorleft, cursortop);
        }


        public static string TruncateToWindow(this string original, int offset = 0)
        {
            return Truncate(original, Console.BufferWidth - offset);
        }

        public static string Truncate(this string original, int length)
        {
            return original.Substring(0, Math.Min(original.Length, length));
        }

        public static class Characters
        {
            public const char LightShadedBlock = '\u2591';  // ░
            public const char MediumShadedBlock = '\u2592'; // ▒
            public const char DarkShadedBlock = '\u2593';   // ▓
            public const char FullBlock = '\u2588';         // █
        }

        public static void SetUnicodeOutput()
        {
            Console.OutputEncoding = System.Text.UTF8Encoding.UTF8;
        }

        public static void Clone()
        {
            var args = Environment.GetCommandLineArgs();
            var executable = args[0];
            System.Diagnostics.Process.Start(executable, string.Join(" ", args.Skip(1)));
        }
        
    }
}
