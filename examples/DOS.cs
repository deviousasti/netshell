using NetShell;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace example
{
    class DOS
    {
        public string Dir => Environment.CurrentDirectory;

        public IEnumerable<string> SuggestDirs(string path) => Directory.EnumerateDirectories(Dir).Select(Path.GetFileName);
        public IEnumerable<string> SuggestFiles() => Directory.EnumerateFiles(Dir).Select(Path.GetFileName);

        [Command("echo")]
        public void Echo(string text, ConsoleColor color = ConsoleColor.White, ConsoleColor bgColor = ConsoleColor.Black)
        {
            Console.ForegroundColor = color;
            Console.BackgroundColor = bgColor;
            Console.WriteLine(text);
            Console.ResetColor();
        }

        [Command("cd")]
        public void ChangeDir([Suggest(nameof(SuggestDirs))] string directory)
        {
            var path = Path.GetFullPath(directory);
            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException($"{path} does not exist");

            RpcShell.Instance.Prompt = Environment.CurrentDirectory = path;
        }

        [Command("dir")]
        public IEnumerable<string> List(
            [Description("Accepts glob patterns")] string pattern = "*", 
            [Description("Only show files if true")] bool onlyFiles = false)
        {
            var dirs = onlyFiles ? Enumerable.Empty<string>() : Directory.EnumerateDirectories(Dir, pattern);
            var files = Directory.EnumerateFiles(Dir, pattern);
            return Enumerable.Concat(dirs, files).Select(Path.GetFileName);
        }

        [Command("exit")]
        public void Exit(Shell shell)
        {
            shell.Exit(0);
        }

        [Command("cls")]
        public void Clear()
        {
            Console.Clear();
        }

        [Command("help")]
        public string Help(string command, RpcShell shell = default)
        {
            return shell.GetHelp(command);
        }


        [DefaultCommand]
        public void Execute(string name, string[] args)
        {
            using (var process = Process.Start(new ProcessStartInfo(name, String.Join(" ", args)) { RedirectStandardOutput = true, UseShellExecute = false }))
            {
                while (!process.StandardOutput.EndOfStream)
                {
                    if (Console.KeyAvailable)
                        process.StandardInput.Write(Console.ReadKey().KeyChar);

                    if (process.StandardOutput.Peek() != -1)
                        Console.Write((char)process.StandardOutput.Read());
                }
            }
        }


    }
}
