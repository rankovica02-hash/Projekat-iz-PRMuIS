using System;
using System.Diagnostics;
using System.IO;

namespace ClientLauncher
{
    internal class Program
    {
        static void Main(string[] args)
        {
            int n = 3;
            if (args.Length > 0) int.TryParse(args[0], out n);
            if (n < 1) n = 3;
            string exe = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..",
                "MazeClient", "bin", "Debug", "net8.0", "MazeClient.exe"));

            Console.WriteLine($"Pokrecem {n} klijenta...");
            Console.WriteLine($"Putanja: {exe}");

            if (!File.Exists(exe))
            {
                Console.WriteLine("Ne mogu da nadjem MazeClient.exe. Pokreni prvo: dotnet build");
                return;
            }

            for (int i = 1; i <= n; i++)
            {
                var p = new Process();
                p.StartInfo.FileName = exe;
                p.StartInfo.Arguments = $"{i}";
                p.StartInfo.UseShellExecute = true;
                p.Start();
                Console.WriteLine($"Pokrenut klijent #{i}");
            }
        }
    }
}
