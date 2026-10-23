namespace LrSample.Primitives;

using System.Reflection;

/// <summary>Prints which build of each library an executable loaded, to check what `lrun go` packed.</summary>
public static class BuildInfo
{
    public static string Of(Type type)
    {
        var assembly = type.Assembly;
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "?";
        return $"{assembly.GetName().Name} {version}";
    }

    public static void Print(Type entryPoint, params Type[] dependencies)
    {
        Console.WriteLine($"{Of(entryPoint)} started");
        foreach (var dependency in dependencies)
        {
            Console.WriteLine($"  uses {Of(dependency)}");
        }
    }
}
