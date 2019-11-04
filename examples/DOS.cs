﻿using NetShell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace example
{
    class DOS
    {
        public string Dir => Environment.CurrentDirectory;

        public IEnumerable<string> SuggestDirs() => Directory.EnumerateDirectories(Dir).Select(Path.GetFileName);
        public IEnumerable<string> SuggestFiles() => Directory.EnumerateFiles(Dir).Select(Path.GetFileName);

        [Command("echo")]
        public void Echo(string text, ConsoleColor color = ConsoleColor.White)
        {
            Console.ForegroundColor = color;
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
        public IEnumerable<string> List(string pattern = "*")
        {
            var dirs = Directory.EnumerateDirectories(Dir, pattern);
            var files = Directory.EnumerateFiles(Dir, pattern);
            return Enumerable.Concat(dirs, files).Select(Path.GetFileName);
        }

        [Command("exit")]
        public void Exit(Shell shell)
        {
            shell.Exit(0);
        }

        
    }
}
