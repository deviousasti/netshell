using NetShell;
using System;
using System.Text;
using System.Threading.Tasks;

namespace example
{
    static class Program
    {
        static int Main(string[] args)
        {
            var shell = new RpcShell(new DOS()) { Prompt = Environment.CurrentDirectory };
            return shell.Run();
        }
    }
}
