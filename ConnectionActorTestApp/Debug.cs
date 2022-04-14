using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace P2pCommunicationPoc.Tests
{
    internal class Debug
    {
        public static void Trace(string source, string message, ConsoleColor consoleColor)
        {
            Console.ForegroundColor = consoleColor;
            var timestamp = DateTime.Now.ToString("hh:mm:ss.ffff");
            Console.WriteLine($"{timestamp};{source, -30};{message}");
        }
    }
}
