using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NetShell
{
    public class CommandBase
    {
        /// <summary>
        /// Default clear implementation
        /// </summary>
        [Command("clear", "Clear the screen")]
        public void Clear()
        {
            Console.Clear();
        }

        /// <summary>
        /// Lists available shell commands
        /// </summary>
        protected IEnumerable<string> Commands(RpcShell shell) => shell.GetCommands();

        /// <summary>
        /// Default help implementation
        /// </summary>
        [Command("help", "Lists available commands. Use help <command> for more information.")]
        public string Help
        (
            [Description("command name"), Suggest(nameof(Commands))] string command = default,
            RpcShell shell = default
        )
        {
            if (command == null)
                return shell.GetHelp();
            else
                return shell.GetHelp(command);
        }

        /// <summary>
        /// Default exit implementation
        /// </summary>
        [Command("exit", "Exit the shell (0)")]
        public void Exit(RpcShell shell)
        {
            shell.Exit(0);
        }

    }
}
