using System.Reflection;
using System.Runtime.Loader;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: ColonyRefresh.Tests <repository directory> <game Managed directory> [Debug|Release]");
    return 2;
}
string repo = Path.GetFullPath(args[0]);
string game = Path.GetFullPath(args[1]);
string bin = Path.Combine(repo, "ONI_Together", "bin", args.Length > 2 ? args[2] : "Debug", "netstandard2.1");
AssemblyLoadContext.Default.Resolving += (context, name) =>
{
    string path = Path.Combine(bin, name.Name + ".dll");
    if (name.Name is "Assembly-CSharp" or "Assembly-CSharp-firstpass")
        path = Path.Combine(repo, "PublicisedAssembly", name.Name + "_public.dll");
    if (!File.Exists(path)) path = Path.Combine(game, name.Name + ".dll");
    return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
};
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin, "ONI_Together.dll"));
var tests = assembly.GetType("ONI_Together.DebugTools.UnitTests.ColonyRefreshTests", true)!;
int passed = 0, failed = 0;
foreach (var method in tests.GetMethods(BindingFlags.Public | BindingFlags.Static))
{
    if (!method.GetCustomAttributesData().Any(a => a.AttributeType.Name == "UnitTestAttribute")) continue;
    try
    {
        object result = method.Invoke(null, null)!;
        string? state = result.GetType().GetProperty("State")!.GetValue(result)?.ToString();
        string? message = result.GetType().GetProperty("Message")!.GetValue(result)?.ToString();
        Console.WriteLine($"{state}: {method.Name} {message}");
        if (state == "Passed") passed++; else failed++;
    }
    catch (Exception exception)
    {
        failed++;
        Console.WriteLine($"Failed: {method.Name} {exception.InnerException ?? exception}");
    }
}
Console.WriteLine($"{passed} passed, {failed} failed");
return passed == 19 && failed == 0 ? 0 : 1;
