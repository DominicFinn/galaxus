// Runs the benchmark against game/data. Usage: dotnet run -c Release --project tools/bench [path to game/data]
using Galaxus.Core;
using Galaxus.Core.Content;

var data = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "../../../../../game/data");
Bench.Run(() => ContentDatabase.Load(new DirectoryContentSource(Path.GetFullPath(data))), Console.WriteLine);
